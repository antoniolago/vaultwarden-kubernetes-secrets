import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import {
  Box,
  Typography,
  Card,
  CardContent,
  Grid,
  Chip,
  CircularProgress,
  Alert,
  Table,
  Sheet,
} from '@mui/joy'
import { api, SecretState } from '../lib/api'
import { formatRelative, formatDuration } from '../lib/utils'
import SecretsModal from '../components/SecretsModal'
import KeysModal from '../components/KeysModal'

function StatCard({ title, value, emoji, subtitle, helpText, testId, color = 'primary' }: any) {
  const colorMap: Record<string, any> = {
    success: {
      bg: 'success.50',
      iconBg: 'success.100',
      icon: 'success.600',
      value: 'success.700',
      border: 'success.200'
    },
    primary: {
      bg: 'primary.50',
      iconBg: 'primary.100',
      icon: 'primary.600',
      value: 'primary.700',
      border: 'primary.200'
    },
    neutral: {
      bg: 'neutral.50',
      iconBg: 'neutral.100',
      icon: 'neutral.600',
      value: 'neutral.700',
      border: 'neutral.200'
    },
    warning: {
      bg: 'warning.50',
      iconBg: 'warning.100',
      icon: 'warning.600',
      value: 'warning.700',
      border: 'warning.200'
    }
  }

  const colors = colorMap[color] || colorMap.primary

  return (
    <Card 
      variant="outlined" 
      data-testid={testId}
      sx={{
        backgroundColor: colors.bg,
        borderColor: colors.border,
        transition: 'all 0.2s',
        '&:hover': {
          boxShadow: 'md',
          transform: 'translateY(-2px)'
        }
      }}
    >
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
          <Box sx={{ flex: 1 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mb: 1 }}>
              <Typography level="body-sm" sx={{ color: 'text.secondary', fontWeight: 600 }}>
                {title}
              </Typography>
              {helpText && (
                <Typography 
                  level="body-xs" 
                  sx={{ color: 'text.tertiary', cursor: 'help' }}
                  title={helpText}
                >
                  ⓘ
                </Typography>
              )}
            </Box>
            <Typography 
              level="h1" 
              sx={{ 
                color: colors.value,
                fontWeight: 700,
                fontSize: '2.5rem',
                lineHeight: 1,
                mb: 0.5
              }} 
              data-testid={`${testId}-value`}
            >
              {value}
            </Typography>
            <Typography 
              level="body-xs" 
              sx={{ 
                color: 'text.tertiary', 
                minHeight: '1.2em',
                display: 'block'
              }} 
              data-testid={`${testId}-subtitle`}
            >
              {subtitle || ' '}
            </Typography>
          </Box>
          <Box 
            sx={{ 
              backgroundColor: colors.iconBg,
              color: colors.icon,
              borderRadius: 'lg',
              width: 64,
              height: 64,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '2rem',
              flexShrink: 0
            }}
          >
            {emoji}
          </Box>
        </Box>
      </CardContent>
    </Card>
  )
}

