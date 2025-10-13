const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:8080/api'

function getToken(): string | null {
  return localStorage.getItem('auth_token')
}

async function fetchWithAuth(url: string, options: RequestInit = {}) {
  const token = getToken()
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }

  // Merge existing headers
  if (options.headers) {
    Object.entries(options.headers).forEach(([key, value]) => {
      if (typeof value === 'string') {
        headers[key] = value
      }
    })
  }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  try {
    const response = await fetch(`${API_URL}${url}`, {
      ...options,
      headers,
      // Remove credentials in dev if CORS doesn't allow it
      // credentials: 'include',
    })

    if (response.status === 401) {
      localStorage.removeItem('auth_token')
      window.location.href = '/login'
      throw new Error('Unauthorized')
    }

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(`API error: ${response.statusText} - ${errorText}`)
    }

    return response.json()
  } catch (error) {
    if (error instanceof Error && error.message.includes('Failed to fetch')) {
      throw new Error('Cannot connect to API. Make sure the API is running on ' + API_URL)
    }
    throw error
  }
}

export interface DashboardOverview {
  totalSyncs: number
  successfulSyncs: number
  failedSyncs: number
  activeSecrets: number
  totalNamespaces: number
  lastSyncTime: string | null
  averageSyncDuration: number
  successRate: number
}

export interface SyncLog {
  id: number
  startTime: string
  endTime: string | null
  status: string
  totalItems: number
  createdSecrets: number
  updatedSecrets: number
  skippedSecrets: number
  failedSecrets: number
  deletedSecrets: number
  errorMessage: string | null
  durationSeconds: number
}

export interface SecretState {
  id: number
  namespace: string
  secretName: string
  vaultwardenItemId: string
  vaultwardenItemName: string
  status: string
  dataKeysCount: number
  lastSyncTime: string
  lastSynced: string  // API returns this
  lastError: string | null  // API returns this, not errorMessage
  errorMessage?: string | null  // Keep for backward compatibility
}

export interface SystemResources {
  cpu: {
    usagePercent: number
    cores: number
    totalProcessorTime: number
  }
  memory: {
    workingSetMB: number
    privateMemoryMB: number
    gcTotalMemoryMB: number
  }
  threads: {
    count: number
  }
  runtime: {
    uptimeSeconds: number
    dotnetVersion: string
    osDescription: string
  }
  timestamp: string
}

export interface NamespaceStats {
  namespace: string
  secretCount: number
  activeSecrets: number
  failedSecrets: number
  totalDataKeys: number
  lastSyncTime: string | null
  successRate: number
}

export const api = {
  // Dashboard
  getDashboardOverview: (): Promise<DashboardOverview> =>
    fetchWithAuth('/dashboard/overview'),

  getTimeline: (days: number = 7): Promise<any[]> =>
    fetchWithAuth(`/dashboard/timeline?days=${days}`),

  getNamespaces: (): Promise<NamespaceStats[]> =>
    fetchWithAuth('/dashboard/namespaces'),

  // Sync Logs
  getSyncLogs: (limit: number = 50): Promise<SyncLog[]> =>
    fetchWithAuth(`/synclogs?limit=${limit}`),

  getSyncLog: (id: number): Promise<SyncLog> =>
    fetchWithAuth(`/synclogs/${id}`),

  // Secrets
  getSecrets: (): Promise<SecretState[]> =>
    fetchWithAuth('/secrets'),

  getActiveSecrets: (): Promise<SecretState[]> =>
    fetchWithAuth('/secrets/active'),

  getSecretsByNamespace: (namespace: string): Promise<SecretState[]> =>
    fetchWithAuth(`/secrets/namespace/${namespace}`),

  getSecretsByNamespaceAndStatus: (namespace: string, status: string): Promise<SecretState[]> =>
    fetchWithAuth(`/secrets/namespace/${namespace}/status/${status}`),

  // System Resources
  getSystemResources: (): Promise<SystemResources> =>
    fetchWithAuth('/system/resources'),

  getSyncServiceResources: (): Promise<SystemResources> =>
    fetchWithAuth('/system/sync-service-resources'),

  // Test connection
  testConnection: async (token: string): Promise<boolean> => {
    try {
      const response = await fetch(`${API_URL}/dashboard/overview`, {
        headers: {
          'Authorization': `Bearer ${token}`,
        },
      })
      return response.ok
    } catch {
      return false
    }
  },
}
