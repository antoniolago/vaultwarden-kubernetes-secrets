import { useQuery } from '@tanstack/react-query'
import {
  Box,
  Typography,
  Card,
  CardContent,
  Grid,
  CircularProgress,
  Alert,
  LinearProgress,
  Chip,
} from '@mui/joy'
import {
  Cpu as CpuIcon,
  HardDrive as MemoryIcon,
  Activity as ThreadsIcon,
  Clock as UptimeIcon,
  Server as ServerIcon,
  AlertTriangle as WarningIcon,
  AlertTriangle,
} from 'lucide-react'
import { api } from '../lib/api'
import { formatDuration } from '../lib/utils'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from 'recharts'
import { useState, useEffect } from 'react'

function ResourceCard({ title, icon, children }: any) {
  return (
    <Card variant="outlined">
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
          {icon}
          <Typography level="title-md">{title}</Typography>
        </Box>
        {children}
      </CardContent>
    </Card>
  )
}

function MetricBar({ label, value, max, unit, color = 'primary' }: any) {
  const percentage = Math.min((value / max) * 100, 100)
  const warningThreshold = 70
  const dangerThreshold = 90

  const barColor =
    percentage > dangerThreshold ? 'danger' : percentage > warningThreshold ? 'warning' : color

  return (
    <Box sx={{ mb: 2 }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography level="body-sm">{label}</Typography>
        <Typography level="body-sm" fontWeight="bold">
          {value.toFixed(1)} {unit}
          {max && ` / ${max} ${unit}`}
        </Typography>
      </Box>
      <LinearProgress
        determinate
        value={percentage}
        color={barColor}
        sx={{ height: 8 }}
      />
      {percentage > warningThreshold && (
        <Typography level="body-xs" sx={{ color: `${barColor}.500`, mt: 0.5 }}>
          {percentage > dangerThreshold ? '⚠️ Critical' : '⚠️ Warning'} - High usage detected
        </Typography>
      )}
    </Box>
  )
}

export default function Resources() {
  const [history, setHistory] = useState<Array<{
    timestamp: string
    apiCpu: number
    apiMemory: number
    syncCpu?: number
    syncMemory?: number
  }>>([])

  const { data: apiResources, isLoading: apiLoading, error: apiError } = useQuery({
    queryKey: ['api-resources'],
    queryFn: api.getSystemResources,
    refetchInterval: 5000,
  })

  const { data: syncResources } = useQuery({
    queryKey: ['sync-resources'],
    queryFn: api.getSyncServiceResources,
    refetchInterval: 5000,
  })

  // Update history for chart
  useEffect(() => {
    if (apiResources) {
      setHistory((prev) => {
        const newPoint = {
          timestamp: new Date(apiResources.timestamp).toLocaleTimeString(),
          apiCpu: apiResources.cpu.usagePercent,
          apiMemory: apiResources.memory.workingSetMB,
          syncCpu: syncResources?.cpu?.usagePercent,
          syncMemory: syncResources?.memory?.workingSetMB,
        }
        const updated = [...prev, newPoint].slice(-20) // Keep last 20 points
        return updated
      })
    }
  }, [apiResources, syncResources])

  if (apiLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  if (apiError) {
    return (
      <Alert color="danger">
        Failed to load resource data: {(apiError as Error).message}
      </Alert>
    )
  }

  const highCpuUsage = (apiResources?.cpu.usagePercent || 0) > 70 || (syncResources?.cpu?.usagePercent || 0) > 70

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Box>
          <Typography level="h2">Resource Monitoring</Typography>
          <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
            Real-time CPU and memory usage
          </Typography>
        </Box>
        {highCpuUsage && (
          <Chip
            variant="soft"
            color="warning"
            startDecorator={<WarningIcon size={16} />}
          >
            High CPU Usage Detected
          </Chip>
        )}
      </Box>

      {/* Alert for high CPU */}
      {(apiResources?.cpu.usagePercent || 0) > 90 && (
        <Alert color="danger" sx={{ mb: 3 }} startDecorator={<AlertTriangle />}>
          <Box>
            <Typography level="title-md">Critical CPU Usage</Typography>
            <Typography level="body-sm">
              The API service is using {(apiResources?.cpu.usagePercent ?? 0).toFixed(1)}% CPU. 
              Consider scaling horizontally or optimizing the sync interval.
            </Typography>
          </Box>
        </Alert>
      )}

      {/* Resource Charts */}
      <Grid container spacing={3} sx={{ mb: 3 }}>
        <Grid xs={12}>
          <Card variant="outlined">
            <CardContent>
              <Typography level="title-lg" sx={{ mb: 2 }}>
                Resource Usage Over Time
              </Typography>
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={history}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="timestamp" />
                  <YAxis yAxisId="cpu" label={{ value: 'CPU %', angle: -90, position: 'insideLeft' }} />
                  <YAxis yAxisId="memory" orientation="right" label={{ value: 'Memory MB', angle: 90, position: 'insideRight' }} />
                  <Tooltip />
                  <Legend />
                  <Line yAxisId="cpu" type="monotone" dataKey="apiCpu" stroke="#667eea" name="API CPU %" strokeWidth={2} />
                  <Line yAxisId="cpu" type="monotone" dataKey="syncCpu" stroke="#f59e0b" name="Sync CPU %" strokeWidth={2} />
                  <Line yAxisId="memory" type="monotone" dataKey="apiMemory" stroke="#10b981" name="API Memory MB" strokeWidth={2} />
                  <Line yAxisId="memory" type="monotone" dataKey="syncMemory" stroke="#ef4444" name="Sync Memory MB" strokeWidth={2} />
                </LineChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      {/* API Service Resources */}
      <Typography level="h4" sx={{ mb: 2 }}>
        <ServerIcon size={20} style={{ verticalAlign: 'middle', marginRight: 8 }} />
        API Service
      </Typography>
      <Grid container spacing={2} sx={{ mb: 4 }}>
        <Grid xs={12} md={6}>
          <ResourceCard
            title="CPU Usage"
            icon={<CpuIcon size={20} />}
          >
            <MetricBar
              label="CPU"
              value={apiResources?.cpu.usagePercent || 0}
              max={100}
              unit="%"
            />
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                Cores: {apiResources?.cpu.cores}
              </Typography>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                Total Time: {(apiResources?.cpu.totalProcessorTime ?? 0).toFixed(1)}s
              </Typography>
            </Box>
          </ResourceCard>
        </Grid>

        <Grid xs={12} md={6}>
          <ResourceCard
            title="Memory Usage"
            icon={<MemoryIcon size={20} />}
          >
            <MetricBar
              label="Working Set"
              value={apiResources?.memory.workingSetMB || 0}
              max={512}
              unit="MB"
              color="success"
            />
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                Private: {(apiResources?.memory.privateMemoryMB ?? 0).toFixed(1)} MB
              </Typography>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                GC: {(apiResources?.memory.gcTotalMemoryMB ?? 0).toFixed(1)} MB
              </Typography>
            </Box>
          </ResourceCard>
        </Grid>

        <Grid xs={12} md={6}>
          <ResourceCard
            title="Threads"
            icon={<ThreadsIcon size={20} />}
          >
            <Typography level="h3" sx={{ mb: 1 }}>
              {apiResources?.threads.count}
            </Typography>
            <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
              Active threads in API process
            </Typography>
          </ResourceCard>
        </Grid>

        <Grid xs={12} md={6}>
          <ResourceCard
            title="Uptime"
            icon={<UptimeIcon size={20} />}
          >
            <Typography level="h3" sx={{ mb: 1 }}>
              {formatDuration(apiResources?.runtime.uptimeSeconds || 0)}
            </Typography>
            <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
              {apiResources?.runtime.dotnetVersion}
            </Typography>
          </ResourceCard>
        </Grid>
      </Grid>

      {/* Sync Service Resources */}
      {syncResources && (
        <>
          <Typography level="h4" sx={{ mb: 2 }}>
            <ThreadsIcon size={20} style={{ verticalAlign: 'middle', marginRight: 8 }} />
            Sync Service
          </Typography>
          <Grid container spacing={2}>
            <Grid xs={12} md={6}>
              <ResourceCard
                title="CPU Usage"
                icon={<CpuIcon size={20} />}
              >
                <MetricBar
                  label="CPU"
                  value={syncResources.cpu.usagePercent || 0}
                  max={100}
                  unit="%"
                  color="warning"
                />
                {(syncResources.cpu.usagePercent || 0) > 70 && (
                  <Alert color="warning" variant="soft" sx={{ mt: 2 }}>
                    <Typography level="body-sm">
                      High CPU usage detected in sync service. This is normal during sync operations.
                      Consider increasing SYNC__SYNCINTERVALSECONDS to reduce frequency.
                    </Typography>
                  </Alert>
                )}
              </ResourceCard>
            </Grid>

            <Grid xs={12} md={6}>
              <ResourceCard
                title="Memory Usage"
                icon={<MemoryIcon size={20} />}
              >
                <MetricBar
                  label="Working Set"
                  value={syncResources.memory.workingSetMB || 0}
                  max={512}
                  unit="MB"
                  color="success"
                />
                <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                  Private: {(syncResources?.memory?.privateMemoryMB ?? 0).toFixed(1)} MB
                </Typography>
              </ResourceCard>
            </Grid>

            <Grid xs={12} md={6}>
              <ResourceCard
                title="Threads"
                icon={<ThreadsIcon size={20} />}
              >
                <Typography level="h3" sx={{ mb: 1 }}>
                  {syncResources.threads.count}
                </Typography>
                <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                  Active threads in sync process
                </Typography>
              </ResourceCard>
            </Grid>

            <Grid xs={12} md={6}>
              <ResourceCard
                title="Uptime"
                icon={<UptimeIcon size={20} />}
              >
                <Typography level="h3" sx={{ mb: 1 }}>
                  {formatDuration((syncResources as any).uptimeSeconds || 0)}
                </Typography>
                <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                  Process ID: {(syncResources as any).processId}
                </Typography>
              </ResourceCard>
            </Grid>
          </Grid>
        </>
      )}

      {/* Recommendations */}
      <Card variant="soft" color="primary" sx={{ mt: 4 }}>
        <CardContent>
          <Typography level="title-md" sx={{ mb: 1 }}>
            💡 Optimization Tips
          </Typography>
          <Box component="ul" sx={{ pl: 2, mt: 1 }}>
            <Typography component="li" level="body-sm">
              If CPU usage is constantly high (100-300%), increase <code>SYNC__SYNCINTERVALSECONDS</code> to reduce sync frequency
            </Typography>
            <Typography component="li" level="body-sm" sx={{ mt: 0.5 }}>
              Use <code>SYNC__CONTINUOUSSYNC=false</code> for one-time sync jobs instead of continuous polling
            </Typography>
            <Typography component="li" level="body-sm" sx={{ mt: 0.5 }}>
              Limit synced items using Organization/Collection ID filters to reduce workload
            </Typography>
            <Typography component="li" level="body-sm" sx={{ mt: 0.5 }}>
              Scale API service horizontally by increasing <code>api.replicaCount</code> in Helm values
            </Typography>
          </Box>
        </CardContent>
      </Card>
    </Box>
  )
}