export default function Dashboard() {
  const [modalOpen, setModalOpen] = useState(false)
  const [modalSecrets, setModalSecrets] = useState<SecretState[]>([])
  const [modalTitle, setModalTitle] = useState('')
  const [modalNamespace, setModalNamespace] = useState('')
  const [loadingSecrets, setLoadingSecrets] = useState(false)
  const [dataKeysModalOpen, setDataKeysModalOpen] = useState(false)
  const [selectedDataKeys, setSelectedDataKeys] = useState<Array<{label: string, keys: string[]}>>([])
  const [loadingDataKeys, setLoadingDataKeys] = useState(false)

  const { data: overview, isLoading, error } = useQuery({
    queryKey: ['dashboard-overview'],
    queryFn: api.getDashboardOverview,
    refetchInterval: 30000,
  })

  const { data: allNamespaces } = useQuery({
    queryKey: ['namespaces'],
    queryFn: api.getNamespaces,
    refetchInterval: 60000,
  })

  // Filter out namespaces that only have deleted secrets (no active or failed)
  const namespaces = allNamespaces?.filter(ns => ns.activeSecrets > 0 || ns.failedSecrets > 0) || []

  const handleShowSecrets = async (namespace: string, status: 'Active' | 'Failed', count?: number) => {
    if (count === 0) return
    
    setLoadingSecrets(true)
    setModalNamespace(namespace)
    setModalTitle(`Secrets With Errors in ${namespace}`)
    setModalOpen(true)
    
    try {
      const secrets = await api.getSecretsByNamespace(namespace)
      const filteredSecrets = secrets.filter(s => s.status === status)
      setModalSecrets(filteredSecrets)
    } catch (err) {
      console.error('Failed to load secrets:', err)
      setModalSecrets([])
    } finally {
      setLoadingSecrets(false)
    }
  }

  const handleShowAllSecrets = async (namespace: string) => {
    setLoadingSecrets(true)
    setModalNamespace(namespace)
    setModalTitle(`All Secrets in ${namespace}`)
    setModalOpen(true)
    
    try {
      const secrets = await api.getSecretsByNamespace(namespace)
      setModalSecrets(secrets)
    } catch (err) {
      console.error('Failed to load secrets:', err)
      setModalSecrets([])
    } finally {
      setLoadingSecrets(false)
    }
  }

  const handleShowDataKeys = async (namespace: string) => {
    setLoadingDataKeys(true)
    setModalNamespace(namespace)
    setDataKeysModalOpen(true)
    
    try {
      const secrets = await api.getSecretsByNamespace(namespace)
      const keysPromises = secrets.map(async (secret) => {
        try {
          const response = await fetch(`http://localhost:8080/api/secrets/${secret.namespace}/${secret.secretName}/keys`)
          if (response.ok) {
            const keys = await response.json()
            return { secretName: secret.secretName, keys }
          }
          // API call failed, but secret exists
          console.warn(`Could not fetch keys for ${secret.secretName}`)
          return { secretName: secret.secretName, keys: ['Unable to fetch key names'] }
        } catch (error) {
          console.error(`Error fetching keys for ${secret.secretName}:`, error)
          return { secretName: secret.secretName, keys: ['Error fetching keys'] }
        }
      })
      const allKeys = await Promise.all(keysPromises)
      // Show all secrets, even if we couldn't fetch their keys
      // Convert to KeysModal format
      const formattedKeys = allKeys.map(item => ({
        label: item.secretName,
        keys: item.keys
      }))
      setSelectedDataKeys(formattedKeys)
    } catch (err) {
      console.error('Failed to load secrets:', err)
      setSelectedDataKeys([])
    } finally {
      setLoadingDataKeys(false)
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
      <Box sx={{ p: 3 }}>
        <Alert color="danger" variant="soft">
          <Typography level="body-sm">
            <strong>❌ Error:</strong> Failed to load dashboard: {(error as Error).message}
          </Typography>
          <Typography level="body-xs" sx={{ mt: 1 }}>
            Please check that the API is running at http://localhost:8080 and is accessible. The page will retry automatically.
          </Typography>
        </Alert>
      </Box>
    )
  }

  return (
    <Box>
      <Typography level="h2" sx={{ mb: 3 }}>
        📊 Dashboard Overview
      </Typography>

      {/* Stats Grid */}
      <Grid container spacing={2} sx={{ mb: 4 }} data-testid="stats-grid">
        <Grid xs={12} sm={6} lg={3}>
          <StatCard
            title="Active Secrets"
            value={overview?.activeSecrets || 0}
            emoji="🔐"
            color="success"
            helpText="Number of Kubernetes secrets successfully synced from Vaultwarden"
            subtitle={`${namespaces?.filter(ns => ns.activeSecrets > 0).length || 0} namespaces have active secrets`}
            testId="stat-active-secrets"
          />
        </Grid>
        <Grid xs={12} sm={6} lg={3}>
          <StatCard
            title="Total Data Keys"
            value={namespaces?.reduce((sum, ns) => sum + ns.totalDataKeys, 0) || 0}
            emoji="🔑"
            color="primary"
            helpText="Total number of key-value pairs stored across all secrets"
            subtitle={`Across ${namespaces?.length || 0} namespaces`}
            testId="stat-total-keys"
          />
        </Grid>
        <Grid xs={12} sm={6} lg={3}>
          <StatCard
            title="Namespaces"
            value={namespaces?.length || 0}
            emoji="📂"
            color="neutral"
            helpText="Number of Kubernetes namespaces with active or failed secrets"
            subtitle={`Managing ${namespaces?.reduce((sum, ns) => sum + ns.secretCount, 0) || 0} total secrets`}
            testId="stat-namespaces"
          />
        </Grid>
        <Grid xs={12} sm={6} lg={3}>
          <StatCard
            title="Sync Avg Duration"
            value={formatDuration(overview?.averageSyncDuration || 0)}
            emoji="⚡"
            color="warning"
            helpText="Average time to complete a sync operation and overall success rate"
            // subtitle={`${(overview?.successRate ?? 0).toFixed(1)}% success • ${overview?.totalSyncs || 0} total syncs`}
            testId="stat-sync-performance"
          />
        </Grid>
      </Grid>

      {/* Namespaces Table */}
      <Card variant="outlined" sx={{ mb: 3 }} data-testid="namespaces-table-card">
        <CardContent>
          <Typography level="title-lg" sx={{ mb: 2 }}>
            📁 Namespaces
          </Typography>
          {namespaces && namespaces.length > 0 ? (
            <Sheet sx={{ overflow: 'auto' }}>
              <Table stripe="odd" hoverRow data-testid="namespaces-table">
                <thead>
                  <tr>
                    <th>Namespace</th>
                    <th>Total</th>
                    <th>Active</th>
                    <th>With Errors</th>
                    <th>Data Keys</th>
                    <th>Success Rate</th>
                    <th>Last Sync</th>
                  </tr>
                </thead>
                <tbody>
                  {namespaces.map((ns) => (
                    <tr key={ns.namespace} data-testid={`namespace-row-${ns.namespace}`}>
                      <td data-testid="namespace-name">
                        <Typography fontWeight="medium">{ns.namespace}</Typography>
                      </td>
                      <td data-testid="namespace-total-secrets">
                        <Chip 
                          variant="soft" 
                          color="neutral" 
                          size="sm"
                          sx={{ cursor: ns.secretCount > 0 ? 'pointer' : 'default' }}
                          onClick={() => ns.secretCount > 0 && handleShowAllSecrets(ns.namespace)}
                          data-testid="chip-total-secrets"
                        >
                          {ns.secretCount}
                        </Chip>
                      </td>
                      <td data-testid="namespace-active-secrets">
                        <Chip 
                          size="sm" 
                          color="success" 
                          variant="soft"
                          sx={{ cursor: ns.activeSecrets > 0 ? 'pointer' : 'default' }}
                          onClick={() => handleShowSecrets(ns.namespace, 'Active', ns.activeSecrets)}
                          data-testid="chip-active-secrets"
                        >
                          {ns.activeSecrets}
                        </Chip>
                      </td>
                      <td data-testid="namespace-failed-secrets">
                        {ns.failedSecrets > 0 ? (
                          <Chip 
                            size="sm" 
                            color="warning" 
                            variant="soft"
                            sx={{ cursor: 'pointer' }}
                            onClick={() => handleShowSecrets(ns.namespace, 'Failed', ns.failedSecrets)}
                            data-testid="chip-failed-secrets"
                          >
                            {ns.failedSecrets}
                          </Chip>
                        ) : (
                          <Typography level="body-sm" sx={{ color: 'text.tertiary' }} data-testid="chip-failed-secrets">
                            0
                          </Typography>
                        )}
                      </td>
                      <td data-testid="namespace-data-keys">
                        <Chip 
                          size="sm" 
                          variant="outlined"
                          sx={{ 
                            cursor: ns.totalDataKeys > 0 ? 'pointer' : 'default',
                            color: ns.totalDataKeys > 0 ? 'primary.600' : 'text.primary',
                            '&:hover': ns.totalDataKeys > 0 ? {
                              textDecoration: 'underline'
                            } : {}
                          }}
                          onClick={() => ns.totalDataKeys > 0 && handleShowDataKeys(ns.namespace)}
                          data-testid="chip-data-keys"
                        >
                          {ns.totalDataKeys}
                        </Chip>
                      </td>
                      <td>
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                          <Box
                            sx={{
                              width: 60,
                              height: 6,
                              bgcolor: 'neutral.200',
                              borderRadius: 'sm',
                              overflow: 'hidden',
                            }}
                          >
                            <Box
                              sx={{
                                height: '100%',
                                width: `${ns.successRate}%`,
                                bgcolor:
                                  ns.successRate > 90
                                    ? 'success.500'
                                    : ns.successRate > 70
                                    ? 'warning.500'
                                    : 'danger.500',
                              }}
                            />
                          </Box>
                          <Typography level="body-sm">{(ns.successRate ?? 0).toFixed(0)}%</Typography>
                        </Box>
                      </td>
                      <td>
                        <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                          {ns.lastSyncTime ? formatRelative(ns.lastSyncTime) : 'Never'}
                        </Typography>
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr style={{ borderTop: '2px solid var(--joy-palette-divider)', fontWeight: 'bold' }}>
                    <td>
                      <Typography fontWeight="bold">TOTAL</Typography>
                    </td>
                    <td>
                      <Typography fontWeight="bold" data-testid="total-secrets">
                        {namespaces.reduce((sum, ns) => sum + ns.secretCount, 0)}
                      </Typography>
                    </td>
                    <td>
                      <Typography fontWeight="bold" color="success" data-testid="total-active">
                        {namespaces.reduce((sum, ns) => sum + ns.activeSecrets, 0)}
                      </Typography>
                    </td>
                    <td>
                      <Typography fontWeight="bold" color="warning" data-testid="total-failed">
                        {namespaces.reduce((sum, ns) => sum + ns.failedSecrets, 0)}
                      </Typography>
                    </td>
                    <td>
                      <Typography fontWeight="bold" data-testid="total-data-keys">
                        {namespaces.reduce((sum, ns) => sum + ns.totalDataKeys, 0)}
                      </Typography>
                    </td>
                    <td colSpan={2}>
                      <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                        {namespaces.length} namespace{namespaces.length !== 1 ? 's' : ''}
                      </Typography>
                    </td>
                  </tr>
                </tfoot>
              </Table>
            </Sheet>
          ) : (
            <Alert color="neutral">
              No namespaces found. Run a sync to populate data.
            </Alert>
          )}
        </CardContent>
      </Card>

      {/* Recent Sync Status */}
      {overview?.lastSyncTime && (
        <Alert 
          color={overview.successRate > 80 ? 'success' : overview.successRate > 50 ? 'warning' : 'danger'}
          sx={{ mb: 3 }}
          data-testid="sync-status-alert"
        >
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2 }}>
            <Box sx={{ flex: 1 }}>
              <Typography level="title-md">
                {overview.successRate === 100 ? '✅ All systems operational' : 
                 overview.successRate > 80 ? '⚠️ Mostly operational' : 
                 '❌ Issues detected'}
              </Typography>
              <Typography level="body-sm">
                Last sync: {formatRelative(overview.lastSyncTime)}
                {namespaces && namespaces.reduce((sum, ns) => sum + ns.failedSecrets, 0) > 0 && (
                  <> • <strong>{namespaces.reduce((sum, ns) => sum + ns.failedSecrets, 0)} secrets with errors</strong> (need attention)</>
                )}
              </Typography>
              <Typography level="body-xs" sx={{ mt: 0.5, color: 'text.tertiary' }}>
                Sync operations: {overview.successfulSyncs} successful, {overview.failedSyncs} failed
              </Typography>
            </Box>
          </Box>
        </Alert>
      )}

      {/* Secrets Modal */}
      <SecretsModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        secrets={loadingSecrets ? [] : modalSecrets}
        title={loadingSecrets ? 'Loading...' : modalTitle}
        namespace={modalNamespace}
      />

      {/* Data Keys Modal */}
      <KeysModal
        open={dataKeysModalOpen}
        onClose={() => setDataKeysModalOpen(false)}
        title={`Data Keys in ${modalNamespace}`}
        subtitle={`${selectedDataKeys.length} ${selectedDataKeys.length === 1 ? 'secret' : 'secrets'}`}
        loading={loadingDataKeys}
        items={selectedDataKeys}
        emptyMessage="No data keys found in this namespace"
      />
    </Box>
  )
}
