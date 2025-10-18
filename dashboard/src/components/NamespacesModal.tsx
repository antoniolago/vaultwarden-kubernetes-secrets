import { Modal, ModalDialog, ModalClose, Typography, Box, Sheet, Table, Chip } from '@mui/joy'

interface NamespaceInfo {
  namespace: string
  secretName: string
  status: string
  dataKeysCount?: number
}

interface NamespacesModalProps {
  open: boolean
  onClose: () => void
  itemName: string
  namespaces: NamespaceInfo[]
  onViewKeys?: (namespace: string, secretName: string, itemName: string) => void
}

export default function NamespacesModal({ 
  open, 
  onClose, 
  itemName, 
  namespaces,
  onViewKeys 
}: NamespacesModalProps) {
  return (
    <Modal open={open} onClose={onClose}>
      <ModalDialog sx={{ minWidth: 600, maxWidth: '90vw' }}>
        <ModalClose />
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography level="h4">Synced Namespaces</Typography>
        </Box>
        
        <Typography level="body-sm" sx={{ mb: 2, color: 'text.secondary' }}>
          Item: <strong>{itemName}</strong> • {namespaces.length} namespace{namespaces.length !== 1 ? 's' : ''}
        </Typography>

        {namespaces.length === 0 ? (
          <Box sx={{ textAlign: 'center', py: 4, color: 'text.tertiary' }}>
            <Typography>No namespaces found</Typography>
          </Box>
        ) : (
          <Sheet sx={{ overflow: 'auto', maxHeight: '60vh' }}>
            <Table>
              <thead>
                <tr>
                  <th>Namespace</th>
                  <th>Secret Name</th>
                  <th>Status</th>
                  <th>Data Keys</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {namespaces.map((ns, idx) => (
                  <tr key={idx}>
                    <td>
                      <Typography fontWeight="medium">{ns.namespace}</Typography>
                    </td>
                    <td>
                      <Typography level="body-sm">{ns.secretName}</Typography>
                    </td>
                    <td>
                      <Chip
                        size="sm"
                        color={ns.status === 'Active' ? 'success' : 'danger'}
                        variant="soft"
                      >
                        {ns.status}
                      </Chip>
                    </td>
                    <td>
                      <Typography level="body-sm">{ns.dataKeysCount ?? '-'}</Typography>
                    </td>
                    <td>
                      <Chip 
                        size="sm" 
                        variant="outlined"
                        sx={{ 
                          cursor: 'pointer',
                          '&:hover': {
                            textDecoration: 'underline'
                          }
                        }}
                        onClick={() => onViewKeys && onViewKeys(ns.namespace, ns.secretName, itemName)}
                      >
                        🔑 View Keys
                      </Chip>
                    </td>
                  </tr>
                ))}
              </tbody>
            </Table>
          </Sheet>
        )}
      </ModalDialog>
    </Modal>
  )
}
