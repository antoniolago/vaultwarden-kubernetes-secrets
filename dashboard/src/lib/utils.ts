import { format, formatDistance } from 'date-fns'

export function formatDate(date: string | null): string {
  if (!date) return 'Never'
  return format(new Date(date), 'PPpp')
}

export function formatRelative(date: string | null): string {
  if (!date) return 'Never'
  return formatDistance(new Date(date), new Date(), { addSuffix: true })
}

export function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds.toFixed(1)}s`
  if (seconds < 3600) return `${(seconds / 60).toFixed(1)}m`
  return `${(seconds / 3600).toFixed(1)}h`
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)}MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)}GB`
}

export function getStatusColor(status: string): 'success' | 'danger' | 'warning' | 'neutral' {
  switch (status.toLowerCase()) {
    case 'success':
    case 'completed':
    case 'active':
      return 'success'
    case 'failed':
    case 'error':
      return 'danger'
    case 'running':
    case 'pending':
      return 'warning'
    default:
      return 'neutral'
  }
}
