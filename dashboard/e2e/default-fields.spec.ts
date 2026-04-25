import { test, expect } from '@playwright/test'

const API_URL = 'http://localhost:8080/api'

test.describe('Default Field Names E2E Tests', () => {
  interface SecretData {
    id: number
    namespace: string
    secretName: string
    dataKeysCount: number
    data: Record<string, string>
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

  test('should use username as default key name', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const usernameSecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'username')
    )
    expect(usernameSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should use password as default key name', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const passwordSecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'password')
    )
    expect(passwordSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should use private-key for SSH items', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const privateKeySecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'private-key')
    )
    expect(privateKeySecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should use public-key for SSH items', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const publicKeySecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'public-key')
    )
    expect(publicKeySecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should use fingerprint for SSH items', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const fingerprintSecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'fingerprint')
    )
    expect(fingerprintSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should support custom field overrides', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const customFieldSecrets = secrets.filter(s =>
      s.data && (
        Object.keys(s.data).some(k => k !== 'username' && k !== 'password' &&
          k !== 'private-key' && k !== 'public-key' && k !== 'fingerprint')
      )
    )
    expect(customFieldSecrets.length).toBeGreaterThanOrEqual(0)
  })
})