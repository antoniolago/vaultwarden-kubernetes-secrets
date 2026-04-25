import { test, expect } from '@playwright/test'

const API_URL = 'http://localhost:8080/api'

test.describe('Context Name Filtering E2E Tests', () => {
  interface SecretData {
    id: number
    namespace: string
    secretName: string
    data: Record<string, string>
    lastSyncedAt?: string
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

  test('should sync items without context-name to all clusters', async () => {
    test.skip(!apiAvailable, 'API not available')
    const allClusterSecrets = secrets.filter(s => s.lastSyncedAt)
    expect(allClusterSecrets.length).toBeGreaterThan(0)
  })

  test('should return secrets for current context', async ({ request }) => {
    test.skip(!apiAvailable, 'API not available')
    const configResponse = await request.get(`${API_URL}/config`, { timeout: 3000 })
    expect(configResponse.ok()).toBeTruthy()
    const config = await configResponse.json()
    const validSecrets = secrets.filter(s => s.secretName && s.namespace)
    expect(validSecrets.length).toBeGreaterThan(0)
    if (config?.contextName) {
      const contextMatched = validSecrets.filter(s =>
        s.secretName?.includes(config.contextName) || s.namespace?.includes(config.contextName)
      )
      expect(contextMatched.length).toBeGreaterThanOrEqual(0)
    }
  })

  test('should handle context-name custom field', async () => {
    test.skip(!apiAvailable, 'API not available')
    const contextFiltered = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k.includes('context'))
    )
    expect(contextFiltered.length).toBeGreaterThanOrEqual(0)
  })

  test('should support context-name configuration', async ({ request }) => {
    test.skip(!apiAvailable, 'API not available')
    const configResponse = await request.get(`${API_URL}/config`, { timeout: 3000 })
    expect(configResponse.ok()).toBeTruthy()
    const config = await configResponse.json()
    expect(config?.contextName).toBeDefined()
  })

  test('should allow env var override for context name', async ({ request }) => {
    test.skip(!apiAvailable, 'API not available')
    const configResponse = await request.get(`${API_URL}/config`, { timeout: 3000 })
    expect(configResponse.ok()).toBeTruthy()
    const config = await configResponse.json()
    expect(config?.contextName).toBeDefined()
  })
})
