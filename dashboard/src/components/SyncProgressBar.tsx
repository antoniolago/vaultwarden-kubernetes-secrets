import { useEffect, useState } from 'react'
import { Card, CardContent, Typography, LinearProgress, Box, Chip } from '@mui/joy'

interface SyncStatus {
  syncIntervalSeconds: number
  continuousSync: boolean
  lastSyncTime: string | null
  nextSyncTime: string | null
}

export default function SyncProgressBar() {
  const [syncStatus, setSyncStatus] = useState<SyncStatus | null>(null)
  const [progress, setProgress] = useState(0)
  const [timeRemaining, setTimeRemaining] = useState<string>('')
  const [error, setError] = useState<string | null>(null)

  // Fetch sync status from API
  useEffect(() => {
    const fetchSyncStatus = async () => {
      try {
        const response = await fetch('http://localhost:8080/api/dashboard/sync-status')
        if (response.ok) {
          const data: SyncStatus = await response.json()
          setSyncStatus(data)
          setError(null)
        } else {
          setError('Failed to fetch sync status')
        }
      } catch (err) {
        setError('Unable to connect to API')
      }
    }

    fetchSyncStatus()
    const interval = setInterval(fetchSyncStatus, 10000) // Refresh every 10 seconds

    return () => clearInterval(interval)
  }, [])

  // Update progress and time remaining every second
  useEffect(() => {
    if (!syncStatus?.lastSyncTime || !syncStatus?.nextSyncTime) {
      return
    }

    const updateProgress = () => {
      const now = new Date()
      // Ensure dates are parsed as UTC
      const lastSync = new Date(syncStatus.lastSyncTime!.endsWith('Z') 
        ? syncStatus.lastSyncTime! 
        : syncStatus.lastSyncTime! + 'Z')
      const nextSync = new Date(syncStatus.nextSyncTime!.endsWith('Z')
        ? syncStatus.nextSyncTime!
        : syncStatus.nextSyncTime! + 'Z')

      const totalDuration = nextSync.getTime() - lastSync.getTime()
      const elapsed = now.getTime() - lastSync.getTime()
      const remaining = nextSync.getTime() - now.getTime()

      // Calculate progress percentage
      const progressPercent = Math.min(100, Math.max(0, (elapsed / totalDuration) * 100))
      setProgress(progressPercent)

      // Format time remaining
      if (remaining <= 0) {
        setTimeRemaining('Syncing now...')
      } else {
        const remainingSeconds = Math.floor(remaining / 1000)
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
    const interval = setInterval(updateProgress, 1000)

    return () => clearInterval(interval)
  }, [syncStatus])

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
            <Box sx={{ 
              width: 32, 
              height: 32, 
              borderRadius: '50%', 
              bgcolor: 'danger.solidBg',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '1.2rem'
            }}>
              ⚠️
            </Box>
            <Box sx={{ flex: 1 }}>
              <Typography level="title-sm" fontWeight="bold">
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
    return (
      <Card 
        variant="soft" 
        sx={{ 
          bgcolor: 'success.softBg',
          borderLeft: '4px solid',
          borderColor: 'success.solidBg',
        }}
      >
        <CardContent sx={{ py: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <Box sx={{ 
              width: 32, 
              height: 32, 
              borderRadius: '50%', 
              bgcolor: 'success.solidBg',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '1.2rem',
              animation: 'spin 3s linear infinite',
              '@keyframes spin': {
                '0%': { transform: 'rotate(0deg)' },
                '100%': { transform: 'rotate(360deg)' },
              },
            }}>
              🔄
            </Box>
            <Box sx={{ flex: 1 }}>
              <Typography level="title-sm" fontWeight="bold">
                Continuous Sync Mode
              </Typography>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                Syncing automatically on changes • No scheduled interval
              </Typography>
            </Box>
          </Box>
        </CardContent>
      </Card>
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

  const progressColor = progress >= 95 ? 'success' : progress >= 75 ? 'warning' : 'primary'
  const statusEmoji = progress >= 95 ? '🔄' : '⏱️'

  return (
    <Card 
      variant="soft" 
      sx={{ 
        bgcolor: `${progressColor}.softBg`,
        borderLeft: '4px solid',
        borderColor: `${progressColor}.solidBg`,
        transition: 'all 0.3s ease',
        p: 0.7
      }}
    >
      <CardContent sx={{ py: 1.5 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {/* Status Icon */}
          <Box sx={{ 
            borderRadius: '10%', 
            bgcolor: `${progressColor}.solidBg`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '1.2rem',
            animation: progress >= 95 ? 'pulse 2s infinite' : 'none',
            '@keyframes pulse': {
              '0%, 100%': { opacity: 1 },
              '50%': { opacity: 0.6 },
            },
          }}>
            {statusEmoji}
          </Box>

          {/* Info & Progress */}
          <Box sx={{ flex: 1 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.5 }}>
              {/* <Typography level="title-sm" fontWeight="bold">
                Next Sync
              </Typography> */}
              <Chip 
                size="sm" 
                variant="solid"
                color={progressColor}
                sx={{ fontWeight: 'bold', minWidth: 60, textAlign: 'center' }}
              >
                {timeRemaining}
              </Chip>
            </Box>
            
            <LinearProgress 
              determinate 
              value={progress} 
              sx={{ 
                height: 6, 
                borderRadius: 3,
                mb: 0.5,
                bgcolor: 'background.level1'
              }}
              color={progressColor}
            />
            
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                {Math.floor(syncStatus.syncIntervalSeconds / 60)}min
              </Typography>
              <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
                {Math.round(progress)}% complete
              </Typography>
            </Box>
          </Box>
        </Box>
      </CardContent>
    </Card>
  )
}
