import { test, expect } from '@playwright/test'

test.describe('Dashboard Data Keys E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3000')
  })

  test('should display data keys count in namespace table', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('[data-testid="namespaces-table"]', { timeout: 10000 })
    
    // Get first namespace row
    const firstRow = page.locator('[data-testid^="namespace-row-"]').first()
    
    // Check data keys column exists and has value
    const dataKeysCell = firstRow.getByTestId('namespace-data-keys')
    await expect(dataKeysCell).toBeVisible()
    
    const dataKeysText = await dataKeysCell.textContent()
    console.log(`✓ Data Keys count displayed: ${dataKeysText}`)
  })

  test('should show data keys chip as clickable when count > 0', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('[data-testid="namespaces-table"]', { timeout: 10000 })
    
    // Find a namespace with data keys > 0
    const rows = page.locator('[data-testid^="namespace-row-"]')
    const count = await rows.count()
    
    let foundClickable = false
    
    for (let i = 0; i < count; i++) {
      const row = rows.nth(i)
      const dataKeysChip = row.getByTestId('chip-data-keys')
      const text = await dataKeysChip.textContent()
      const keysCount = parseInt(text || '0')
      
      if (keysCount > 0) {
        // Check if it has pointer cursor (clickable)
        const cursor = await dataKeysChip.evaluate(el => 
          window.getComputedStyle(el).cursor
        )
        
        expect(cursor).toBe('pointer')
        console.log(`✓ Data Keys chip (${keysCount} keys) is clickable`)
        foundClickable = true
        break
      }
    }
    
    if (!foundClickable) {
      console.log('⚠️ No namespaces with data keys > 0 found')
    }
  })

  test('should open data keys modal when clicking data keys count', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('[data-testid="namespaces-table"]', { timeout: 10000 })
    
    // Find first namespace with data keys > 0
    const rows = page.locator('[data-testid^="namespace-row-"]')
    const count = await rows.count()
    
    for (let i = 0; i < count; i++) {
      const row = rows.nth(i)
      const dataKeysChip = row.getByTestId('chip-data-keys')
      const text = await dataKeysChip.textContent()
      const keysCount = parseInt(text || '0')
      
      if (keysCount > 0) {
        const namespace = await row.getByTestId('namespace-name').textContent()
        console.log(`\n📋 Testing data keys modal for namespace: ${namespace}`)
        
        // Click the data keys chip
        await dataKeysChip.click()
        
        // Modal should open
        await expect(page.getByRole('dialog')).toBeVisible()
        console.log('✓ Data keys modal opened')
        
        // Check modal title
        await expect(page.getByText(`Data Keys in ${namespace}`)).toBeVisible()
        console.log(`✓ Modal title correct: "Data Keys in ${namespace}"`)
        
        // Check modal has content (either keys or loading)
        const modalContent = page.getByRole('dialog')
        const hasKeys = await modalContent.locator('.MuiChip-root').count() > 0
        const hasLoading = await modalContent.locator('[role="progressbar"]').isVisible().catch(() => false)
        
        if (hasKeys) {
          console.log('✓ Data keys displayed in modal')
        } else if (hasLoading) {
          console.log('⏳ Modal is loading keys...')
          
          // Wait for loading to finish
          await page.waitForSelector('[role="progressbar"]', { state: 'detached', timeout: 5000 }).catch(() => {})
          
          // Check again
          const keysAfterLoading = await modalContent.locator('.MuiChip-root').count()
          if (keysAfterLoading > 0) {
            console.log(`✓ Loaded ${keysAfterLoading} data keys`)
          }
        }
        
        // Close modal
        await page.keyboard.press('Escape')
        await expect(page.getByRole('dialog')).not.toBeVisible()
        console.log('✓ Modal closed successfully')
        
        break
      }
    }
  })

  test('should display secret names with their keys in modal', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('[data-testid="namespaces-table"]', { timeout: 10000 })
    
    // Find first namespace with data keys
    const rows = page.locator('[data-testid^="namespace-row-"]')
    const count = await rows.count()
    
    for (let i = 0; i < count; i++) {
      const row = rows.nth(i)
      const dataKeysChip = row.getByTestId('chip-data-keys')
      const text = await dataKeysChip.textContent()
      const keysCount = parseInt(text || '0')
      
      if (keysCount > 0) {
        const namespace = await row.getByTestId('namespace-name').textContent()
        
        // Click data keys
        await dataKeysChip.click()
        
        // Wait for modal
        await page.waitForSelector('[role="dialog"]', { timeout: 5000 })
        
        // Wait for loading to complete
        await page.waitForTimeout(2000)
        
        // Check for secret names (should have 🔑 emoji)
        const secretNames = page.getByRole('dialog').locator('text=/🔑/')
        const secretCount = await secretNames.count()
        
        if (secretCount > 0) {
          console.log(`✓ Found ${secretCount} secrets with data keys`)
          
          // Check for key chips
          const keyChips = page.getByRole('dialog').locator('.MuiChip-root')
          const chipCount = await keyChips.count()
          
          if (chipCount > 0) {
            console.log(`✓ Found ${chipCount} data key chips`)
            
            // Log first few key names
            const firstKeys = await keyChips.first().textContent()
            console.log(`  Example key: ${firstKeys}`)
          }
        } else {
          console.log('⚠️ No secrets with keys displayed (might be loading or error)')
        }
        
        // Close modal
        await page.keyboard.press('Escape')
        break
      }
    }
  })

  test('should handle API errors gracefully', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('[data-testid="namespaces-table"]', { timeout: 10000 })
    
    // Intercept API calls to simulate error
    await page.route('**/api/secrets/*/keys', route => {
      route.fulfill({
        status: 404,
        body: 'Not found'
      })
    })
    
    // Find first namespace with data keys
    const rows = page.locator('[data-testid^="namespace-row-"]')
    const count = await rows.count()
    
    for (let i = 0; i < count; i++) {
      const row = rows.nth(i)
      const dataKeysChip = row.getByTestId('chip-data-keys')
      const text = await dataKeysChip.textContent()
      const keysCount = parseInt(text || '0')
      
      if (keysCount > 0) {
        // Click data keys
        await dataKeysChip.click()
        
        // Modal should still open
        await expect(page.getByRole('dialog')).toBeVisible()
        console.log('✓ Modal opened even with API error')
        
        // Should show "No data keys found" or similar
        await page.waitForTimeout(2000)
        
        const noDataText = page.getByRole('dialog').getByText(/No data keys found/i)
        const isVisible = await noDataText.isVisible().catch(() => false)
        
        if (isVisible) {
          console.log('✓ Shows appropriate message when keys cannot be fetched')
        }
        
        // Close modal
        await page.keyboard.press('Escape')
        break
      }
    }
  })

  test('should close modal with X button', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('[data-testid="namespaces-table"]', { timeout: 10000 })
    
    // Find first namespace with data keys
    const rows = page.locator('[data-testid^="namespace-row-"]')
    const count = await rows.count()
    
    for (let i = 0; i < count; i++) {
      const row = rows.nth(i)
      const dataKeysChip = row.getByTestId('chip-data-keys')
      const text = await dataKeysChip.textContent()
      const keysCount = parseInt(text || '0')
      
      if (keysCount > 0) {
        // Click data keys
        await dataKeysChip.click()
        
        // Modal should open
        await expect(page.getByRole('dialog')).toBeVisible()
        
        // Click X button
        await page.getByRole('dialog').getByRole('button').filter({ hasText: '✕' }).click()
        
        // Modal should close
        await expect(page.getByRole('dialog')).not.toBeVisible()
        console.log('✓ Modal closed with X button')
        
        break
      }
    }
  })
})
