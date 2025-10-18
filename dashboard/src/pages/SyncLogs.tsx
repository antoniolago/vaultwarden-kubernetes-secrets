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
} from '@mui/joy'
import { api, SecretState } from '../lib/api'
import { formatDate, formatDuration, getStatusColor, computeSyncStatus } from '../lib/utils'
import SecretsModal from '../components/SecretsModal'

export default function SyncLogs() {
  const [modalOpen, setModalOpen] = useState(false)
  const [modalSecrets, setModalSecrets] = useState<SecretState[]>([])
  const [modalTitle, setModalTitle] = useState('')
  const [loadingSecrets, setLoadingSecrets] = useState(false)

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
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={10} style={{ textAlign: 'center', padding: '2rem' }}>
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

      {/* Secrets Modal */}
      <SecretsModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        secrets={loadingSecrets ? [] : modalSecrets}
        title={loadingSecrets ? 'Loading...' : modalTitle}
        namespace="All"
      />
    </Box>
  )
}
