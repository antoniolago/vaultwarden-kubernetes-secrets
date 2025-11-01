import { useEffect, useState, useRef } from 'react'
import { Modal, ModalDialog, ModalClose, Typography, Box, Sheet, CircularProgress, Chip, Tabs, TabList, TabPanel } from '@mui/joy'
import { useQuery, useQueryClient } from '@tanstack/react-query'

interface SyncLog {
  id: number
  startTime: string
  endTime?: string
  status: string
  totalItems: number
  createdSecrets: number
  updatedSecrets: number
  skippedSecrets: number
  failedSecrets: number
  deletedSecrets: number
  durationSeconds: number
  errorMessage?: string
}

interface SyncOutputModalProps {
  open: boolean
  onClose: () => void
}

export default function SyncOutputModal({ open, onClose }: SyncOutputModalProps) {
  const queryClient = useQueryClient()
  const [activeTab, setActiveTab] = useState(1)
  const [consoleOutput, setConsoleOutput] = useState<string[]>([])
  const [wsConnected, setWsConnected] = useState(false)
  const [wsError, setWsError] = useState<string | null>(null)
  const wsRef = useRef<WebSocket | null>(null)
  const consoleContainerRef = useRef<HTMLDivElement>(null)
  const previousSyncStatusRef = useRef<string | null>(null)

  // Fetch recent sync logs (for summary tab)
  const { data: logs, isLoading } = useQuery({
    queryKey: ['sync-logs-live'],
    queryFn: async (): Promise<SyncLog[]> => {
      const response = await fetch('http://localhost:8080/api/SyncLogs?count=10')
      if (!response.ok) throw new Error('Failed to fetch sync logs')
      return response.json()
    },
    refetchInterval: open ? 2000 : false, // Poll every 2s when modal is open
    enabled: open,
  })

  // WebSocket connection for real-time console output
  useEffect(() => {
    if (!open) return

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const wsUrl = `${protocol}//localhost:8080/api/sync-output/stream`
    
    try {
      const ws = new WebSocket(wsUrl)
      wsRef.current = ws

      ws.onopen = () => {
        console.log('[SyncOutput] WebSocket connected')
        setWsConnected(true)
        setWsError(null)
      }

      ws.onmessage = (event) => {
        const message = event.data.trim()
        
        // Handle special messages
        if (message.startsWith('__REDIS_NOT_CONFIGURED__')) {
          const errorLines = message.split('\n').filter((line: string) => !line.startsWith('__'))
          setWsError('Redis Not Configured')
          setConsoleOutput(errorLines)
          setWsConnected(false)
          return
        }
        
        if (message === '__CLEAR__') {
          setConsoleOutput([])
        } else if (message) {
          setConsoleOutput(prev => [...prev, message])
        }
      }

      ws.onerror = (error) => {
        console.error('[SyncOutput] WebSocket error:', error)
        setWsError('Connection Error')
        setWsConnected(false)
      }

      ws.onclose = (event) => {
        console.log('[SyncOutput] WebSocket closed:', event.code, event.reason)
        setWsConnected(false)
        
        // If closed due to policy violation (Redis not configured), keep the error message
        if (event.code === 1008 && !wsError) {
          setWsError('Service Unavailable')
        }
      }
    } catch (error) {
      console.error('[SyncOutput] Failed to create WebSocket:', error)
      setWsError('Failed to connect to sync output stream')
    }

    return () => {
      if (wsRef.current) {
        wsRef.current.close()
        wsRef.current = null
      }
    }
  }, [open])

  // Auto scroll console output
  useEffect(() => {
    if (consoleContainerRef.current && activeTab === 1) {
      consoleContainerRef.current.scrollTop = consoleContainerRef.current.scrollHeight
    }
  }, [consoleOutput, activeTab])

  const mostRecent = logs?.[0]
  const isSyncing = mostRecent?.status === 'InProgress'

  // Invalidate sync logs cache when sync completes
  useEffect(() => {
    if (!mostRecent) return

    const currentStatus = mostRecent.status
    const previousStatus = previousSyncStatusRef.current

    // Detect transition from InProgress to completed (Success or Failed)
    if (previousStatus === 'InProgress' && currentStatus !== 'InProgress') {
      console.log('[SyncOutput] Sync completed, invalidating caches...')
      
      // Invalidate all sync-related queries to refresh the UI
      queryClient.invalidateQueries({ queryKey: ['sync-logs'] })
      queryClient.invalidateQueries({ queryKey: ['sync-logs-live'] })
      queryClient.invalidateQueries({ queryKey: ['dashboard-overview'] })
      queryClient.invalidateQueries({ queryKey: ['secrets'] })
    }

    // Update previous status
    previousSyncStatusRef.current = currentStatus
  }, [mostRecent, queryClient])

  return (
    <Modal open={open} onClose={onClose}>
      <ModalDialog sx={{ minWidth: 800, maxWidth: '90vw', maxHeight: '85vh' }}>
        <ModalClose />
        <Typography level="h4" sx={{ mb: 2 }}>
          {isSyncing ? '🔄 Sync In Progress' : '📋 Sync Activity'}
        </Typography>

        <Tabs value={activeTab} onChange={(_, value) => setActiveTab(value as number)}>
          <TabList>
            {/* <Tab>Summary</Tab> */}
            {/* <Tab>
              Console Output
              {wsConnected && <Chip size="sm" color="success" variant="soft" sx={{ ml: 1 }}>Live</Chip>}
              {wsError && <Chip size="sm" color="warning" variant="soft" sx={{ ml: 1 }}>Offline</Chip>}
            </Tab> */}
          </TabList>

          {/* Summary Tab - Sync Logs */}
          <TabPanel value={0}>
            {isLoading ? (
              <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
                <CircularProgress />
              </Box>
            ) : (
              <Sheet
                variant="soft"
                sx={{
                  p: 2,
                  borderRadius: 'sm',
                  maxHeight: '55vh',
                  overflow: 'auto',
                  bgcolor: 'neutral.900',
                  color: 'neutral.50',
                  fontFamily: 'monospace',
                  fontSize: '0.875rem',
                }}
              >
            {logs && logs.length > 0 ? (
              logs.map((log) => (
                <Box
                  key={log.id}
                  sx={{
                    mb: 2,
                    pb: 2,
                    borderBottom: '1px solid',
                    borderColor: 'neutral.700',
                    '&:last-child': { borderBottom: 'none', mb: 0, pb: 0 },
                  }}
                >
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
                    <Typography level="body-sm" sx={{ color: 'primary.300' }}>
                      [{new Date(log.startTime).toLocaleString()}]
                    </Typography>
                    <Chip
                      size="sm"
                      variant="soft"
                      color={
                        log.status === 'InProgress'
                          ? 'primary'
                          : log.status === 'Success'
                          ? 'success'
                          : 'danger'
                      }
                    >
                      {log.status}
                    </Chip>
                  </Box>

                  {log.status === 'InProgress' && (
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                      <CircularProgress size="sm" />
                      <Typography level="body-sm" sx={{ color: 'warning.300' }}>
                        Syncing {log.totalItems} items...
                      </Typography>
                    </Box>
                  )}

                  {log.status !== 'InProgress' && (
                    <>
                      <Typography level="body-sm" sx={{ color: 'success.200', mt: 0.5 }}>
                        ✓ Created: {log.createdSecrets} | Updated: {log.updatedSecrets} | Skipped: {log.skippedSecrets}
                      </Typography>
                      {log.failedSecrets > 0 && (
                        <Typography level="body-sm" sx={{ color: 'danger.300', mt: 0.5 }}>
                          ✗ Failed: {log.failedSecrets}
                        </Typography>
                      )}
                      {log.deletedSecrets > 0 && (
                        <Typography level="body-sm" sx={{ color: 'warning.300', mt: 0.5 }}>
                          🗑 Deleted: {log.deletedSecrets}
                        </Typography>
                      )}
                      <Typography level="body-sm" sx={{ color: 'neutral.400', mt: 0.5 }}>
                        Duration: {log.durationSeconds.toFixed(2)}s | Total: {log.totalItems}
                      </Typography>
                    </>
                  )}

                  {log.errorMessage && (
                    <Typography
                      level="body-sm"
                      sx={{
                        color: 'danger.200',
                        mt: 1,
                        p: 1,
                        bgcolor: 'danger.900',
                        borderRadius: 'sm',
                      }}
                    >
                      Error: {log.errorMessage}
                    </Typography>
                  )}
                </Box>
              ))
            ) : (
              <Typography level="body-sm" sx={{ color: 'neutral.400', textAlign: 'center', py: 4 }}>
                No sync logs available
              </Typography>
            )}
          </Sheet>
        )}
          </TabPanel>

          {/* Console Output Tab - Real-time stream */}
          <TabPanel value={1}>
            <Sheet
              ref={consoleContainerRef}
              variant="soft"
              sx={{
                p: 2,
                borderRadius: 'sm',
                maxHeight: '55vh',
                overflow: 'auto',
                bgcolor: 'neutral.900',
                color: 'neutral.50',
                fontFamily: 'monospace',
                fontSize: '0.875rem',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
              }}
            >
              {wsError && (
                <Typography level="body-sm" sx={{ color: 'warning.300', mb: 2 }}>
                  ⚠️ {wsError}
                </Typography>
              )}
              {consoleOutput.length === 0 ? (
                <Typography level="body-sm" sx={{ color: 'neutral.400', textAlign: 'center', py: 4 }}>
                  {wsConnected ? 'Waiting for sync output...' : 'Connect to Redis to see live output'}
                </Typography>
              ) : (
                consoleOutput.map((line, index) => (
                  <Box key={index} sx={{ mb: 0.25 }}>
                    {line}
                  </Box>
                ))
              )}
            </Sheet>
          </TabPanel>
        </Tabs>

        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: 2 }}>
          <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
            {activeTab === 0 
              ? (isSyncing ? 'Updating every 2s...' : 'Showing last 10 syncs')
              : (wsConnected ? '🟢 Live stream active' : 'Offline - configure Redis for live output')}
          </Typography>
        </Box>
      </ModalDialog>
    </Modal>
  )
}
