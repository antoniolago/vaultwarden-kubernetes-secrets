import { useEffect, useState, useRef } from 'react'
import { Card, CardContent, Typography, LinearProgress, Box, Chip, CircularProgress, Modal, ModalDialog, ModalClose, Sheet, IconButton } from '@mui/joy'
import { Info } from 'lucide-react'
import SyncOutputModal from './SyncOutputModal'

interface SyncStatus {
  syncIntervalSeconds: number
  continuousSync: boolean
  lastSyncTime: string | null
  nextSyncTime: string | null
}

interface RecentSync {
  id: number
  startTime: string
  status: string
  createdSecrets: number
  updatedSecrets: number
  failedSecrets: number
  durationSeconds: number
  errorMessage?: string
}

interface OverviewData {
  averageSyncDuration: number
  recentActivity: RecentSync[]
}

type SyncState = 'idle' | 'syncing' | 'error'

export default function SyncProgressBar() {
  const [syncStatus, setSyncStatus] = useState<SyncStatus | null>(null)
  const [progress, setProgress] = useState(0)
  const [timeRemaining, setTimeRemaining] = useState<string>('')
  const [secondsRemaining, setSecondsRemaining] = useState<number>(0)
  const [error, setError] = useState<string | null>(null)
  const [syncState, setSyncState] = useState<SyncState>('idle')
  const [recentSync, setRecentSync] = useState<RecentSync | null>(null)
  const [errorModalOpen, setErrorModalOpen] = useState(false)
  const [syncOutputModalOpen, setSyncOutputModalOpen] = useState(false)
  const [averageDuration, setAverageDuration] = useState<number>(0)
  const [syncStartTime, setSyncStartTime] = useState<Date | null>(null)
  const currentSyncIdRef = useRef<number | null>(null)
  const lastSyncTimeRef = useRef<string | null>(null)
  const pollingIntervalRef = useRef<number>(5000)

  // Fetch sync status and recent logs from API
  useEffect(() => {
    const fetchData = async () => {
      try {
        // Fetch sync status
        const statusResponse = await fetch('http://localhost:8080/api/dashboard/sync-status')
        if (statusResponse.ok) {
          const data: SyncStatus = await statusResponse.json()
          console.log('[SyncBar] Sync status:', {
            lastSyncTime: data.lastSyncTime,
            nextSyncTime: data.nextSyncTime,
            interval: data.syncIntervalSeconds,
            continuous: data.continuousSync
          })
          setSyncStatus(data)
          setError(null)
        } else {
          setError('Failed to fetch sync status')
        }

        // Fetch recent sync logs to determine current state
        const overviewResponse = await fetch('http://localhost:8080/api/dashboard/overview')
        if (overviewResponse.ok) {
          const overview: OverviewData = await overviewResponse.json()
          setAverageDuration(overview.averageSyncDuration || 5)
          
          if (overview.recentActivity && overview.recentActivity.length > 0) {
            const mostRecent = overview.recentActivity[0]
            setRecentSync(mostRecent)
            
            // Determine sync state based on status
            if (mostRecent.status === 'InProgress') {
              // Sync is actively running
              if (currentSyncIdRef.current !== mostRecent.id) {
                // New sync detected
                const startTimeStr = mostRecent.startTime.endsWith('Z') 
                  ? mostRecent.startTime 
                  : mostRecent.startTime + 'Z'
                setSyncStartTime(new Date(startTimeStr))
                currentSyncIdRef.current = mostRecent.id
                console.log('[SyncBar] New sync detected, ID:', mostRecent.id, 'StartTime:', startTimeStr)
              }
              setSyncState('syncing')
              // Poll aggressively during sync (every 1s)
              pollingIntervalRef.current = 1000
            } else {
              // Sync completed or failed
              const newState = mostRecent.status === 'Failed' ? 'error' : 'idle'
              console.log('[SyncBar] Sync completed/failed, Status:', mostRecent.status, 'New state:', newState)
              setSyncState(newState)
              setSyncStartTime(null)
              currentSyncIdRef.current = null
              // Reset to normal polling
              pollingIntervalRef.current = 5000
            }
          }
        }
      } catch (err) {
        setError('Unable to connect to API')
      }
    }

    // Initial fetch
    fetchData()
    
    // Set up polling with dynamic interval - need to use a different approach
    // since setInterval captures the initial value
    let timeoutId: NodeJS.Timeout
    const scheduleFetch = () => {
      timeoutId = setTimeout(() => {
        fetchData().then(scheduleFetch)
      }, pollingIntervalRef.current)
    }
    
    scheduleFetch()

    return () => clearTimeout(timeoutId)
  }, [])

  // Dynamic polling adjustment based on proximity to next sync
  useEffect(() => {
    if (!syncStatus || syncState === 'syncing') return

    const updatePollingRate = () => {
      const now = new Date()
      let nextSyncTime: Date | null = null

      if (syncStatus.nextSyncTime) {
        nextSyncTime = new Date(syncStatus.nextSyncTime.endsWith('Z') 
          ? syncStatus.nextSyncTime 
          : syncStatus.nextSyncTime + 'Z')
      } else if (syncStatus.lastSyncTime) {
        const lastSync = new Date(syncStatus.lastSyncTime.endsWith('Z') 
          ? syncStatus.lastSyncTime 
          : syncStatus.lastSyncTime + 'Z')
        nextSyncTime = new Date(lastSync.getTime() + (syncStatus.syncIntervalSeconds * 1000))
      }

      if (nextSyncTime) {
        const timeUntilSync = nextSyncTime.getTime() - now.getTime()
        
        // Aggressive polling near sync time
        if (timeUntilSync <= 10000) {
          pollingIntervalRef.current = 1000 // 1s when sync expected within 10s
        } else if (timeUntilSync <= 30000) {
          pollingIntervalRef.current = 2000 // 2s when within 30s
        } else if (timeUntilSync <= 60000) {
          pollingIntervalRef.current = 5000 // 5s when within 1min
        } else {
          pollingIntervalRef.current = 10000 // 10s otherwise
        }
      }
    }

    updatePollingRate()
    const intervalId = setInterval(updatePollingRate, 1000)

    return () => clearInterval(intervalId)
  }, [syncStatus, syncState])

  // Update progress and time remaining smoothly
  useEffect(() => {
    if (!syncStatus) {
      return
    }
    
    const updateProgress = () => {
      const now = new Date()
      
      // Detect if lastSyncTime changed (new cycle started)
      if (syncStatus.lastSyncTime && syncStatus.lastSyncTime !== lastSyncTimeRef.current) {
        lastSyncTimeRef.current = syncStatus.lastSyncTime
        // Reset sync state for new cycle
        if (syncState === 'syncing') {
          setSyncState('idle')
          setSyncStartTime(null)
          currentSyncIdRef.current = null
        }
      }
      
      // For countdown progress, we need lastSyncTime and nextSyncTime
      let totalDuration = 0
      let remaining = 0
      
      if (syncStatus.lastSyncTime && syncStatus.nextSyncTime) {
        // Ensure dates are parsed as UTC
        const lastSync = new Date(syncStatus.lastSyncTime.endsWith('Z')
          ? syncStatus.lastSyncTime
          : syncStatus.lastSyncTime + 'Z')
        const nextSync = new Date(syncStatus.nextSyncTime.endsWith('Z')
          ? syncStatus.nextSyncTime
          : syncStatus.nextSyncTime + 'Z')

        // For countdown, totalDuration is always the sync interval
        totalDuration = syncStatus.syncIntervalSeconds * 1000
        remaining = nextSync.getTime() - now.getTime()
        
        if (syncState === 'idle' && Math.random() < 0.01) { // Log occasionally to avoid spam
          console.log('[SyncBar] Countdown calc:', {
            lastSync: lastSync.toISOString(),
            nextSync: nextSync.toISOString(),
            now: now.toISOString(),
            remaining: Math.floor(remaining / 1000) + 's',
            totalDuration: Math.floor(totalDuration / 1000) + 's'
          })
        }
      } else if (syncStatus.lastSyncTime) {
        // Calculate nextSyncTime for countdown display
        const lastSync = new Date(syncStatus.lastSyncTime.endsWith('Z') 
          ? syncStatus.lastSyncTime 
          : syncStatus.lastSyncTime + 'Z')
        const calculatedNext = new Date(lastSync.getTime() + (syncStatus.syncIntervalSeconds * 1000))
        
        totalDuration = syncStatus.syncIntervalSeconds * 1000
        remaining = calculatedNext.getTime() - now.getTime()
      }

      // Calculate progress percentage
      let progressPercent = 0
      
      if (syncState === 'syncing' && syncStartTime) {
        // Syncing with confirmed start time - show real progress
        const syncElapsed = (now.getTime() - syncStartTime.getTime()) / 1000
        const avgDuration = averageDuration || 5
        progressPercent = Math.min(100, (syncElapsed / avgDuration) * 100)
      } else if (syncState !== 'syncing' && totalDuration > 0) {
        // Idle state - countdown to next sync
        // Progress from 0 to 100 as we approach next sync
        const elapsed = totalDuration - remaining
        // Keep at 100% when waiting for sync to start (remaining <= 0)
        if (remaining <= 0) {
          progressPercent = 100
        } else {
          progressPercent = Math.min(100, Math.max(0, (elapsed / totalDuration) * 100))
        }
      }
      
      const clampedProgress = Math.max(0, Math.min(100, progressPercent))
      setProgress(clampedProgress)

      // Format time remaining
      if (syncState === 'syncing' && syncStartTime) {
        // Syncing - show estimated time to completion
        const syncElapsed = (now.getTime() - syncStartTime.getTime()) / 1000
        const avgDuration = averageDuration || 5
        const syncRemaining = Math.max(0, avgDuration - syncElapsed)
        const syncRemainingSeconds = Math.ceil(syncRemaining)
        
        setSecondsRemaining(syncRemainingSeconds)
        if (syncRemainingSeconds > 0) {
          setTimeRemaining(`~${syncRemainingSeconds}s`)
        } else {
          setTimeRemaining('Finishing...')
        }
      } else if (syncState !== 'syncing' && remaining <= 0) {
        // Countdown reached 0 but not syncing yet
        setTimeRemaining('Starting...')
        setSecondsRemaining(0)
      } else if (syncState !== 'syncing') {
        // Normal countdown - show time until next sync
        const remainingSeconds = Math.floor(remaining / 1000)
        setSecondsRemaining(remainingSeconds)
        const minutes = Math.floor(remainingSeconds / 60)
        const seconds = remainingSeconds % 60

        if (minutes >= 60) {
          const hours = Math.floor(minutes / 60)
          const mins = minutes % 60
          setTimeRemaining(`${hours}h ${mins}m`)
        } else if (minutes > 0) {
          setTimeRemaining(`${minutes}m ${seconds}s`)
        } else {
          setTimeRemaining(`${seconds}s`)
        }
      }
    }

    updateProgress()
    const interval = setInterval(updateProgress, 100) // Update every 100ms for smooth animation

    return () => clearInterval(interval)
  }, [syncStatus, syncState, syncStartTime, averageDuration])

  if (error) {
    return (
      <Card 
        variant="soft" 
        sx={{ 
          bgcolor: 'danger.softBg',
          borderLeft: '4px solid',
          borderColor: 'danger.solidBg',
        }}
      >
        <CardContent sx={{ py: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <CircularProgress size="sm" color="danger" />
            <Box sx={{ flex: 1 }}>
              <Typography level="title-sm" fontWeight="bold" color="danger">
                Sync Status Unavailable
              </Typography>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                {error}
              </Typography>
            </Box>
          </Box>
        </CardContent>
      </Card>
    )
  }

  if (!syncStatus) {
    return null
  }

  // Show continuous sync status
  if (syncStatus.continuousSync) {
    const isSyncing = syncState === 'syncing'
    const hasError = syncState === 'error'
    const isIdle = syncState === 'idle'
    
    // Determine last sync result for idle state
    let lastSyncResult = ''
    if (isIdle && recentSync) {
      const hasFailures = recentSync.failedSecrets > 0
      const hasSuccess = recentSync.createdSecrets > 0 || recentSync.updatedSecrets > 0
      if (hasFailures && hasSuccess) {
        lastSyncResult = '⚠️ Partial'
      } else if (hasSuccess) {
        lastSyncResult = '✓ Success'
      } else {
        lastSyncResult = '— No changes'
      }
    }
    
    // In continuous mode, show countdown when idle, spinner when syncing
    return (
      <>
        <Card 
          variant="soft" 
          sx={{ 
            bgcolor: hasError ? 'danger.softBg' : isSyncing ? 'success.softBg' : 'warning.softBg',
            borderLeft: '4px solid',
            borderColor: hasError ? 'danger.solidBg' : isSyncing ? 'success.solidBg' : 'warning.solidBg',
            cursor: 'pointer',
            transition: 'all 0.3s ease',
            p: 0.7,
            '&:hover': {
              transform: 'scale(1.01)',
              boxShadow: 'sm',
            }
          }}
          onClick={() => hasError ? setErrorModalOpen(true) : setSyncOutputModalOpen(true)}
        >
          <CardContent sx={{ py: 1.5 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              {/* Show spinner only when syncing or error */}
              {isSyncing && <CircularProgress size="sm" color="success" />}
              {hasError && <CircularProgress size="sm" color="danger" />}
              
              <Box sx={{ flex: 1 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.5 }}>
                  <Typography level="body-xs" fontWeight="bold" color={hasError ? 'danger' : isSyncing ? 'success' : 'warning'}>
                    {hasError ? 'Sync Failed' : isSyncing ? 'Syncing Now' : 'Waiting next sync'}
                    {isIdle && lastSyncResult && (
                      <Typography component="span" level="body-xs" sx={{ ml: 1, fontWeight: 'normal' }}>
                        {lastSyncResult}
                      </Typography>
                    )}
                  </Typography>
                  {/* {!hasError && (
                    <Chip 
                      size="sm" 
                      variant="solid"
                      color={isSyncing ? 'success' : 'warning'}
                      sx={{ fontWeight: 'bold', minWidth: 60, textAlign: 'center' }}
                    >
                      {isSyncing ? 'Syncing' : timeRemaining || 'Checking...'}
                    </Chip>
                  )}
                  {hasError && (
                    <Chip 
                      size="sm" 
                      variant="solid"
                      color="danger"
                      sx={{ fontWeight: 'bold', minWidth: 60, textAlign: 'center' }}
                    >
                      Error
                    </Chip> */}
                  {/* )} */}
                </Box>
                
                {!hasError && (
                  <LinearProgress 
                    determinate
                    value={Number(progress) || 0}
                    variant="soft"
                    size="sm"
                    sx={{ 
                      mb: 0.5,
                      '--LinearProgress-thickness': '6px'
                    }}
                    color={isSyncing ? 'success' : 'warning'}
                  />
                )}
                
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                    {hasError 
                      ? 'Click to view error' 
                      : isSyncing 
                        ? `Syncing... ${timeRemaining} • Click for logs` 
                        : `Next: ${timeRemaining || 'ERROR'} • Click for logs`}
                  </Typography>
                  {!hasError && (
                    <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                      {Math.round(progress)}%
                    </Typography>
                  )}
                </Box>
              </Box>
              
              {hasError && (
                <IconButton size="sm" color="danger" variant="soft">
                  <Info size={18} />
                </IconButton>
              )}
            </Box>
          </CardContent>
        </Card>
        
        {/* Error Modal */}
        <Modal open={errorModalOpen} onClose={() => setErrorModalOpen(false)}>
          <ModalDialog sx={{ minWidth: 500, maxWidth: 700 }}>
            <ModalClose />
            <Typography level="h4" sx={{ mb: 2 }}>Sync Error Details</Typography>
            <Sheet variant="soft" color="danger" sx={{ p: 2, borderRadius: 'sm', maxHeight: 400, overflow: 'auto' }}>
              <Typography level="body-sm" sx={{ fontFamily: 'monospace', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
                {recentSync?.errorMessage || 'No error details available'}
              </Typography>
            </Sheet>
            {recentSync && (
              <Box sx={{ mt: 2, display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                <Chip size="sm" variant="soft">Failed: {recentSync.failedSecrets}</Chip>
                <Chip size="sm" variant="soft">Created: {recentSync.createdSecrets}</Chip>
                <Chip size="sm" variant="soft">Updated: {recentSync.updatedSecrets}</Chip>
                <Chip size="sm" variant="soft">Duration: {recentSync.durationSeconds.toFixed(2)}s</Chip>
              </Box>
            )}
          </ModalDialog>
        </Modal>

        {/* Sync Output Modal */}
        <SyncOutputModal 
          open={syncOutputModalOpen} 
          onClose={() => setSyncOutputModalOpen(false)} 
        />
      </>
    )
  }

  if (!syncStatus.lastSyncTime || !syncStatus.nextSyncTime) {
    return (
      <Card 
        variant="soft" 
        sx={{ 
          bgcolor: 'neutral.softBg',
          borderLeft: '4px solid',
          borderColor: 'neutral.outlinedBorder',
        }}
      >
        <CardContent sx={{ py: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <Box sx={{ 
              width: 32, 
              height: 32, 
              borderRadius: '50%', 
              bgcolor: 'neutral.softBg',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '1.2rem'
            }}>
              ⏳
            </Box>
            <Box sx={{ flex: 1 }}>
              <Typography level="title-sm" fontWeight="bold">
                Sync Status
              </Typography>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                Waiting for first sync...
              </Typography>
            </Box>
          </Box>
        </CardContent>
      </Card>
    )
  }

  const isSyncing = syncState === 'syncing' || progress >= 99
  const hasError = syncState === 'error'
  const progressColor = hasError ? 'danger' : isSyncing ? 'success' : secondsRemaining <= 10 ? 'warning' : 'primary'

  return (
    <>
      <Card 
        variant="soft" 
        sx={{ 
          bgcolor: `${progressColor}.softBg`,
          borderLeft: '4px solid',
          borderColor: `${progressColor}.solidBg`,
          transition: 'all 0.3s ease',
          p: 0.7,
          cursor: 'pointer',
          '&:hover': {
            transform: 'scale(1.01)',
            boxShadow: 'sm',
          }
        }}
        onClick={() => hasError ? setErrorModalOpen(true) : setSyncOutputModalOpen(true)}
      >
        <CardContent sx={{ py: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            {/* Status Icon */}
            {isSyncing ? (
              <CircularProgress size="sm" color="success" />
            ) : hasError ? (
              <CircularProgress size="sm" color="danger" />
            ) : null}

            {/* Info & Progress */}
            <Box sx={{ flex: 1 }}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.5 }}>
                <Typography level="title-sm" fontWeight="bold" color={progressColor}>
                  {hasError ? 'Sync Failed' : isSyncing ? 'Syncing Now' : 'Next Sync'}
                </Typography>
                <Chip 
                  size="sm" 
                  variant="solid"
                  color={progressColor}
                  sx={{ fontWeight: 'bold', minWidth: 60, textAlign: 'center' }}
                >
                  {hasError ? 'Error' : timeRemaining}
                </Chip>
              </Box>
              
              <LinearProgress 
                determinate 
                value={Number(progress) || 0}
                variant="soft"
                size="sm"
                sx={{ 
                  mb: 0.5,
                  '--LinearProgress-thickness': '6px'
                }}
                color={progressColor}
              />
              
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                  {hasError ? 'Click to view error' : 'Click to view sync logs'}
                </Typography>
                <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                  {Math.round(progress)}%
                </Typography>
              </Box>
            </Box>
            
            {hasError && (
              <IconButton size="sm" color="danger" variant="soft">
                <Info size={18} />
              </IconButton>
            )}
          </Box>
        </CardContent>
      </Card>
      
      {/* Error Modal */}
      <Modal open={errorModalOpen} onClose={() => setErrorModalOpen(false)}>
        <ModalDialog sx={{ minWidth: 500, maxWidth: 700 }}>
          <ModalClose />
          <Typography level="h4" sx={{ mb: 2 }}>Sync Error Details</Typography>
          <Sheet variant="soft" color="danger" sx={{ p: 2, borderRadius: 'sm', maxHeight: 400, overflow: 'auto' }}>
            <Typography level="body-sm" sx={{ fontFamily: 'monospace', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {recentSync?.errorMessage || 'No error details available'}
            </Typography>
          </Sheet>
          {recentSync && (
            <Box sx={{ mt: 2, display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Chip size="sm" variant="soft">Failed: {recentSync.failedSecrets}</Chip>
              <Chip size="sm" variant="soft">Created: {recentSync.createdSecrets}</Chip>
              <Chip size="sm" variant="soft">Updated: {recentSync.updatedSecrets}</Chip>
              <Chip size="sm" variant="soft">Duration: {recentSync.durationSeconds.toFixed(2)}s</Chip>
            </Box>
          )}
        </ModalDialog>
      </Modal>

      {/* Sync Output Modal */}
      <SyncOutputModal 
        open={syncOutputModalOpen} 
        onClose={() => setSyncOutputModalOpen(false)} 
      />
    </>
  )
}
