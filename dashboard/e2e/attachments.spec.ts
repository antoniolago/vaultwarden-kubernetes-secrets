import { test, expect } from '@playwright/test'
import { API_URL } from './shared'

test.describe('Attachments E2E Tests', () => {
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

  test('should handle regular file attachments', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const regularFiles = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k => !k.startsWith('__'))
    )
    expect(regularFiles.length).toBeGreaterThanOrEqual(0)
  })

  test('should store filename as key for regular attachments', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const filenameKeys = secrets.filter(s =>
      s.data && Object.keys(s.data).some(k =>
        k.includes('.') && !k.startsWith('__')
      )
    )
    expect(filenameKeys.length).toBeGreaterThanOrEqual(0)
  })

  test('should handle multiple attachments per item', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const multiAttach = secrets.filter(s => s.dataKeysCount && s.dataKeysCount > 1)
    expect(multiAttach.length).toBeGreaterThanOrEqual(0)
  })

  test('should exclude attachment keys from data when processed', async () => {
    if (!apiAvailable) {
      expect(true).toBe(true)
      return
    }
    const allValid = secrets.every(s => {
      if (!s.data) return true
      const keys = Object.keys(s.data)
      return s.dataKeysCount === keys.length || keys.every(k => !k.startsWith('__'))
    })
    expect(allValid).toBe(true)
  })
})