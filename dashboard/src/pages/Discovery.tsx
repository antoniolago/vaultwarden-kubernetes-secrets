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
import KeysModal from '../components/KeysModal'
import NamespacesModal from '../components/NamespacesModal'
import { api } from '../lib/api'

interface VaultwardenItem {
  id: string
  name: string
  folder: string | null
  organizationId: string | null
  organizationName: string | null
  owner: string | null
  fields: number
  notes: string | null
  hasNamespacesField: boolean
  namespacesValue: string | null
}

interface DiscoveryData {
  vaultwardenItems: VaultwardenItem[]
  syncedSecrets: Array<{
    vaultwardenItemId: string
    vaultwardenItemName: string
    namespace: string
    secretName: string
    status: string
    dataKeysCount: number
    lastError: string | null
  }>
  lastScanTime: string
}

export default function Discovery() {
  const [searchTerm, setSearchTerm] = useState('')
  const [activeTab, setActiveTab] = useState(0)
  const [dataKeysModalOpen, setDataKeysModalOpen] = useState(false)
  const [selectedDataKeys, setSelectedDataKeys] = useState<Array<{label: string, keys: string[]}>>([])  
  const [loadingDataKeys, setLoadingDataKeys] = useState(false)
  const [dataKeysModalTitle, setDataKeysModalTitle] = useState('')
  const [dataKeysModalSubtitle, setDataKeysModalSubtitle] = useState('')
  const [fieldsModalOpen, setFieldsModalOpen] = useState(false)
  const [selectedFields, setSelectedFields] = useState<Array<{label: string, keys: string[]}>>([])  
  const [loadingFields, setLoadingFields] = useState(false)
  const [fieldsModalTitle, setFieldsModalTitle] = useState('')
  const [namespacesModalOpen, setNamespacesModalOpen] = useState(false)
  const [selectedNamespaces, setSelectedNamespaces] = useState<Array<{namespace: string, secretName: string, status: string}>>([])
  const [namespacesModalItemName, setNamespacesModalItemName] = useState('')

  // Fetch sync status to get interval and timing
  const { data: syncStatus } = useQuery({
    queryKey: ['sync-status'],
    queryFn: api.getSyncStatus,
    refetchInterval: 10000, // Check sync status every 10s
  })


  // Calculate refetch interval: use half of sync interval, or 15 seconds minimum
  const discoveryRefetchInterval = syncStatus 
    ? Math.max(Math.floor(syncStatus.syncIntervalSeconds * 1000 / 2), 15000)
    : 30000 // Default to 30s if not yet loaded

  // Fetch discovery data from API
  const { data, isLoading, error } = useQuery({
    queryKey: ['discovery'],
    queryFn: api.getDiscoveryData,
    refetchInterval: discoveryRefetchInterval,
    retry: 2,
  })

  // Filter out deleted secrets
  const activeSecrets = data?.syncedSecrets.filter((s: { status: string }) => s.status !== 'Deleted') || []
  
  // Separate successfully synced from failed
  const successfullySyncedItemIds = new Set(activeSecrets.filter((s: { status: string }) => s.status !== 'Failed').map((s: { vaultwardenItemId: string }) => s.vaultwardenItemId))
  const failedSecrets = activeSecrets.filter((s: { status: string }) => s.status === 'Failed')
  const failedItemIds = new Set(failedSecrets.map((s: { vaultwardenItemId: string }) => s.vaultwardenItemId))
  
  // Not synced includes: items without synced secrets OR items with only failed secrets
  const notSyncedItems = data?.vaultwardenItems.filter((item: VaultwardenItem) => 
    !successfullySyncedItemIds.has(item.id)
  ) || []
  // @ts-ignore - Used in Coverage Analysis section (line 534), but TS can't detect usage in JSX
  const syncedItems = data?.vaultwardenItems.filter((item: VaultwardenItem) => successfullySyncedItemIds.has(item.id)) || []
  
  // Debug: Log secrets info
  console.log('All Vaultwarden items:', data?.vaultwardenItems)
  console.log('All active secrets:', activeSecrets)
  console.log('All synced secrets statuses:', activeSecrets.map((s: any) => ({ id: s.vaultwardenItemId, name: s.vaultwardenItemName, status: s.status, error: s.lastError })))
  console.log('Items with namespaces field:', data?.vaultwardenItems?.filter((i: VaultwardenItem) => i.hasNamespacesField))
  console.log('Not synced items:', notSyncedItems.map((i: VaultwardenItem) => ({ id: i.id, name: i.name, hasNsField: i.hasNamespacesField })))
  if (failedSecrets.length > 0) {
    console.log('Failed secrets:', failedSecrets)
    console.log('Failed item IDs:', Array.from(failedItemIds))
  } else {
    console.log('No failed secrets found in database')
  }
  
  // Use active secrets only (exclude deleted and failed)
  const syncedSecrets = activeSecrets.filter((s: { status: string }) => s.status !== 'Failed')
  
  // Deduplicate synced secrets by grouping namespaces
  const groupedSyncedSecrets = syncedSecrets.reduce((acc: Record<string, { vaultwardenItemId: string, vaultwardenItemName: string, secretName: string, totalDataKeys: number, namespaces: Array<{namespace: string, secretName: string, status: string, dataKeysCount: number}> }>, secret: any) => {
    const key = secret.vaultwardenItemId
    if (!acc[key]) {
      acc[key] = {
        vaultwardenItemId: secret.vaultwardenItemId,
        vaultwardenItemName: secret.vaultwardenItemName,
        secretName: secret.secretName,
        totalDataKeys: secret.dataKeysCount,
        namespaces: []
      }
    } else {
      acc[key].totalDataKeys += secret.dataKeysCount
    }
    acc[key].namespaces.push({
      namespace: secret.namespace,
      secretName: secret.secretName,
      status: secret.status,
      dataKeysCount: secret.dataKeysCount
    })
    return acc
  }, {} as Record<string, { vaultwardenItemId: string, vaultwardenItemName: string, secretName: string, totalDataKeys: number, namespaces: Array<{namespace: string, secretName: string, status: string, dataKeysCount: number}> }>)
  
  const dedupedSyncedSecrets = Object.values(groupedSyncedSecrets)
  
  const filteredNotSynced = notSyncedItems.filter(
    (item: VaultwardenItem) => item.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
           (item.folder?.toLowerCase().includes(searchTerm.toLowerCase()) || false)
  )

  const filteredSynced = dedupedSyncedSecrets.filter(
    (item: any) => item.vaultwardenItemName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      item.namespaces.some((ns: any) => 
        ns.namespace.toLowerCase().includes(searchTerm.toLowerCase()) ||
        ns.secretName.toLowerCase().includes(searchTerm.toLowerCase())
      )
  )

  const handleShowDataKeys = async (namespace: string, secretName: string, itemName: string) => {
    setLoadingDataKeys(true)
    setDataKeysModalTitle(itemName)
    setDataKeysModalSubtitle(`${namespace}/${secretName}`)
    setDataKeysModalOpen(true)
    
    try {
      const response = await fetch(`http://localhost:8080/api/secrets/${namespace}/${secretName}/keys`)
      if (response.ok) {
        const keys = await response.json()
        setSelectedDataKeys([{ label: secretName, keys }])
      } else {
        setSelectedDataKeys([{ label: secretName, keys: ['Unable to fetch key names'] }])
      }
    } catch (error) {
      console.error(`Error fetching keys:`, error)
      setSelectedDataKeys([{ label: secretName, keys: ['Error fetching keys'] }])
    } finally {
      setLoadingDataKeys(false)
    }
  }

  const handleShowFields = async (itemId: string, itemName: string) => {
    setLoadingFields(true)
    setFieldsModalTitle(itemName)
    setFieldsModalOpen(true)
    
    try {
      const response = await fetch(`http://localhost:8080/api/vaultwarden/items/${itemId}/fields`)
      if (response.ok) {
        const fields = await response.json()
        setSelectedFields([{ label: 'Custom Fields', keys: fields }])
      } else {
        setSelectedFields([{ label: 'Custom Fields', keys: ['Unable to fetch field names'] }])
      }
    } catch (error) {
      console.error(`Error fetching fields:`, error)
      setSelectedFields([{ label: 'Custom Fields', keys: ['Error fetching fields'] }])
    } finally {
      setLoadingFields(false)
    }
  }

  const handleShowNamespaces = (itemName: string, namespaces: Array<{namespace: string, secretName: string, status: string, dataKeysCount?: number}>) => {
    setNamespacesModalItemName(itemName)
    setSelectedNamespaces(namespaces)
    setNamespacesModalOpen(true)
  }

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

      {/* Error Alert */}
      {error && (
        <Alert color="danger" variant="soft" sx={{ mb: 3 }}>
          <Typography level="body-sm">
            <strong>❌ Error:</strong> {(error as Error).message}
          </Typography>
          <Typography level="body-xs" sx={{ mt: 1 }}>
            Please check that the API is running and accessible. The page will retry automatically.
          </Typography>
        </Alert>
      )}

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

      {/* Info Alert - Only show if there's an error AND no data loaded */}
      {error && (!data?.vaultwardenItems || data.vaultwardenItems.length === 0) && (
        <Alert color="warning" variant="soft" sx={{ mb: 3 }}>
          <Typography level="body-sm">
            <strong>⚠️ Authentication Issue:</strong> Vaultwarden items could not be loaded. 
            The API may not be authenticated or the Vaultwarden service is unavailable. 
            Check API logs for authentication errors.
          </Typography>
        </Alert>
      )}


      {/* Summary Cards */}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 2, mb: 3 }}>
        <Card variant="outlined" color="success" sx={{ bgcolor: 'success.softBg' }}>
          <CardContent>
            <Typography level="body-sm" color="neutral">Synced to K8s</Typography>
            <Typography level="h2" color="success">{dedupedSyncedSecrets.length}</Typography>
            <Typography level="body-xs" color="neutral">Unique items</Typography>
          </CardContent>
        </Card>
        
        <Card variant="outlined" color="warning" sx={{ bgcolor: 'warning.softBg' }}>
          <CardContent>
            <Typography level="body-sm" color="neutral">Not Synced</Typography>
            <Typography level="h2" color="warning">{notSyncedItems.length}</Typography>
            <Typography level="body-xs" color="neutral">Available in VW</Typography>
          </CardContent>
        </Card>
        
        <Card variant="outlined" color="primary" sx={{ bgcolor: 'primary.softBg' }}>
          <CardContent>
            <Typography level="body-sm" color="neutral">Total Items</Typography>
            <Typography level="h2" color="primary">{data?.vaultwardenItems.length || 0}</Typography>
            <Typography level="body-xs" color="neutral">In Vaultwarden</Typography>
          </CardContent>
        </Card>
        
        <Card variant="outlined" color="neutral" sx={{ bgcolor: 'neutral.softBg' }}>
          <CardContent>
            <Typography level="body-sm" color="neutral">Sync Rate</Typography>
            <Typography level="h2" color="neutral">
              {data?.vaultwardenItems.length ? Math.round((dedupedSyncedSecrets.length / data.vaultwardenItems.length) * 100) : 0}%
            </Typography>
            <Typography level="body-xs" color="neutral">Coverage</Typography>
          </CardContent>
        </Card>
      </Box>

      {/* Tabs */}
      <Card variant="outlined" sx={{ bgcolor: 'background.surface' }}>
        <Tabs value={activeTab} onChange={(_, value) => setActiveTab(value as number)}>
          <TabList>
            <Tab>
              Synced ({dedupedSyncedSecrets.length})
            </Tab>
            <Tab>
              Not Synced ({notSyncedItems.length})
            </Tab>
            {/* <Tab>
              Statistics
            </Tab> */}
          </TabList>
          
          {/* Synced Tab */}
          <TabPanel value={0}>
            <Sheet variant="soft" sx={{ overflow: 'auto' }}>
              <Table stripe="odd" hoverRow>
                <thead>
                  <tr>
                    <th>Item Name</th>
                    <th>Secret Name</th>
                    <th>Namespaces</th>
                    <th>Data Keys</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredSynced.length > 0 ? (
                    filteredSynced.map((item) => {
                      // const vaultwardenItem = syncedItems.find(vwItem => vwItem.id === item.vaultwardenItemId)
                      return (
                        <tr key={item.vaultwardenItemId}>
                          <td>
                            <Typography level="body-sm" fontWeight="medium">
                              {item.vaultwardenItemName}
                            </Typography>
                            <Typography level="body-xs" sx={{ color: 'text.tertiary' }}>
                              ID: {item.vaultwardenItemId.substring(0, 8)}...
                            </Typography>
                          </td>
                          <td>
                            <Typography level="body-sm">
                              {item.secretName}
                            </Typography>
                          </td>
                          <td>
                            <Chip 
                              size="sm" 
                              variant="soft"
                              color="primary"
                              sx={{ 
                                cursor: item.namespaces.length > 4 ? 'pointer' : 'default',
                                '&:hover': item.namespaces.length > 4 ? {
                                  bgcolor: 'primary.solidHoverBg'
                                } : {}
                              }}
                              onClick={() => item.namespaces.length > 4 && handleShowNamespaces(item.vaultwardenItemName, item.namespaces)}
                            >
                              {item.namespaces.slice(0, 4).map(ns => ns.namespace).join(', ')}{item.namespaces.length > 4 ? `, +${item.namespaces.length - 4} more` : ''}
                            </Chip>
                          </td>
                          <td>
                            <Typography level="body-sm">
                              {item.totalDataKeys}
                            </Typography>
                          </td>
                        </tr>
                      )
                    })
                  ) : (
                    <tr>
                      <td colSpan={5} style={{ textAlign: 'center', padding: '2rem' }}>
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

          {/* Not Synced Tab */}
          <TabPanel value={1}>
            <Sheet sx={{ overflow: 'auto' }}>
              <Table hoverRow>
                <thead>
                  <tr>
                    <th>Item Name</th>
                    <th>Folder</th>
                    <th>Owner</th>
                    <th>Fields</th>
                    <th>Reason Not Synced</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredNotSynced.length > 0 ? (
                    filteredNotSynced.map((item: VaultwardenItem) => (
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
                          <Chip 
                            variant="soft" 
                            size="sm" 
                            color={item.organizationId ? "primary" : "neutral"}
                          >
                            {item.owner || (item.organizationId ? 'Organization' : 'Personal')}
                          </Chip>
                        </td>
                        <td>
                          <Chip 
                            size="sm" 
                            variant="outlined"
                            sx={{ 
                              cursor: item.fields > 0 ? 'pointer' : 'default',
                              '&:hover': item.fields > 0 ? {
                                textDecoration: 'underline'
                              } : {}
                            }}
                            onClick={() => item.fields > 0 && handleShowFields(item.id, item.name)}
                          >
                            {item.fields} {item.fields === 1 ? 'field' : 'fields'}
                          </Chip>
                        </td>
                        <td>
                          {(() => {
                            // Missing namespaces field
                            if (!item.hasNamespacesField) {
                              return (
                                <Typography level="body-sm" sx={{ color: 'warning.500' }}>
                                  Missing 'namespaces' custom field
                                </Typography>
                              )
                            }
                            
                            // Failed sync with error
                            if (failedItemIds.has(item.id)) {
                              const failedSecret = failedSecrets.find((s: any) => s.vaultwardenItemId === item.id)
                              const error = failedSecret?.lastError
                              return (
                                <Typography level="body-sm" sx={{ color: 'danger.500', fontFamily: 'monospace' }}>
                                  {error || 'Sync failed - no error details'}
                                </Typography>
                              )
                            }
                            
                            // Has namespaces field but not synced - show actual namespaces value
                            let namespaces: string[] = []
                            try {
                              if (item.namespacesValue) {
                                namespaces = JSON.parse(item.namespacesValue)
                              }
                            } catch {
                              // Invalid JSON
                            }
                            
                            if (namespaces.length === 0) {
                              return (
                                <Typography level="body-sm" sx={{ color: 'warning.500' }}>
                                  'namespaces' field exists but is empty or invalid
                                </Typography>
                              )
                            }
                            
                            return (
                              <Typography level="body-sm" sx={{ color: 'neutral.500' }}>
                                Configured for: {namespaces.join(', ')} - Not yet synced (check sync logs for errors)
                              </Typography>
                            )
                          })()}
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

          {/* Statistics Tab */}
          {/* <TabPanel value={2}>
            <Box sx={{ p: 3 }}>
              <Typography level="h4" sx={{ mb: 3 }}>📊 Sync Statistics</Typography>
              
              <Box sx={{ display: 'grid', gap: 3 }}>
                <Card variant="outlined" sx={{ bgcolor: 'background.surface' }}>
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

                <Card variant="outlined" sx={{ bgcolor: 'background.surface' }}>
                  <CardContent>
                    <Typography level="title-md" sx={{ mb: 1 }}>Why Items Aren't Synced</Typography>
                    <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                      Items are synced based on a custom field called <Chip size="sm" variant="soft">namespaces</Chip> configured
                      via environment variable (VAULTWARDEN_CUSTOM_FIELD_NAME).
                    </Typography>
                    <Typography level="body-sm" sx={{ color: 'text.secondary', mt: 2 }}>
                      To sync an item, add a custom field to your Vaultwarden item with the namespace name:
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
                      Field Value: namespace-name (e.g., "default" or "prod")
                    </Box>
                    <Typography level="body-sm" sx={{ color: 'text.secondary', mt: 2 }}>
                      You can also specify multiple namespaces separated by commas. The secret name will be derived from the item name.
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
          </TabPanel> */}
        </Tabs>
      </Card>

      {/* Data Keys Modal */}
      <KeysModal
        open={dataKeysModalOpen}
        onClose={() => setDataKeysModalOpen(false)}
        title={dataKeysModalTitle}
        subtitle={dataKeysModalSubtitle}
        loading={loadingDataKeys}
        items={selectedDataKeys}
        emptyMessage="No data keys found"
      />

      {/* Fields Modal */}
      <KeysModal
        open={fieldsModalOpen}
        onClose={() => setFieldsModalOpen(false)}
        title={fieldsModalTitle}
        subtitle="Custom fields from Vaultwarden"
        loading={loadingFields}
        items={selectedFields}
        emptyMessage="No custom fields found"
      />

      {/* Namespaces Modal */}
      <NamespacesModal
        open={namespacesModalOpen}
        onClose={() => setNamespacesModalOpen(false)}
        itemName={namespacesModalItemName}
        namespaces={selectedNamespaces}
        onViewKeys={handleShowDataKeys}
      />
    </Box>
  )
}
