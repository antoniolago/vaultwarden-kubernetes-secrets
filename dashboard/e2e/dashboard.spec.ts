import { test, expect } from '@playwright/test'

const API_URL = 'http://localhost:8080/api'
const DASHBOARD_URL = 'http://localhost:3000'

interface DashboardOverview {
  totalSyncs: number
  successfulSyncs: number
  failedSyncs: number
  activeSecrets: number
  totalNamespaces: number
  lastSyncTime: string | null
  averageSyncDuration: number
  successRate: number
}

interface NamespaceStats {
  namespace: string
  secretCount: number
  activeSecrets: number
  failedSecrets: number
  totalDataKeys: number
  lastSyncTime: string | null
  successRate: number
}

test.describe('Dashboard E2E Tests', () => {
  let apiOverview: DashboardOverview
  let apiNamespaces: NamespaceStats[]

  test.beforeAll(async ({ request }) => {
    // Fetch API data once before all tests
    const overviewResponse = await request.get(`${API_URL}/dashboard/overview`)
    expect(overviewResponse.ok()).toBeTruthy()
    apiOverview = await overviewResponse.json()
    console.log('API Overview:', apiOverview)

    const namespacesResponse = await request.get(`${API_URL}/dashboard/namespaces`)
    expect(namespacesResponse.ok()).toBeTruthy()
    apiNamespaces = await namespacesResponse.json()
    console.log('API Namespaces:', apiNamespaces.length, 'namespaces')
  })

  test('should load without import errors', async ({ page }) => {
    const errors: string[] = []
    
    page.on('pageerror', (error) => {
      errors.push(error.toString())
    })

    await page.goto(DASHBOARD_URL)
    await page.waitForLoadState('networkidle')

    if (errors.length > 0) {
      console.log('❌ Page errors found:', errors)
      expect(errors).toHaveLength(0)
    } else {
      console.log('✅ No import or runtime errors detected')
    }
  })

  test.beforeEach(async ({ page }) => {
    // Navigate to the dashboard
    await page.goto(DASHBOARD_URL)
    // Wait for the dashboard to load
    await page.waitForLoadState('networkidle')
    await page.waitForSelector('text=📊 Dashboard Overview', { timeout: 10000 })
  })

  test('should display correct Active Secrets count from API', async ({ page }) => {
    const displayedValue = await page.getByTestId('stat-active-secrets-value').textContent()
    const displayedCount = parseInt(displayedValue || '0')

    console.log(`Active Secrets - API: ${apiOverview.activeSecrets}, Dashboard: ${displayedCount}`)
    expect(displayedCount).toBe(apiOverview.activeSecrets)
    expect(displayedCount).toBeGreaterThan(0)
  })

  test('should display correct Total Data Keys count', async ({ page }) => {
    const displayedValue = await page.getByTestId('stat-total-keys-value').textContent()
    const displayedCount = parseInt(displayedValue || '0')

    const expectedCount = apiNamespaces.reduce((sum, ns) => sum + ns.totalDataKeys, 0)
    console.log(`Total Data Keys - Expected: ${expectedCount}, Dashboard: ${displayedCount}`)
    expect(displayedCount).toBe(expectedCount)
  })

  test('should display correct Namespaces count from API', async ({ page }) => {
    const displayedValue = await page.getByTestId('stat-namespaces-value').textContent()
    const displayedCount = parseInt(displayedValue || '0')

    console.log(`Namespaces - API: ${apiOverview.totalNamespaces}, Dashboard: ${displayedCount}`)
    expect(displayedCount).toBe(apiOverview.totalNamespaces)
    expect(displayedCount).toBeGreaterThan(0)
  })

  test('should display correct success rate in Sync Performance card', async ({ page }) => {
    const subtitle = await page.getByTestId('stat-sync-performance-subtitle').textContent()
    
    const expectedRate = apiOverview.successRate.toFixed(1)
    console.log(`Success Rate - API: ${expectedRate}%, Dashboard subtitle: ${subtitle}`)
    expect(subtitle).toContain(`${expectedRate}%`)
  })

  test('should display correct number of namespace rows matching API', async ({ page }) => {
    await expect(page.getByTestId('namespaces-table-card')).toBeVisible()
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    const rows = page.getByTestId('namespaces-table').locator('tbody tr')
    const rowCount = await rows.count()

    console.log(`Namespaces - API: ${apiNamespaces.length}, Dashboard table rows: ${rowCount}`)
    expect(rowCount).toBe(apiNamespaces.length)
  })

  test('should display correct namespace data in table rows', async ({ page }) => {
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    // Verify first namespace data matches API
    const firstNs = apiNamespaces[0]
    const firstRow = page.getByTestId(`namespace-row-${firstNs.namespace}`)

    const nameCell = await firstRow.getByTestId('namespace-name').textContent()
    expect(nameCell?.trim()).toBe(firstNs.namespace)

    const totalSecretsText = await firstRow.getByTestId('chip-total-secrets').textContent()
    const totalSecrets = parseInt(totalSecretsText || '0')
    console.log(`${firstNs.namespace} Total Secrets - API: ${firstNs.secretCount}, Dashboard: ${totalSecrets}`)
    expect(totalSecrets).toBe(firstNs.secretCount)

    const activeSecretsText = await firstRow.getByTestId('chip-active-secrets').textContent()
    const activeSecrets = parseInt(activeSecretsText || '0')
    console.log(`${firstNs.namespace} Active - API: ${firstNs.activeSecrets}, Dashboard: ${activeSecrets}`)
    expect(activeSecrets).toBe(firstNs.activeSecrets)

    const failedSecretsText = await firstRow.getByTestId('chip-failed-secrets').textContent()
    const failedSecrets = parseInt(failedSecretsText || '0')
    console.log(`${firstNs.namespace} Failed - API: ${firstNs.failedSecrets}, Dashboard: ${failedSecrets}`)
    expect(failedSecrets).toBe(firstNs.failedSecrets)

    const dataKeysText = await firstRow.getByTestId('chip-data-keys').textContent()
    const dataKeys = parseInt(dataKeysText || '0')
    console.log(`${firstNs.namespace} Data Keys - API: ${firstNs.totalDataKeys}, Dashboard: ${dataKeys}`)
    expect(dataKeys).toBe(firstNs.totalDataKeys)
  })

  test('should verify all namespaces match API data', async ({ page }) => {
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    // Verify all namespaces
    for (const apiNs of apiNamespaces) {
      const row = page.getByTestId(`namespace-row-${apiNs.namespace}`)
      await expect(row).toBeVisible()
      
      const nameCell = await row.getByTestId('namespace-name').textContent()
      expect(nameCell?.trim()).toBe(apiNs.namespace)
      
      console.log(`✓ Verified namespace: ${apiNs.namespace}`)
    }
  })

  test('should display sync status alert with clear messaging', async ({ page }) => {
    if (!apiOverview.lastSyncTime) {
      console.log('No syncs have run yet, skipping test')
      return
    }

    const syncAlert = page.getByTestId('sync-status-alert')
    await expect(syncAlert).toBeVisible()

    // Verify status message based on success rate
    const alertText = await syncAlert.textContent()
    
    if (apiOverview.successRate === 100) {
      expect(alertText).toContain('All secrets synced')
      console.log('✅ Status: All secrets synced')
    } else if (apiOverview.successRate > 80) {
      expect(alertText).toContain('Partially Synced')
      console.log('⚠️ Status: Partially Synced')
    } else {
      expect(alertText).toContain('Issues detected')
      console.log('❌ Status: Issues detected')
    }

    // Verify sync operations are clearly labeled
    expect(alertText).toContain('Sync operations:')
    expect(alertText).toContain(`${apiOverview.successfulSyncs} successful`)
    expect(alertText).toContain(`${apiOverview.failedSyncs} failed`)
    
    // Verify failed secrets are separately mentioned
    const failedSecretsCount = apiNamespaces.reduce((sum, ns) => sum + ns.failedSecrets, 0)
    if (failedSecretsCount > 0) {
      expect(alertText).toContain('secrets with errors')
      console.log(`\n📊 Alert shows: ${failedSecretsCount} secrets with errors, ${apiOverview.failedSyncs} sync operations failed`)
    }
    
    console.log(`Sync Operations - ${apiOverview.successfulSyncs} successful, ${apiOverview.failedSyncs} failed`)
  })

  test('should open modal and verify Active secrets data from API', async ({ page, request }) => {
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    // Find first namespace with active secrets
    const nsWithActive = apiNamespaces.find(ns => ns.activeSecrets > 0)
    if (!nsWithActive) {
      console.log('⚠️ No namespaces with active secrets, skipping test')
      return
    }

    // Find the row for this namespace and click Active chip
    const targetRow = page.getByTestId(`namespace-row-${nsWithActive.namespace}`)
    const activeChip = targetRow.getByTestId('chip-active-secrets')
    await activeChip.click()

    // Wait for modal
    await page.waitForSelector('[role="dialog"]', { timeout: 5000 })
    await expect(page.locator(`text=/Active Secrets in ${nsWithActive.namespace}/`)).toBeVisible()

    // Fetch API data for this namespace
    const secretsResponse = await request.get(`${API_URL}/secrets/namespace/${nsWithActive.namespace}/status/Active`)
    expect(secretsResponse.ok()).toBeTruthy()
    const apiSecrets = await secretsResponse.json()

    // Verify modal shows correct count
    const modalRows = page.locator('[role="dialog"] table tbody tr')
    const modalRowCount = await modalRows.count()
    console.log(`Active secrets in ${nsWithActive.namespace} - API: ${apiSecrets.length}, Modal: ${modalRowCount}`)
    expect(modalRowCount).toBe(apiSecrets.length)

    // Close modal
    const closeButton = page.locator('[role="dialog"] button').first()
    await closeButton.click()
    await expect(page.locator('[role="dialog"]')).not.toBeVisible()
  })

  test('should open modal and verify Total Secrets data from API', async ({ page, request }) => {
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    const firstNs = apiNamespaces[0]

    // Click Total Secrets chip
    const firstRow = page.getByTestId(`namespace-row-${firstNs.namespace}`)
    const totalSecretsChip = firstRow.getByTestId('chip-total-secrets')
    await totalSecretsChip.click()

    // Wait for modal
    await page.waitForSelector('[role="dialog"]', { timeout: 5000 })
    await expect(page.locator(`text=/All Secrets in ${firstNs.namespace}/`)).toBeVisible()

    // Fetch API data for this namespace
    const secretsResponse = await request.get(`${API_URL}/secrets/namespace/${firstNs.namespace}`)
    expect(secretsResponse.ok()).toBeTruthy()
    const apiSecrets = await secretsResponse.json()

    // Verify modal shows correct count
    const modalRows = page.locator('[role="dialog"] table tbody tr')
    const modalRowCount = await modalRows.count()
    console.log(`Total secrets in ${firstNs.namespace} - API: ${apiSecrets.length}, Modal: ${modalRowCount}`)
    expect(modalRowCount).toBe(apiSecrets.length)

    // Verify first secret details match
    if (apiSecrets.length > 0) {
      const firstSecret = apiSecrets[0]
      const firstModalRow = modalRows.first()
      
      const secretName = await firstModalRow.locator('td').nth(0).textContent()
      const vaultwardenItem = await firstModalRow.locator('td').nth(1).textContent()
      const status = await firstModalRow.locator('td').nth(2).textContent()
      const dataKeys = await firstModalRow.locator('td').nth(3).textContent()

      console.log(`Verifying first secret: ${firstSecret.secretName}`)
      expect(secretName?.trim()).toBe(firstSecret.secretName)
      expect(vaultwardenItem?.trim()).toBe(firstSecret.vaultwardenItemName)
      expect(status?.trim()).toBe(firstSecret.status)
      expect(parseInt(dataKeys || '0')).toBe(firstSecret.dataKeysCount)
    }

    // Close modal
    const closeButton = page.locator('[role="dialog"] button').first()
    await closeButton.click()
  })

  test('should open modal for Failed secrets if any exist', async ({ page, request }) => {
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    // Find namespace with failed secrets
    const nsWithFailed = apiNamespaces.find(ns => ns.failedSecrets > 0)
    if (!nsWithFailed) {
      console.log('✅ No failed secrets found (good!), skipping test')
      return
    }

    // Find the row for this namespace and click Failed chip
    const targetRow = page.getByTestId(`namespace-row-${nsWithFailed.namespace}`)
    const failedChip = targetRow.getByTestId('chip-failed-secrets')
    await failedChip.click()

    // Wait for modal
    await page.waitForSelector('[role="dialog"]', { timeout: 5000 })
    await expect(page.locator(`text=/Failed Secrets in ${nsWithFailed.namespace}/`)).toBeVisible()

    // Fetch API data
    const secretsResponse = await request.get(`${API_URL}/secrets/namespace/${nsWithFailed.namespace}/status/Failed`)
    expect(secretsResponse.ok()).toBeTruthy()
    const apiSecrets = await secretsResponse.json()

    // Verify count
    const modalRows = page.locator('[role="dialog"] table tbody tr')
    const modalRowCount = await modalRows.count()
    console.log(`Failed secrets in ${nsWithFailed.namespace} - API: ${apiSecrets.length}, Modal: ${modalRowCount}`)
    expect(modalRowCount).toBe(apiSecrets.length)

    // Close modal
    const closeButton = page.locator('[role="dialog"] button').first()
    await closeButton.click()
  })

  test('should not have any console errors', async ({ page }) => {
    const errors: string[] = []
    const warnings: string[] = []
    
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        errors.push(msg.text())
      } else if (msg.type() === 'warning') {
        warnings.push(msg.text())
      }
    })

    await page.goto(DASHBOARD_URL)
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Click around to test interactions - use first namespace with active secrets
    const nsWithActive = apiNamespaces.find(ns => ns.activeSecrets > 0)
    if (nsWithActive) {
      const row = page.getByTestId(`namespace-row-${nsWithActive.namespace}`)
      const activeChip = row.getByTestId('chip-active-secrets')
      
      if (await activeChip.isVisible()) {
        await activeChip.click()
        await page.waitForTimeout(500)
        const closeButton = page.locator('[role="dialog"] button').first()
        if (await closeButton.isVisible()) {
          await closeButton.click()
        }
      }
    }

    // Log results
    if (warnings.length > 0) {
      console.log(`⚠️ ${warnings.length} console warnings found (non-critical)`)
    }
    
    if (errors.length > 0) {
      console.log('❌ Console errors found:', errors)
      expect(errors).toHaveLength(0)
    } else {
      console.log('✅ No console errors detected')
    }
  })

  test('should verify totals row matches API data', async ({ page }) => {
    await page.getByTestId('namespaces-table').waitFor({ timeout: 5000 })

    // Calculate expected totals
    const expectedTotalSecrets = apiNamespaces.reduce((sum, ns) => sum + ns.secretCount, 0)
    const expectedTotalActive = apiNamespaces.reduce((sum, ns) => sum + ns.activeSecrets, 0)
    const expectedTotalFailed = apiNamespaces.reduce((sum, ns) => sum + ns.failedSecrets, 0)
    const expectedTotalKeys = apiNamespaces.reduce((sum, ns) => sum + ns.totalDataKeys, 0)

    // Get displayed totals
    const displayedTotalSecrets = parseInt(await page.getByTestId('total-secrets').textContent() || '0')
    const displayedTotalActive = parseInt(await page.getByTestId('total-active').textContent() || '0')
    const displayedTotalFailed = parseInt(await page.getByTestId('total-failed').textContent() || '0')
    const displayedTotalKeys = parseInt(await page.getByTestId('total-data-keys').textContent() || '0')

    console.log('\n📈 Totals Row Verification:')
    console.log(`  Total Secrets - Expected: ${expectedTotalSecrets}, Displayed: ${displayedTotalSecrets}`)
    console.log(`  Total Active - Expected: ${expectedTotalActive}, Displayed: ${displayedTotalActive}`)
    console.log(`  Total Failed - Expected: ${expectedTotalFailed}, Displayed: ${displayedTotalFailed}`)
    console.log(`  Total Keys - Expected: ${expectedTotalKeys}, Displayed: ${displayedTotalKeys}`)

    expect(displayedTotalSecrets).toBe(expectedTotalSecrets)
    expect(displayedTotalActive).toBe(expectedTotalActive)
    expect(displayedTotalFailed).toBe(expectedTotalFailed)
    expect(displayedTotalKeys).toBe(expectedTotalKeys)
  })

  test('comprehensive data integrity check', async ({ page }) => {
    // This test verifies complete data flow from API to UI
    console.log('\n📋 Comprehensive Data Integrity Check')
    console.log('=====================================')
    
    // Overview stats
    console.log('\n📈 Overview Stats:')
    console.log(`  Active Secrets: ${apiOverview.activeSecrets}`)
    console.log(`  Total Syncs: ${apiOverview.totalSyncs}`)
    console.log(`  Successful Syncs: ${apiOverview.successfulSyncs}`)
    console.log(`  Failed Syncs: ${apiOverview.failedSyncs}`)
    console.log(`  Success Rate: ${apiOverview.successRate.toFixed(2)}%`)
    console.log(`  Namespaces: ${apiOverview.totalNamespaces}`)
    
    // Namespace breakdown
    console.log('\n📁 Namespace Breakdown:')
    apiNamespaces.forEach((ns, idx) => {
      console.log(`  ${idx + 1}. ${ns.namespace}:`)
      console.log(`     Total: ${ns.secretCount}, Active: ${ns.activeSecrets}, Failed: ${ns.failedSecrets}, Keys: ${ns.totalDataKeys}`)
      console.log(`     Success Rate: ${ns.successRate.toFixed(2)}%`)
    })
    
    // Verify totals add up
    const totalSecretsInNamespaces = apiNamespaces.reduce((sum, ns) => sum + ns.secretCount, 0)
    const totalActiveInNamespaces = apiNamespaces.reduce((sum, ns) => sum + ns.activeSecrets, 0)
    const totalFailedInNamespaces = apiNamespaces.reduce((sum, ns) => sum + ns.failedSecrets, 0)
    
    console.log('\n🔢 Data Integrity Checks:')
    console.log(`  ✓ Total namespaces match: ${apiNamespaces.length} === ${apiOverview.totalNamespaces}`)
    expect(apiNamespaces.length).toBe(apiOverview.totalNamespaces)
    
    console.log(`  ✓ Active secrets aggregate: ${totalActiveInNamespaces} === ${apiOverview.activeSecrets}`)
    expect(totalActiveInNamespaces).toBe(apiOverview.activeSecrets)
    
    console.log(`  ✓ All counts are non-negative`)
    expect(apiOverview.totalSyncs).toBeGreaterThanOrEqual(0)
    expect(apiOverview.activeSecrets).toBeGreaterThanOrEqual(0)
    expect(apiOverview.totalNamespaces).toBeGreaterThanOrEqual(0)
    
    console.log('\n✅ Comprehensive data integrity check passed!')
  })
})
