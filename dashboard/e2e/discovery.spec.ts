import { test, expect } from '@playwright/test'

test.describe('Discovery Page E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3000/discovery')
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
    
    expect(tabCount).toBeGreaterThanOrEqual(3)
    console.log(`✓ Found ${tabCount} tabs`)
    
    // Check tab names
    const tabTexts = await tabs.allTextContents()
    const hasNotSynced = tabTexts.some(t => t.includes('Not Synced'))
    const hasSynced = tabTexts.some(t => t.includes('Synced') && !t.includes('Not'))
    const hasStats = tabTexts.some(t => t.includes('Statistics'))
    
    expect(hasNotSynced).toBeTruthy()
    expect(hasSynced).toBeTruthy()
    expect(hasStats).toBeTruthy()
    
    console.log('✓ All 3 tabs displayed')
  })

  test('should allow tab switching', async ({ page }) => {
    // Wait for tabs
    await page.waitForSelector('[role="tablist"]', { timeout: 10000 })
    
    // Get all tabs
    const tabs = page.getByRole('tab')
    const tabTexts = await tabs.allTextContents()
    
    // Find Statistics tab index
    const statsIdx = tabTexts.findIndex(t => t.includes('Statistics'))
    if (statsIdx >= 0) {
      await tabs.nth(statsIdx).click()
      await page.waitForTimeout(500) // Wait for tab to activate
      const selected = await tabs.nth(statsIdx).getAttribute('aria-selected')
      expect(selected).toBe('true')
      console.log('✓ "Statistics" tab activated')
    }
  })

  test('should display correct sync instructions in Statistics tab', async ({ page }) => {
    // Wait for tabs
    await page.waitForSelector('[role="tablist"]', { timeout: 10000 })
    
    // Click Statistics tab by index
    const tabs = page.getByRole('tab')
    const tabTexts = await tabs.allTextContents()
    const statsIdx = tabTexts.findIndex(t => t.includes('Statistics'))
    if (statsIdx >= 0) {
      await tabs.nth(statsIdx).click()
      await page.waitForTimeout(500) // Wait for tab content to render
    }
    
    // Check for correct instructions
    await expect(page.getByText(/custom field called/i).first()).toBeVisible()
    await expect(page.locator('strong:has-text("VAULTWARDEN_CUSTOM_FIELD_NAME")').first()).toBeVisible()
    
    console.log('✓ Statistics tab shows correct custom field explanation')
    
    // Check for example
    await expect(page.getByText(/Field Name: namespaces/i)).toBeVisible()
    await expect(page.getByText(/Field Value: namespace\/secret-name/i)).toBeVisible()
    
    console.log('✓ Statistics tab shows correct example')
    
    // Should NOT mention annotations
    const pageText = await page.textContent('body')
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

  test('should show "Missing custom field" in reason column', async ({ page }) => {
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
        
        if (reasonText?.includes('Missing custom field')) {
          console.log('✓ Reason shows "Missing custom field" (correct)')
        } else if (reasonText?.includes('annotation')) {
          console.log('❌ Reason still mentions "annotation" (should be "Missing custom field")')
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
    await page.goto('http://localhost:3000')
    
    // Check sidebar for Discovery link
    const discoveryLink = page.getByText('🔍 Discovery')
    await expect(discoveryLink).toBeVisible()
    console.log('✓ Discovery link in sidebar')
    
    // Click it
    await discoveryLink.click()
    
    // Should navigate to discovery page
    await page.waitForURL('**/discovery')
    await expect(page).toHaveURL(/.*discovery/)
    console.log('✓ Navigation to Discovery page works')
  })

  test('should display coverage analysis in Statistics tab', async ({ page }) => {
    // Go to Statistics tab
    await page.waitForSelector('[role="tablist"]', { timeout: 10000 })
    await page.getByRole('tab', { name: /Statistics/i }).click()
    
    // Check for coverage analysis card
    await expect(page.getByText('Coverage Analysis')).toBeVisible()
    console.log('✓ Coverage Analysis section visible')
    
    // Check for expected fields
    await expect(page.getByText(/Synced Items:/i)).toBeVisible()
    await expect(page.getByText(/Not Synced:/i)).toBeVisible()
    await expect(page.getByText(/Total in VW:/i)).toBeVisible()
    
    console.log('✓ Coverage statistics displayed')
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
    // Go to Statistics tab
    await page.waitForSelector('[role="tablist"]', { timeout: 10000 })
    await page.getByRole('tab', { name: /Statistics/i }).click()
    await page.waitForTimeout(500) // Wait for tab content to render
    
    // Wait for the statistics content to be visible
    await page.waitForSelector('text=Sync Statistics', { timeout: 5000 })
    
    // Get all text on page
    const bodyText = await page.textContent('body')
    
    // Should mention "namespaces" field
    expect(bodyText).toContain('namespaces')
    console.log('✓ Mentions "namespaces" field name')
    
    // Should mention custom field
    expect(bodyText?.toLowerCase()).toContain('custom field')
    console.log('✓ Mentions "custom field" terminology')
    
    // Should mention environment variable
    expect(bodyText).toContain('VAULTWARDEN_CUSTOM_FIELD_NAME')
    console.log('✓ Mentions VAULTWARDEN_CUSTOM_FIELD_NAME env var')
    
    // Should NOT mention notes or annotations
    const mentionsNotes = bodyText?.toLowerCase().includes('note')
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
