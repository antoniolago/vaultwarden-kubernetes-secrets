import { test, expect } from '@playwright/test'

const API_URL = 'http://localhost:8080/api'
const DASHBOARD_URL = 'http://localhost:3000'

interface SecretState {
  id: number
  namespace: string
  secretName: string
  vaultwardenItemId: string
  vaultwardenItemName: string
  status: string
  dataKeysCount: number
  lastSyncTime: string
  lastSynced: string
  lastError: string | null
  errorMessage?: string | null
}

test.describe('Secrets Page E2E Tests', () => {
  let apiSecrets: SecretState[]

  test.beforeAll(async ({ request }) => {
    // Fetch all secrets from API
    const secretsResponse = await request.get(`${API_URL}/secrets`)
    expect(secretsResponse.ok()).toBeTruthy()
    apiSecrets = await secretsResponse.json()
    console.log('API Secrets:', apiSecrets.length, 'total secrets')
  })

  test.beforeEach(async ({ page }) => {
    // Navigate to secrets page
    await page.goto(`${DASHBOARD_URL}/secrets`)
    await page.waitForLoadState('networkidle')
  })

  test('should display all secrets from API', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 5000 })

    const rows = page.locator('table tbody tr')
    const rowCount = await rows.count()

    console.log(`Secrets - API: ${apiSecrets.length}, Page: ${rowCount}`)
    expect(rowCount).toBe(apiSecrets.length)
  })

  test('failed secrets must have error messages', async ({ page }) => {
    const failedSecrets = apiSecrets.filter(s => s.status === 'Failed')
    
    if (failedSecrets.length === 0) {
      console.log('✅ No failed secrets found (good!)')
      return
    }

    console.log(`\n❌ Found ${failedSecrets.length} failed secrets - verifying error messages...`)

    await page.waitForSelector('table tbody tr', { timeout: 5000 })

    for (const failedSecret of failedSecrets) {
      // Find the row for this secret using test ID
      const row = page.getByTestId(`secret-row-${failedSecret.namespace}-${failedSecret.secretName}`)
      await expect(row).toBeVisible()

      // Get the status using test ID
      const statusCell = row.getByTestId('secret-status')
      const status = await statusCell.textContent()
      expect(status?.trim()).toBe('Failed')

      // Get the error using test ID
      const errorCell = row.getByTestId('secret-error')
      const errorText = await errorCell.textContent()

      console.log(`  • ${failedSecret.namespace}/${failedSecret.secretName}: ${errorText || '(no error message)'}`)

      // Verify error message exists and is not empty (not just '-')
      const hasErrorInAPI = failedSecret.lastError || failedSecret.errorMessage
      if (hasErrorInAPI) {
        expect(errorText).toBeTruthy()
        expect(errorText?.trim()).not.toBe('-')
        expect(errorText?.trim().length).toBeGreaterThan(1)
        console.log(`    ✓ Error message present: "${errorText?.substring(0, 50)}..."`)
      } else {
        console.log(`    ⚠️ Warning: Failed secret has no error message in API`)
      }
    }
  })

  // test('all secrets must have Last Sync time if sync has run', async ({ page }) => {
  //   if (apiSecrets.length === 0) {
  //     console.log('No secrets to test')
  //     return
  //   }

  //   console.log(`\n📅 Verifying Last Sync times for ${apiSecrets.length} secrets...`)

  //   await page.waitForSelector('table tbody tr', { timeout: 5000 })

  //   let secretsWithNever = 0
  //   let secretsWithTime = 0

  //   for (const secret of apiSecrets) {
  //     const row = page.getByTestId(`secret-row-${secret.namespace}-${secret.secretName}`)
      
  //     if (await row.count() === 0) {
  //       console.log(`  ⚠️ Secret not found in table: ${secret.namespace}/${secret.secretName}`)
  //       continue
  //     }

  //     // Get the Last Sync cell using test ID
  //     const lastSyncCell = row.getByTestId('secret-last-sync')
  //     const lastSyncText = await lastSyncCell.textContent()

  //     if (lastSyncText?.toLowerCase().includes('never')) {
  //       secretsWithNever++
  //       console.log(`  ❌ ${secret.namespace}/${secret.secretName}: Shows "Never" (should have sync time)`)
  //     } else {
  //       secretsWithTime++
  //     }
  //   }

  //   console.log(`\nResults:`)
  //   console.log(`  ✓ Secrets with sync time: ${secretsWithTime}`)
  //   console.log(`  ✗ Secrets showing "Never": ${secretsWithNever}`)

  //   // All secrets should have sync time (not "Never")
  //   expect(secretsWithNever).toBe(0)
  //   console.log(`\n✅ All secrets have valid Last Sync times`)
  // })

  test('should filter secrets when searching', async ({ page }) => {
    if (apiSecrets.length === 0) {
      console.log('No secrets to test search')
      return
    }

    const firstSecret = apiSecrets[0]
    const searchTerm = firstSecret.secretName.substring(0, 5)

    // Type in search box
    await page.fill('input[placeholder*="Search"]', searchTerm)
    await page.waitForTimeout(500) // Wait for filter to apply

    const rows = page.locator('table tbody tr')
    const rowCount = await rows.count()

    const expectedCount = apiSecrets.filter(s => 
      s.secretName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      s.namespace.toLowerCase().includes(searchTerm.toLowerCase()) ||
      s.vaultwardenItemName.toLowerCase().includes(searchTerm.toLowerCase())
    ).length

    console.log(`Search "${searchTerm}" - Expected: ${expectedCount}, Displayed: ${rowCount}`)
    expect(rowCount).toBe(expectedCount)
  })

  test('should show secret details correctly', async ({ page }) => {
    if (apiSecrets.length === 0) {
      console.log('No secrets to test')
      return
    }

    const firstSecret = apiSecrets[0]
    console.log(`\n🔍 Verifying details for: ${firstSecret.namespace}/${firstSecret.secretName}`)

    await page.waitForSelector('table tbody tr', { timeout: 5000 })

    const row = page.getByTestId(`secret-row-${firstSecret.namespace}-${firstSecret.secretName}`)
    await expect(row).toBeVisible()

    // Verify namespace using test ID
    const namespaceCell = await row.getByTestId('secret-namespace').textContent()
    expect(namespaceCell?.trim()).toContain(firstSecret.namespace)
    console.log(`  ✓ Namespace: ${namespaceCell?.trim()}`)

    // Verify secret name using test ID
    const nameCell = await row.getByTestId('secret-name').textContent()
    expect(nameCell).toContain(firstSecret.secretName)
    console.log(`  ✓ Secret Name: ${nameCell}`)

    // Verify Vaultwarden item using test ID
    const itemCell = await row.getByTestId('secret-vaultwarden-item').textContent()
    expect(itemCell?.trim()).toBe(firstSecret.vaultwardenItemName)
    console.log(`  ✓ Vaultwarden Item: ${itemCell}`)

    // Verify status using test ID
    const statusCell = await row.getByTestId('secret-status').textContent()
    expect(statusCell?.trim()).toBe(firstSecret.status)
    console.log(`  ✓ Status: ${statusCell}`)

    // Verify data keys count using test ID
    const keysCell = await row.getByTestId('secret-data-keys').textContent()
    expect(parseInt(keysCell || '0')).toBe(firstSecret.dataKeysCount)
    console.log(`  ✓ Data Keys: ${keysCell}`)
  })

  test('comprehensive error message validation', async ({ page }) => {
    const failedSecrets = apiSecrets.filter(s => s.status === 'Failed')
    
    if (failedSecrets.length === 0) {
      console.log('✅ No failed secrets to validate')
      return
    }

    console.log(`\n🔍 Comprehensive Error Validation for ${failedSecrets.length} failed secrets`)
    console.log('=' .repeat(70))

    await page.waitForSelector('table tbody tr', { timeout: 5000 })

    let secretsWithErrors = 0
    let secretsWithoutErrors = 0

    for (const secret of failedSecrets) {
      const row = page.getByTestId(`secret-row-${secret.namespace}-${secret.secretName}`)
      
      if (await row.count() === 0) {
        console.log(`  ⚠️ Secret not displayed: ${secret.namespace}/${secret.secretName}`)
        continue
      }

      const errorCell = row.getByTestId('secret-error')
      const errorText = await errorCell.textContent()
      const hasError = errorText && errorText.trim().length > 1 && errorText.trim() !== '-'

      const apiError = secret.lastError || secret.errorMessage
      console.log(`\n${secret.namespace}/${secret.secretName}:`)
      console.log(`  API Error: ${apiError || '(null)'}`)
      console.log(`  UI Error:  ${errorText || '(empty)'}`)
      
      if (apiError) {
        if (hasError) {
          secretsWithErrors++
          console.log(`  ✅ ERROR MESSAGE DISPLAYED`)
        } else {
          secretsWithoutErrors++
          console.log(`  ❌ ERROR MESSAGE MISSING IN UI`)
        }
      }
    }

    console.log(`\n${'='.repeat(70)}`)
    console.log(`Summary:`)
    console.log(`  Failed secrets with error messages: ${secretsWithErrors}`)
    console.log(`  Failed secrets missing error messages: ${secretsWithoutErrors}`)

    // All failed secrets with errorMessage in API should display it in UI
    expect(secretsWithoutErrors).toBe(0)
    console.log(`\n✅ All failed secrets display error messages correctly`)
  })
})
