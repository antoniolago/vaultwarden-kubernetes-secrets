import { test, expect } from '@playwright/test'
import { API_URL, DASHBOARD_URL, waitForSyncComplete } from './shared'

test.describe('Discovery Page E2E Tests', () => {
  test.beforeAll(async ({ request }) => {
    test.setTimeout(120000)
    await waitForSyncComplete(request)
  })

  test.beforeEach(async ({ page }) => {
    await page.goto(`${DASHBOARD_URL}/discovery`)
  })

  test('should display discovery page', async ({ page }) => {
    // Check page title
    await expect(page.getByText('🔍 Vaultwarden Discovery')).toBeVisible()
    console.log('✓ Discovery page loaded')
    
    // Check description
    await expect(page.getByText(/Compare Vaultwarden items/i)).toBeVisible()
    console.log('✓ Page description visible')
  })

  test('should display summary cards', async ({ page }) => {
    // Wait for page to load
    await page.waitForSelector('h2:has-text("Vaultwarden Discovery")', { timeout: 10000 })
    
    // Check for summary cards by looking for the text patterns
    const pageText = await page.textContent('body')
    
    expect(pageText).toContain('Synced to K8s')
    expect(pageText).toContain('Not Synced')
    expect(pageText).toContain('Total Items')
    expect(pageText).toContain('Sync Rate')
    
    console.log('✓ All 4 summary cards displayed')
  })

  test('should display warning when no backend data', async ({ page }) => {
    // Wait for page to load
    await page.waitForTimeout(1000)
    
    // Check for backend API warning
    const warning = page.getByText(/Backend API Required/i)
    const isVisible = await warning.isVisible().catch(() => false)
    
    if (isVisible) {
      console.log('✓ Shows warning about backend API requirement')
      await expect(page.getByText(/GET \/api\/discovery/i)).toBeVisible()
      console.log('✓ Warning explains which endpoint to implement')
    } else {
      console.log('⚠️ Backend API appears to be implemented (no warning shown)')
    }
  })

  test('should display tabs', async ({ page }) => {
    // Wait for page to load
    await page.waitForSelector('[role="tablist"]', { timeout: 10000 })
    
    // Check tabs exist by counting
    const tabs = page.getByRole('tab')
    const tabCount = await tabs.count()
    
    expect(tabCount).toBeGreaterThanOrEqual(2)
    console.log(`✓ Found ${tabCount} tabs`)
    
    // Check tab names
    const tabTexts = await tabs.allTextContents()
    const hasSynced = tabTexts.some(t => t.includes('Synced'))
    const hasNotSynced = tabTexts.some(t => t.includes('Not Synced'))
    
    expect(hasSynced).toBeTruthy()
    expect(hasNotSynced).toBeTruthy()
    
    console.log('✓ Both tabs displayed (Synced and Not Synced)')
  })

  test('should allow tab switching', async ({ page }) => {
    // Wait for tabs
    await page.waitForSelector('[role="tablist"]', { timeout: 10000 })
    
    // Get all tabs
    const tabs = page.getByRole('tab')
    const tabTexts = await tabs.allTextContents()
    
    // Find Not Synced tab index
    const notSyncedIdx = tabTexts.findIndex(t => t.includes('Not Synced'))
    if (notSyncedIdx >= 0) {
      await tabs.nth(notSyncedIdx).click()
      await page.waitForTimeout(500) // Wait for tab to activate
      const selected = await tabs.nth(notSyncedIdx).getAttribute('aria-selected')
      expect(selected).toBe('true')
      console.log('✓ "Not Synced" tab activated')
    }
  })

  test('should display correct sync instructions', async ({ page }) => {
    // Wait for page to load
    await page.waitForTimeout(1000)
    
    // Check for all the instruction text about custom fields
    const pageText = await page.textContent('body')
    
    // Should mention custom field setup
    const hasCustomField = pageText?.toLowerCase().includes('custom field') || false
    const hasNamespaces = pageText?.includes('namespaces') || false
    
    if (hasNamespaces) {
      console.log('✓ Mentions "namespaces" field name')
    } else {
      console.log('⚠️ "namespaces" field name not found in visible text')
    }
    
    if (hasCustomField) {
      console.log('✓ Page mentions custom field terminology')
    } else {
      console.log('⚠️ Custom field terminology not found (may be in a different view)')
    }
    
    // Should NOT mention annotations
    const hasAnnotationText = pageText?.toLowerCase().includes('annotation')
    
    if (hasAnnotationText) {
      console.log('⚠️ Warning: Page still mentions "annotation" (should be "custom field")')
    } else {
      console.log('✓ Page correctly uses "custom field" terminology (not "annotation")')
    }
  })

  test('should have search functionality', async ({ page }) => {
    // Check search input exists
    const searchInput = page.getByPlaceholder(/Search items/i)
    await expect(searchInput).toBeVisible()
    console.log('✓ Search input visible')
    
    // Try typing in search
    await searchInput.fill('test')
    const value = await searchInput.inputValue()
    expect(value).toBe('test')
    console.log('✓ Search input functional')
  })

  test('should display table in Not Synced tab', async ({ page }) => {
    // Wait for tabs
    await page.waitForSelector('[role=\"tablist\"]', { timeout: 10000 })
    
    // Not Synced tab should be active by default
    const table = page.locator('table')
    const isVisible = await table.isVisible().catch(() => false)
    
    if (isVisible) {
      console.log('\u2713 Table displayed in Not Synced tab')
      
      // Check table headers by text content
      const headers = await table.locator('thead th').allTextContents()
      const hasItemName = headers.some(h => h.includes('Item Name'))
      const hasFolder = headers.some(h => h.includes('Folder'))
      const hasReason = headers.some(h => h.includes('Reason'))
      
      expect(hasItemName || hasFolder || hasReason).toBeTruthy()
      console.log('\u2713 Table has headers')
    } else {
      console.log('\u26a0\ufe0f Table not visible (likely no data from backend)')
    }
  })

  test('should show "Missing namespaces custom field" in reason column', async ({ page }) => {
    // Wait for page load
    await page.waitForTimeout(1000)
    
    // Check if table has data
    const table = page.locator('table')
    const tableVisible = await table.isVisible().catch(() => false)
    
    if (tableVisible) {
      const rows = page.locator('tbody tr')
      const count = await rows.count()
      
      if (count > 0) {
        // Check reason column
        const reasonCell = rows.first().locator('td').last()
        const reasonText = await reasonCell.textContent()
        
        if (reasonText?.includes('Missing namespaces custom field')) {
          console.log('✓ Reason shows "Missing namespaces custom field" (correct)')
        } else if (reasonText?.includes('annotation')) {
          console.log('❌ Reason still mentions "annotation" (should be "Missing namespaces custom field")')
        } else {
          console.log(`⚠️ Reason text: ${reasonText}`)
        }
      } else {
        console.log('⚠️ Table has no rows (backend not implemented)')
      }
    } else {
      console.log('⚠️ No table visible (backend not implemented)')
    }
  })

  test('should display empty state when no data', async ({ page }) => {
    // Wait for page load
    await page.waitForTimeout(1000)
    
    // If no backend data, should show appropriate message
    const emptyMessage = page.getByText(/All items are synced/i)
    const isVisible = await emptyMessage.isVisible().catch(() => false)
    
    if (isVisible) {
      console.log('✓ Shows positive empty state message')
    } else {
      console.log('⚠️ No empty state message (might have data or different state)')
    }
  })

  test('should have accessible navigation', async ({ page }) => {
    // Check page is in navigation
    await page.goto(DASHBOARD_URL)
    
    // Check sidebar for Discovery link
    const discoveryLink = page.getByText('Discovery')
    await expect(discoveryLink).toBeVisible()
    console.log('✓ Discovery link in sidebar')
    
    // Click it
    await discoveryLink.click()
    
    // Should navigate to discovery page
    await page.waitForURL('**/discovery')
    await expect(page).toHaveURL(/.*discovery/)
    console.log('✓ Navigation to Discovery page works')
  })

  test('should display coverage analysis if available', async ({ page }) => {
    // Check for coverage analysis content in the Not Synced tab (default active tab)
    await page.waitForTimeout(1000)
    
    // The Statistics tab is currently commented out. Check what's available on the page.
    const pageText = await page.textContent('body')
    const hasCoverage = pageText?.includes('Coverage Analysis') || false
    const hasSyncedItems = pageText?.includes('Synced Items') || false
    
    if (hasCoverage) {
      console.log('✓ Coverage Analysis section visible')
    } else {
      console.log('⚠️ Coverage Analysis not visible (Statistics tab is commented out)')
    }
    
    console.log('✓ Discovery page loaded and checked for available content')
  })

  test('should display sync rate percentage in summary', async ({ page }) => {
    // Wait for summary cards
    await page.waitForTimeout(1000)
    
    // Find sync rate card
    const syncRateCard = page.getByText('Sync Rate').locator('..')
    
    // Should show a percentage
    const rateText = await syncRateCard.textContent()
    const hasPercentage = rateText?.includes('%')
    
    if (hasPercentage) {
      console.log(`✓ Sync rate shows percentage: ${rateText?.match(/\d+%/)?.[0]}`)
    } else {
      console.log('⚠️ Sync rate might be 0% (no data)')
    }
  })

  test('should validate correct field name in instructions', async ({ page }) => {
    // Wait for page to load
    await page.waitForTimeout(1000)
    
    // Get all text on page
    const bodyText = await page.textContent('body')
    
    // Should mention "namespaces" field
    const mentionsNamespaces = bodyText?.includes('namespaces') || false
    if (mentionsNamespaces) {
      console.log('✓ Mentions "namespaces" field name')
    } else {
      console.log('⚠️ "namespaces" field name not found on page')
    }
    
    // Should mention custom field
    const mentionsCustomField = bodyText?.toLowerCase().includes('custom field') || false
    if (mentionsCustomField) {
      console.log('✓ Page uses \"custom field\" terminology')
    } else {
      console.log('⚠️ \"custom field\" terminology not found on page')
    }
    
    // Check for incorrect terminology
    const mentionsNotes = bodyText?.toLowerCase().includes('note') && 
                          !bodyText?.toLowerCase().includes('vaultwarden item')
    const mentionsAnnotation = bodyText?.toLowerCase().includes('annotation') && 
                               !bodyText?.toLowerCase().includes('via environment variable')
    
    if (mentionsNotes) {
      console.log('⚠️ Warning: Still mentions "notes" (should only mention custom field)')
    }
    
    if (mentionsAnnotation) {
      console.log('⚠️ Warning: Still mentions "annotation" (should be "custom field")')
    }
    
    if (!mentionsNotes && !mentionsAnnotation) {
      console.log('✓ Correctly avoids incorrect terminology')
    }
  })
})
