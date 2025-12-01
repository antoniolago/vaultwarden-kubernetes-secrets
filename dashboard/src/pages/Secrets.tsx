import { useQuery } from '@tanstack/react-query'
import {
  Box,
  Typography,
  Card,
  Input,
  Chip,
  CircularProgress,
  Alert,
  Table,
  Sheet,
  Modal,
  ModalDialog,
  IconButton,
} from '@mui/joy'
import { api } from '../lib/api'
import { getStatusColor } from '../lib/utils' // formatRelative commented out - not currently used
import { useState } from 'react'
import KeysModal from '../components/KeysModal'

export default function Secrets() {
  const [searchTerm, setSearchTerm] = useState('')
  const [errorModalOpen, setErrorModalOpen] = useState(false)
  const [selectedError, setSelectedError] = useState<string | null>(null)
  const [dataKeysModalOpen, setDataKeysModalOpen] = useState(false)
  const [selectedDataKeys, setSelectedDataKeys] = useState<Array<{label: string, keys: string[]}>>([])
  const [loadingDataKeys, setLoadingDataKeys] = useState(false)
  const [dataKeysModalTitle, setDataKeysModalTitle] = useState('')
  
  const handleShowKeys = (keys: string[], namespace: string, secretName: string) => {
    setSelectedDataKeys([{
      label: `${namespace}/${secretName}`,
      keys: keys
    }])
    setDataKeysModalTitle(`${namespace}/${secretName}`)
    setDataKeysModalOpen(true)
  }
  
  const { data: secrets, isLoading, error } = useQuery({
    queryKey: ['secrets'],
    queryFn: api.getSecrets,
    refetchInterval: 30000,
  })

  const filteredSecrets = secrets?.filter(
    (secret) =>
      secret.secretName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      secret.namespace.toLowerCase().includes(searchTerm.toLowerCase()) ||
      secret.vaultwardenItemName.toLowerCase().includes(searchTerm.toLowerCase())
  )

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
        Failed to load secrets: {(error as Error).message}
      </Alert>
    )
  }

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Box>
          <Typography level="h2">Secrets</Typography>
          <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
            {secrets?.length || 0} total secrets synced
          </Typography>
        </Box>
        <Input
          placeholder="🔍 Search secrets..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          sx={{ width: 300 }}
        />
      </Box>

      <Card variant="outlined" sx={{ bgcolor: 'background.surface' }}>
        <Sheet sx={{ overflow: 'auto' }}>
          <Table hoverRow>
            <thead>
              <tr>
                <th style={{ width: 200 }}>Namespace</th>
                <th style={{ width: 250 }}>Secret Name</th>
                <th style={{ width: 200 }}>Vaultwarden Item</th>
                <th style={{ width: 100 }}>Status</th>
                <th style={{ width: 80 }}>Keys</th>
                {/* <th style={{ width: 180 }}>Last Sync</th> */}
                <th>Error</th>
              </tr>
            </thead>
            <tbody>
              {filteredSecrets && filteredSecrets.length > 0 ? (
                filteredSecrets.map((secret) => (
                  <tr key={secret.id} data-testid={`secret-row-${secret.namespace}-${secret.secretName}`}>
                    <td data-testid="secret-namespace">
                      <Chip variant="soft" size="sm" color="neutral">
                        {secret.namespace}
                      </Chip>
                    </td>
                    <td data-testid="secret-name">
                      <Typography level="body-sm" fontWeight="medium">
                        {secret.secretName}
                      </Typography>
                    </td>
                    <td data-testid="secret-vaultwarden-item">
                      <Typography level="body-sm">{secret.vaultwardenItemName}</Typography>
                    </td>
                    <td data-testid="secret-status">
                      <Chip
                        variant="soft"
                        size="sm"
                        color={getStatusColor(secret.status)}
                      >
                        {secret.status}
                      </Chip>
                    </td>
                    <td data-testid="secret-data-keys">
                      <Typography 
                        level="body-sm"
                        sx={{ 
                          cursor: secret.dataKeysCount > 0 ? 'pointer' : 'default',
                          color: secret.dataKeysCount > 0 ? 'primary.600' : 'text.primary',
                          '&:hover': secret.dataKeysCount > 0 ? {
                            textDecoration: 'underline'
                          } : {}
                        }}
                        onClick={async () => {
                          if (secret.dataKeysCount > 0) {
                            setLoadingDataKeys(true)
                            try {
                              // Fetch actual data keys from K8s secret
                              const keys = await api.getSecretDataKeys(secret.namespace, secret.secretName)
                              handleShowKeys(keys, secret.namespace, secret.secretName)
                            } catch (err) {
                              console.error('Failed to fetch data keys:', err)
                              handleShowKeys([`${secret.dataKeysCount} keys (error fetching names)`], secret.namespace, secret.secretName)
                            } finally {
                              setLoadingDataKeys(false)
                            }
                          }
                        }}
                      >
                        {secret.dataKeysCount}
                      </Typography>
                    </td>
                    {/* <td data-testid="secret-last-sync">
                      <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                        {formatRelative(secret.lastSynced || secret.lastSyncTime)}
                      </Typography>
                    </td> */}
                    <td data-testid="secret-error">
                      {(secret.lastError || secret.errorMessage) ? (
                        <Typography 
                          level="body-xs" 
                          sx={{ 
                            color: 'danger.500',
                            maxWidth: 250,
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap',
                            cursor: 'pointer',
                            '&:hover': {
                              textDecoration: 'underline'
                            }
                          }}
                          onClick={() => {
                            setSelectedError(secret.lastError || secret.errorMessage || '')
                            setErrorModalOpen(true)
                          }}
                        >
                          {secret.lastError || secret.errorMessage}
                        </Typography>
                      ) : (
                        <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>
                          -
                        </Typography>
                      )}
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={7} style={{ textAlign: 'center', padding: '2rem' }}>
                    <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                      {searchTerm ? 'No secrets found matching your search' : 'No secrets available'}
                    </Typography>
                  </td>
                </tr>
              )}
            </tbody>
          </Table>
        </Sheet>
      </Card>

      {/* Error Modal */}
      <Modal open={errorModalOpen} onClose={() => setErrorModalOpen(false)}>
        <ModalDialog sx={{ minWidth: 500, maxWidth: '80vw' }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography level="h4">Error Details</Typography>
            <IconButton onClick={() => setErrorModalOpen(false)} variant="plain" color="neutral">
              ✕
            </IconButton>
          </Box>
          <Box sx={{ 
            backgroundColor: 'danger.50', 
            p: 2, 
            borderRadius: 'md',
            border: '1px solid',
            borderColor: 'danger.300',
            maxHeight: '60vh',
            overflow: 'auto'
          }}>
            <Typography level="body-sm" sx={{ color: 'danger.700', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {selectedError}
            </Typography>
          </Box>
        </ModalDialog>
      </Modal>

      {/* Data Keys Modal */}
      <KeysModal
        open={dataKeysModalOpen}
        onClose={() => setDataKeysModalOpen(false)}
        title="Data Keys"
        subtitle={dataKeysModalTitle}
        loading={loadingDataKeys}
        items={selectedDataKeys}
        emptyMessage="No data keys found"
      />
    </Box>
  )
}
