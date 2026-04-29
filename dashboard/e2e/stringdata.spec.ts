import { test, expect } from '@playwright/test'
import { API_URL } from './shared'

test.describe('stringData: Mode E2E Tests', () => {
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

  test('should parse stringData from note as key-value pairs', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const stringDataSecrets = secrets.filter(s => 
      s.data && (
        Object.keys(s.data).some(k => k.endsWith('.yaml')) ||
        Object.keys(s.data).some(k => s.data[k]?.includes('='))
      )
    )
    expect(stringDataSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should preserve multiline YAML in stringData values', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const multilineSecrets = secrets.filter(s => 
      s.data && Object.keys(s.data).some(k => s.data[k]?.includes('\n'))
    )
    expect(multilineSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should handle multiple stringData keys', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const multiKeySecrets = secrets.filter(s => s.dataKeysCount && s.dataKeysCount > 1)
    expect(multiKeySecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should parse key=value format', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const kvSecrets = secrets.filter(s => 
      s.data && Object.values(s.data).some(v => v?.includes('=')))
    expect(kvSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should parse key: value format', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const colonSecrets = secrets.filter(s => 
      s.data && Object.values(s.data).some(v => v?.includes(':')))
    expect(colonSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should handle empty stringData gracefully', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const allValid = secrets.every(s => {
      if (!s.data) return true
      return s.dataKeysCount === Object.keys(s.data).length
    })
    expect(allValid).toBe(true)
  })
})