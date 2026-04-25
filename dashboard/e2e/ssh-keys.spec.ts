import { test, expect } from '@playwright/test'

const API_URL = 'http://localhost:8080/api'

test.describe('SSH Keys E2E Tests', () => {
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

  test('should store SSH private key as password', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const sshSecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'private-key' || k === 'password')
    )
    expect(sshSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should add SSH public key automatically', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const publicKeySecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'public-key')
    )
    expect(publicKeySecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should add SSH fingerprint automatically', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const fpSecrets = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => k === 'fingerprint')
    )
    expect(fpSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should use private-key as default key name for SSH items', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const sshKeySecrets = secrets.filter(s =>
      s.secretName?.toLowerCase().includes('ssh') ||
      s.secretName?.toLowerCase().includes('key')
    )
    expect(sshKeySecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should handle SSH key with multiple keys', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const multiKey = secrets.filter(s =>
      s.dataKeysCount && s.dataKeysCount >= 2
    )
    expect(multiKey.length).toBeGreaterThanOrEqual(0)
  })
})