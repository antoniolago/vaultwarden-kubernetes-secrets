import { test as base } from '@playwright/test'

// API URL - use env var or default to localhost:8080
export const API_URL = process.env.API_URL || 'http://localhost:8080/api'
// Dashboard URL - use env var or default to localhost:3000
export const DASHBOARD_URL = process.env.DASHBOARD_URL || 'http://localhost:3000'

/**
 * Polls the API until the sync has completed at least once.
 * Returns true if sync completed, false if timeout.
 */
export async function waitForSyncComplete(request: any, maxRetries = 30, retryDelay = 1000): Promise<boolean> {
  for (let i = 0; i < maxRetries; i++) {
    try {
      const response = await request.get(`${API_URL}/dashboard/overview`)
      if (response.ok()) {
        const data = await response.json()
        if (data.totalSyncs > 0) {
          return true
        }
      }
    } catch {
      // API not ready yet
    }
    console.log(`⏳ Waiting for sync to complete (attempt ${i + 1}/${maxRetries})...`)
    await new Promise(r => setTimeout(r, retryDelay))
  }
  return false
}
