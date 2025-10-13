import { Modal, ModalDialog, ModalClose, Typography, Table, Sheet, Chip, Box, IconButton } from '@mui/joy'
import { SecretState } from '../lib/api'
import { formatRelative } from '../lib/utils'
import { useState } from 'react'

interface SecretsModalProps {
  open: boolean
  onClose: () => void
  secrets: SecretState[]
  title: string
  namespace: string
}

export default function SecretsModal({ open, onClose, secrets, title, namespace }: SecretsModalProps) {
  const [errorModalOpen, setErrorModalOpen] = useState(false)
  const [selectedError, setSelectedError] = useState<string | null>(null)

  return (
    <>
      <Modal open={open} onClose={onClose}>
        <ModalDialog sx={{ minWidth: 700, maxWidth: '90vw' }}>
          <ModalClose />
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography level="h4">{title}</Typography>
          </Box>
          
          <Typography level="body-sm" sx={{ mb: 2, color: 'text.secondary' }}>
            Namespace: <strong>{namespace}</strong> • {secrets.length} secret{secrets.length !== 1 ? 's' : ''}
          </Typography>

        {secrets.length === 0 ? (
          <Box sx={{ textAlign: 'center', py: 4, color: 'text.tertiary' }}>
            <Typography>No secrets found</Typography>
          </Box>
        ) : (
          <Sheet sx={{ overflow: 'auto', maxHeight: '60vh' }}>
            <Table>
              <thead>
                <tr>
                  <th>Secret Name</th>
                  <th>Vaultwarden Item</th>
                  <th>Status</th>
                  <th>Data Keys</th>
                  <th>Last Synced</th>
                  <th>Error</th>
                </tr>
              </thead>
              <tbody>
                {secrets.map((secret) => (
                  <tr key={secret.id}>
                    <td>
                      <Typography fontWeight="medium">{secret.secretName}</Typography>
                    </td>
                    <td>
                      <Typography level="body-sm">{secret.vaultwardenItemName}</Typography>
                    </td>
                    <td>
                      <Chip
                        size="sm"
                        color={secret.status === 'Active' ? 'success' : 'danger'}
                        variant="soft"
                      >
                        {secret.status}
                      </Chip>
                    </td>
                    <td>{secret.dataKeysCount}</td>
                    <td>
                      <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                        {formatRelative(secret.lastSynced || secret.lastSyncTime)}
                      </Typography>
                    </td>
                    <td>
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
                ))}
              </tbody>
            </Table>
          </Sheet>
        )}
      </ModalDialog>
    </Modal>

    {/* Error Details Modal */}
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
  </>
  )
}
