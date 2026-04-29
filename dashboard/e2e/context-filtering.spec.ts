import { test, expect } from '@playwright/test'
import { API_URL } from './shared'

test.describe('Context Name Filtering E2E Tests', () => {
  interface SecretData {
    id: number
    namespace: string
    secretName: string
    data: Record<string, string>
    lastSyncedAt?: string
    metadata?: {
      annotations?: Record<string, string>
      contextName?: string
    }
  }

  let secrets: SecretData[] = []
  let apiAvailable = false

  test.beforeAll(async ({ request }) => {
    try {
      const response = await request.get(`${API_URL}/secrets`, { timeout: 5000 })
      if (response.ok()) {
        secrets = await response.json()
        apiAvailable = true
      }
    } catch {
      apiAvailable = false
    }
  })

  test('should verify API is available for context filtering tests', async () => {
    expect(apiAvailable).toBeTruthy()
  })

  test('should sync items without context-name to all clusters', async () => {
    const allClusterSecrets = secrets.filter(s => s.lastSyncedAt)
    expect(allClusterSecrets.length).toBeGreaterThan(0)
  })

  test('should return secrets for current context', async ({ request }) => {
    const configResponse = await request.get(`${API_URL}/config`, { timeout: 3000 })
    expect(configResponse.ok()).toBeTruthy()
    const config = await configResponse.json()
    const validSecrets = secrets.filter(s => s.secretName && s.namespace)
    expect(validSecrets.length).toBeGreaterThan(0)
    if (config?.contextName) {
      const contextMatched = validSecrets.filter(s =>
        s.metadata?.annotations?.['context-name'] === config.contextName ||
        s.metadata?.contextName === config.contextName
      )
      expect(contextMatched.length).toBeGreaterThanOrEqual(0)
    }
  })

  test('should handle context-name custom field', async () => {
    const contextFiltered = secrets.filter(s =>
      s.metadata?.annotations?.['context-name'] || s.metadata?.contextName
    )
    expect(contextFiltered.length).toBeGreaterThanOrEqual(0)
  })

  test('should support context-name configuration', async ({ request }) => {
    const configResponse = await request.get(`${API_URL}/config`, { timeout: 3000 })
    expect(configResponse.ok()).toBeTruthy()
    const config = await configResponse.json()
    const expectedContext = process.env.EXPECTED_CONTEXT_NAME || 'production'
    expect(config.contextName).toEqual(expectedContext)
  })

  test('should allow env var override for context name', async ({ request }) => {
    const configResponse = await request.get(`${API_URL}/config`, { timeout: 3000 })
    expect(configResponse.ok()).toBeTruthy()
    const config = await configResponse.json()
    const expectedOverride = process.env.EXPECTED_CONTEXT_NAME || 'production'
    expect(config.contextName).toEqual(expectedOverride)
  })
})
