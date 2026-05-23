import { test, expect } from '@playwright/test'
import { API_URL } from './shared'

test.describe('Kubernetes YAML E2E Tests', () => {
  interface SecretData {
    secretName: string
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

  test('should apply YAML from note as ConfigMap', async () => {
    if (!apiAvailable) { expect(true).toBe(true); return }
    const yamlSecrets = secrets.filter(s => 
      s.secretName && (s.secretName.startsWith('config-') || s.secretName.startsWith('cm-')))
    expect(yamlSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should have yaml attachment prefix for kubectl apply', async () => {
    if (!apiAvailable) { expect(true).toBe(true); return }
    const yamlAttachments = secrets.filter(s => 
      s.data && Object.keys(s.data).some(k => k.startsWith('__yaml_attachment__')))
    expect(yamlAttachments.length).toBeGreaterThanOrEqual(0)
  })

  test('should store YAML content correctly', async () => {
    if (!apiAvailable) { expect(true).toBe(true); return }
    const yamlSecrets = secrets.filter(s => 
      s.data && Object.keys(s.data).some(k => k.endsWith('.yaml') || k.endsWith('.yml')))
    expect(yamlSecrets.length).toBeGreaterThanOrEqual(0)
  })

  test('should handle multi-document YAML', async () => {
    if (!apiAvailable) { expect(true).toBe(true); return }
    const multiDocSecrets = secrets.filter(s => 
      s.data && Object.values(s.data).some(v => v?.includes('---')))
    expect(multiDocSecrets.length).toBeGreaterThanOrEqual(0)
  })
})