import { format, formatDistance } from 'date-fns'

export function formatDate(date: string | null): string {
  if (!date) return 'Never'
  // Ensure the date string is treated as UTC if it doesn't have timezone info
  const dateStr = date.endsWith('Z') ? date : date + 'Z'
  return format(new Date(dateStr), 'PPpp')
}

export function formatRelative(date: string | null): string {
  if (!date) return 'Never'
  // Ensure the date string is treated as UTC if it doesn't have timezone info
  const dateStr = date.endsWith('Z') ? date : date + 'Z'
  return formatDistance(new Date(dateStr), new Date(), { addSuffix: true })
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
    case 'partial':
    case 'running':
    case 'pending':
      return 'warning'
    default:
      return 'neutral'
  }
}

export interface SyncLogCounts {
  status: string
  failedSecrets: number
  createdSecrets: number
  updatedSecrets: number
  skippedSecrets: number
}

/**
 * Computes a derived status for sync operations.
 * Returns 'Partial' if there are failures but also some successful operations.
 * Otherwise returns the original status.
 */
export function computeSyncStatus(log: SyncLogCounts): string {
  const hasFailures = log.failedSecrets > 0
  const hasSuccesses = log.createdSecrets > 0 || log.updatedSecrets > 0 || log.skippedSecrets > 0
  
  // If there are both failures and successes, it's a partial success
  if (hasFailures && hasSuccesses) {
    return 'Partial'
  }
  
  // If there are only failures and nothing else, keep it as Failed
  if (hasFailures && !hasSuccesses) {
    return 'Failed'
  }
  
  // Otherwise use the original status from backend
  return log.status
}
