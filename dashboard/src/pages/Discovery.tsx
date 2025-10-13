import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import {
  Box,
  Typography,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Alert,
  Table,
  Sheet,
  Tabs,
  TabList,
  Tab,
  TabPanel,
  Input,
} from '@mui/joy'

interface VaultwardenItem {
  id: string
  name: string
  folder: string | null
  organizationId: string | null
  fields: number
  notes: string | null
}

interface DiscoveryData {
  vaultwardenItems: VaultwardenItem[]
  syncedSecrets: Array<{
    vaultwardenItemId: string
    vaultwardenItemName: string
    namespace: string
    secretName: string
    status: string
  }>
  lastScanTime: string
}

export default function Discovery() {
  const [searchTerm, setSearchTerm] = useState('')
  const [activeTab, setActiveTab] = useState(0)

  // Fetch discovery data from API
  const { data, isLoading } = useQuery({
    queryKey: ['discovery'],
    queryFn: async (): Promise<DiscoveryData> => {
      const response = await fetch('http://localhost:8080/api/discovery')
      if (!response.ok) {
        throw new Error('Failed to fetch discovery data')
      }
      return response.json()
    },
    refetchInterval: 60000,
  })

  // Filter out deleted secrets
  const activeSecrets = data?.syncedSecrets.filter(s => s.status !== 'Deleted') || []
  
  const syncedItemIds = new Set(activeSecrets.map(s => s.vaultwardenItemId))
  const notSyncedItems = data?.vaultwardenItems.filter(item => !syncedItemIds.has(item.id)) || []
  const syncedItems = data?.vaultwardenItems.filter(item => syncedItemIds.has(item.id)) || []
  
  // Use active secrets only (exclude deleted)
  const syncedSecrets = activeSecrets
  const filteredNotSynced = notSyncedItems.filter(
    item => item.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
           (item.folder?.toLowerCase().includes(searchTerm.toLowerCase()) || false)
  )

  const filteredSynced = syncedSecrets.filter(
    secret => secret.vaultwardenItemName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      secret.namespace.toLowerCase().includes(searchTerm.toLowerCase()) ||
      secret.secretName.toLowerCase().includes(searchTerm.toLowerCase())
  )

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  return (
    <Box sx={{ p: 3 }}>
      <Box sx={{ mb: 3 }}>
        <Typography level="h2" sx={{ mb: 1 }}>
          🔍 Vaultwarden Discovery
        </Typography>
        <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
          Compare Vaultwarden items with synced Kubernetes secrets
        </Typography>
      </Box>

      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Box>
          {data?.lastScanTime && (
            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>
              Last scan: {new Date(data.lastScanTime).toLocaleString()}
            </Typography>
          )}
        </Box>
        <Input
          placeholder="🔍 Search items..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          sx={{ width: 300 }}
        />
      </Box>

      {/* Info Alert */}
      {(!data?.vaultwardenItems || data.vaultwardenItems.length === 0) && (
        <Alert color="primary" variant="soft" sx={{ mb: 3 }}>
          <Typography level="body-sm">
            <strong>Note:</strong> Vaultwarden items data is not yet available. Currently showing synced secrets from the database.
            To enable full discovery (including non-synced VW items), implement Vaultwarden API integration in the backend.
          </Typography>
        </Alert>
      )}

      {/* Summary Cards */}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 2, mb: 3 }}>
        <Card variant="outlined" sx={{ backgroundColor: 'success.50' }}>
          <CardContent>
            <Typography level="body-sm" sx={{ color: 'text.secondary' }}>Synced to K8s</Typography>
            <Typography level="h2" sx={{ color: 'success.700' }}>{syncedSecrets.length}</Typography>
            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>Active secrets</Typography>
          </CardContent>
        </Card>
        
        <Card variant="outlined" sx={{ backgroundColor: 'warning.50' }}>
          <CardContent>
            <Typography level="body-sm" sx={{ color: 'text.secondary' }}>Not Synced</Typography>
            <Typography level="h2" sx={{ color: 'warning.700' }}>{notSyncedItems.length}</Typography>
            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>Available in VW</Typography>
          </CardContent>
        </Card>
        
        <Card variant="outlined" sx={{ backgroundColor: 'primary.50' }}>
          <CardContent>
            <Typography level="body-sm" sx={{ color: 'text.secondary' }}>Total Items</Typography>
            <Typography level="h2" sx={{ color: 'primary.700' }}>{data?.vaultwardenItems.length || 0}</Typography>
            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>In Vaultwarden</Typography>
          </CardContent>
        </Card>
        
        <Card variant="outlined" sx={{ backgroundColor: 'neutral.50' }}>
          <CardContent>
            <Typography level="body-sm" sx={{ color: 'text.secondary' }}>Sync Rate</Typography>
            <Typography level="h2" sx={{ color: 'neutral.700' }}>
              {data?.vaultwardenItems.length ? Math.round((syncedSecrets.length / data.vaultwardenItems.length) * 100) : 0}%
            </Typography>
            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>Coverage</Typography>
          </CardContent>
        </Card>
      </Box>

      {/* Tabs */}
      <Card variant="outlined">
        <Tabs value={activeTab} onChange={(_, value) => setActiveTab(value as number)}>
          <TabList>
            <Tab>
              Not Synced ({notSyncedItems.length})
            </Tab>
            <Tab>
              Synced ({syncedSecrets.length})
            </Tab>
            <Tab>
              Statistics
            </Tab>
          </TabList>
          
          {/* Not Synced Tab */}
          <TabPanel value={0}>
            <Sheet sx={{ overflow: 'auto' }}>
              <Table stripe="odd" hoverRow>
                <thead>
                  <tr>
                    <th>Item Name</th>
                    <th>Folder</th>
                    <th>Organization</th>
                    <th>Fields</th>
                    <th>Reason Not Synced</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredNotSynced.length > 0 ? (
                    filteredNotSynced.map((item) => (
                      <tr key={item.id}>
                        <td>
                          <Typography level="body-sm" fontWeight="medium">
                            🔐 {item.name}
                          </Typography>
                        </td>
                        <td>
                          {item.folder ? (
                            <Chip variant="soft" size="sm" color="neutral">
                              📁 {item.folder}
                            </Chip>
                          ) : (
                            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>
                              -
                            </Typography>
                          )}
                        </td>
                        <td>
                          {item.organizationId ? (
                            <Chip variant="soft" size="sm" color="primary">
                              Org
                            </Chip>
                          ) : (
                            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>
                              Personal
                            </Typography>
                          )}
                        </td>
                        <td>
                          <Typography level="body-sm">{item.fields}</Typography>
                        </td>
                        <td>
                          <Chip variant="soft" size="sm" color="warning">
                            Missing custom field
                          </Chip>
                        </td>
                      </tr>
                    ))
                  ) : (
                    <tr>
                      <td colSpan={5} style={{ textAlign: 'center', padding: '2rem' }}>
                        <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                          {searchTerm ? 'No items found matching your search' : 'All items are synced! 🎉'}
                        </Typography>
                      </td>
                    </tr>
                  )}
                </tbody>
              </Table>
            </Sheet>
          </TabPanel>

          {/* Synced Tab */}
          <TabPanel value={1}>
            <Sheet sx={{ overflow: 'auto' }}>
              <Table stripe="odd" hoverRow>
                <thead>
                  <tr>
                    <th>Item Name</th>
                    <th>Folder</th>
                    <th>Synced To</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredSynced.length > 0 ? (
                    filteredSynced.map((secret) => (
                      <tr key={`${secret.namespace}-${secret.secretName}`}>
                        <td>
                          <Typography level="body-sm" fontWeight="medium">
                            🔐 {secret.vaultwardenItemName}
                          </Typography>
                          <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>
                            ID: {secret.vaultwardenItemId.substring(0, 8)}...
                          </Typography>
                        </td>
                        <td>
                          <Chip variant="soft" size="sm" color="neutral">
                            📁 {secret.namespace}
                          </Chip>
                        </td>
                        <td>
                          <Typography level="body-sm">
                            {secret.namespace}/{secret.secretName}
                          </Typography>
                        </td>
                        <td>
                          <Chip 
                            variant="soft" 
                            size="sm" 
                            color={secret.status === 'Active' ? 'success' : 'danger'}
                          >
                            {secret.status}
                          </Chip>
                        </td>
                      </tr>
                    ))
                  ) : (
                    <tr>
                      <td colSpan={4} style={{ textAlign: 'center', padding: '2rem' }}>
                        <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                          {searchTerm ? 'No items found matching your search' : 'No synced items found'}
                        </Typography>
                      </td>
                    </tr>
                  )}
                </tbody>
              </Table>
            </Sheet>
          </TabPanel>

          {/* Statistics Tab */}
          <TabPanel value={2}>
            <Box sx={{ p: 3 }}>
              <Typography level="h4" sx={{ mb: 3 }}>📊 Sync Statistics</Typography>
              
              <Box sx={{ display: 'grid', gap: 3 }}>
                <Card variant="outlined">
                  <CardContent>
                    <Typography level="title-md" sx={{ mb: 1 }}>Coverage Analysis</Typography>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 2 }}>
                      <Typography level="body-sm">Synced Items:</Typography>
                      <Typography level="body-sm" fontWeight="bold">{syncedItems.length}</Typography>
                    </Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 2 }}>
                      <Typography level="body-sm">Not Synced:</Typography>
                      <Typography level="body-sm" fontWeight="bold">{notSyncedItems.length}</Typography>
                    </Box>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                      <Typography level="body-sm">Total in VW:</Typography>
                      <Typography level="body-sm" fontWeight="bold">{data?.vaultwardenItems.length || 0}</Typography>
                    </Box>
                  </CardContent>
                </Card>

                <Card variant="outlined">
                  <CardContent>
                    <Typography level="title-md" sx={{ mb: 1 }}>Why Items Aren't Synced</Typography>
                    <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                      Items are synced based on a custom field called <Chip size="sm" variant="soft">namespaces</Chip> configured
                      via environment variable (VAULTWARDEN_CUSTOM_FIELD_NAME).
                    </Typography>
                    <Typography level="body-sm" sx={{ color: 'text.secondary', mt: 2 }}>
                      To sync an item, add a custom field to your Vaultwarden item:
                    </Typography>
                    <Box sx={{ 
                      p: 2, 
                      backgroundColor: 'neutral.100', 
                      borderRadius: 'sm', 
                      mt: 1,
                      fontFamily: 'monospace',
                      fontSize: '0.875rem'
                    }}>
                      Field Name: namespaces<br/>
                      Field Value: namespace/secret-name
                    </Box>
                    <Typography level="body-sm" sx={{ color: 'text.secondary', mt: 2 }}>
                      The custom field name can be configured via the <strong>VAULTWARDEN_CUSTOM_FIELD_NAME</strong> environment variable.
                    </Typography>
                  </CardContent>
                </Card>

                <Alert color="primary" variant="soft">
                  <Typography level="body-sm">
                    <strong>Tip:</strong> This discovery view helps you identify which Vaultwarden items are
                    available but not yet synced to Kubernetes. You can use this to audit your setup and
                    decide which items should be synced.
                  </Typography>
                </Alert>
              </Box>
            </Box>
          </TabPanel>
        </Tabs>
      </Card>
    </Box>
  )
}
