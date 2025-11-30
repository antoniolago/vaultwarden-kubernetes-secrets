import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import {
  Box,
  Typography,
  Card,
  Chip,
  CircularProgress,
  Alert,
  Table,
  Sheet,
  Button,
  Modal,
  ModalDialog,
  ModalClose,
} from '@mui/joy'
import { api, SecretState } from '../lib/api'
import { formatDate, formatDuration, getStatusColor, computeSyncStatus } from '../lib/utils'
import SecretsModal from '../components/SecretsModal'
import { Trash2 } from 'lucide-react'

export default function SyncLogs() {
  const [modalOpen, setModalOpen] = useState(false)
  const [modalSecrets, setModalSecrets] = useState<SecretState[]>([])
  const [modalTitle, setModalTitle] = useState('')
  const [resetModalOpen, setResetModalOpen] = useState(false)
  const [loadingSecrets, setLoadingSecrets] = useState(false)
  const [isResetting, setIsResetting] = useState(false)
  const [resetSuccess, setResetSuccess] = useState(false)
  const [resetError, setResetError] = useState<string | null>(null)
  const { data: logs, isLoading, error } = useQuery({
    queryKey: ['sync-logs'],
    queryFn: () => api.getSyncLogs(100),
    refetchInterval: 30000,
  })

  const handleShowSecrets = async (status: 'Active' | 'Failed' | 'Deleted') => {
    setLoadingSecrets(true)
    setModalTitle(`${status} Secrets`)
    setModalOpen(true)
    
    try {
      const secrets = await api.getSecrets()
      const filteredSecrets = secrets.filter(s => s.status === status)
      setModalSecrets(filteredSecrets)
    } catch (err) {
      console.error('Failed to load secrets:', err)
      setModalSecrets([])
    } finally {
      setLoadingSecrets(false)
    }
  }

  const handleResetDatabase = async () => {
    setIsResetting(true)
    setResetError(null)
    setResetSuccess(false)
    
    try {
      await api.resetDatabase()
      setResetSuccess(true)
      setTimeout(() => {
        setResetModalOpen(false)
        setResetSuccess(false)
        window.location.reload() // Refresh to show empty state
      }, 2000)
    } catch (err) {
      setResetError('Unable to connect to API')
    } finally {
      setIsResetting(false)
    }
  }
  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  if (error) {
    return (
      <Alert color="danger">
        Failed to load sync logs: {(error as Error).message}
      </Alert>
    )
  }

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Box>
          <Typography level="h2">Sync Logs</Typography>
          <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
            {logs?.length || 0} recent sync operations
          </Typography>
        </Box>
      </Box>

      <Card variant="outlined" sx={{ bgcolor: 'background.surface' }}>
        <Sheet variant="soft" sx={{ overflow: 'auto' }}>
          <Table stripe="odd" hoverRow>
            <thead>
              <tr>
                <th style={{ width: 80 }}>ID</th>
                <th style={{ width: 180 }}>Start Time</th>
                <th style={{ width: 100 }}>Result</th>
                <th style={{ width: 100 }}>Duration</th>
                <th style={{ width: 80 }}>Total</th>
                <th style={{ width: 80 }}>Created</th>
                <th style={{ width: 80 }}>Updated</th>
                <th style={{ width: 80 }}>Skipped</th>
                <th style={{ width: 80 }}>Failed</th>
                <th style={{ width: 80 }}>Deleted</th>
                <th style={{ width: 200 }}>Message</th>
              </tr>
            </thead>
            <tbody>
              {logs && logs.length > 0 ? (
                logs.map((log) => (
                  <tr key={log.id}>
                    <td>
                      <Chip size="sm" variant="plain">
                        #{log.id}
                      </Chip>
                    </td>
                    <td>
                      <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                        {formatDate(log.startTime)}
                      </Typography>
                    </td>
                    <td>
                      <Chip
                        variant="soft"
                        size="sm"
                        color={getStatusColor(computeSyncStatus(log))}
                      >
                        {computeSyncStatus(log)}
                      </Chip>
                    </td>
                    <td>
                      <Typography level="body-sm">
                        {formatDuration(log.durationSeconds)}
                      </Typography>
                    </td>
                    <td>
                      <Typography level="body-sm">{log.totalItems}</Typography>
                    </td>
                    <td>
                      <Chip 
                        variant="soft" 
                        size="sm" 
                        color="success"
                        sx={{ cursor: log.createdSecrets > 0 ? 'pointer' : 'default' }}
                        onClick={() => log.createdSecrets > 0 && handleShowSecrets('Active')}
                      >
                        {log.createdSecrets}
                      </Chip>
                    </td>
                    <td>
                      <Chip 
                        variant="soft" 
                        size="sm" 
                        color="primary"
                        sx={{ cursor: log.updatedSecrets > 0 ? 'pointer' : 'default' }}
                        onClick={() => log.updatedSecrets > 0 && handleShowSecrets('Active')}
                      >
                        {log.updatedSecrets}
                      </Chip>
                    </td>
                    <td>
                      <Chip 
                        variant="soft" 
                        size="sm" 
                        color="neutral"
                        sx={{ cursor: log.skippedSecrets > 0 ? 'pointer' : 'default' }}
                        onClick={() => log.skippedSecrets > 0 && handleShowSecrets('Active')}
                      >
                        {log.skippedSecrets}
                      </Chip>
                    </td>
                    <td>
                      {log.failedSecrets > 0 ? (
                        <Chip 
                          variant="soft" 
                          size="sm" 
                          color="danger"
                          sx={{ cursor: 'pointer' }}
                          onClick={() => handleShowSecrets('Failed')}
                        >
                          {log.failedSecrets}
                        </Chip>
                      ) : (
                        <Typography level="body-sm" sx={{ color: 'text.tertiary' }}>
                          0
                        </Typography>
                      )}
                    </td>
                    <td>
                      {log.deletedSecrets > 0 ? (
                        <Chip 
                          variant="soft" 
                          size="sm" 
                          color="warning"
                          sx={{ 
                            cursor: 'pointer',
                            '&:hover': {
                              backgroundColor: 'warning.200'
                            }
                          }}
                          onClick={() => handleShowSecrets('Deleted')}
                        >
                          {log.deletedSecrets}
                        </Chip>
                      ) : (
                        <Typography level="body-sm" sx={{ color: 'text.tertiary' }}>
                          0
                        </Typography>
                      )}
                    </td>
                    <td>
                      {log.errorMessage && (
                        <Chip 
                          variant="soft" 
                          size="sm" 
                          color={log.totalItems === 0 && log.errorMessage.includes('No items found') ? 'warning' : 'neutral'}
                          title={log.errorMessage}
                        >
                          {log.errorMessage.length > 25 ? log.errorMessage.substring(0, 25) + '...' : log.errorMessage}
                        </Chip>
                      )}
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={11} style={{ textAlign: 'center', padding: '2rem' }}>
                    <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                      No sync logs available
                    </Typography>
                  </td>
                </tr>
              )}
            </tbody>
          </Table>
        </Sheet>
      </Card>

      <Button
        onClick={() => setResetModalOpen(true)}
        color="danger"
        variant="soft"
        sx={{ gap: 1, mt: 4 }}
      >
        <Trash2 size={18} />
        Reset Sync Logs and Statistics
      </Button>
              
      {/* Secrets Modal */}
      <SecretsModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        secrets={loadingSecrets ? [] : modalSecrets}
        title={loadingSecrets ? 'Loading...' : modalTitle}
        namespace="All"
      />

      {/* Reset Database Modal */}
      <Modal open={resetModalOpen} onClose={() => setResetModalOpen(false)}>
        <ModalDialog sx={{ minWidth: 500 }}>
          <ModalClose />
          <Typography level="h4" sx={{ mb: 2 }}>Reset Database</Typography>
          
          {resetSuccess ? (
            <Alert color="success" sx={{ mb: 2 }}>
              Database reset successfully! Refreshing...
            </Alert>
          ) : (
            <>
              <Alert color="warning" sx={{ mb: 2 }}>
                <Typography level="body-md" fontWeight="bold">Warning: This action cannot be undone!</Typography>
                <Typography level="body-sm" sx={{ mt: 1 }}>
                  This will permanently delete all:
                </Typography>
                <Box component="ul" sx={{ mt: 0.5, pl: 2 }}>
                  <li>Sync logs and history</li>
                  <li>Secret state tracking data</li>
                  <li>Cached Vaultwarden items</li>
                </Box>
                <Typography level="body-sm" sx={{ mt: 1, fontStyle: 'italic' }}>
                  Note: Your actual Kubernetes secrets and Vaultwarden items will NOT be affected.
                </Typography>
              </Alert>
              
              {resetError && (
                <Alert color="danger" sx={{ mb: 2 }}>
                  {resetError}
                </Alert>
              )}
              
              <Box sx={{ display: 'flex', gap: 2, justifyContent: 'flex-end' }}>
                <Button
                  variant="outlined"
                  color="neutral"
                  onClick={() => setResetModalOpen(false)}
                  disabled={isResetting}
                >
                  Cancel
                </Button>
                <Button
                  variant="solid"
                  color="danger"
                  onClick={handleResetDatabase}
                  loading={isResetting}
                >
                  {isResetting ? 'Resetting...' : 'Reset Database'}
                </Button>
              </Box>
            </>
          )}
        </ModalDialog>
      </Modal>
    </Box>
  )
}
