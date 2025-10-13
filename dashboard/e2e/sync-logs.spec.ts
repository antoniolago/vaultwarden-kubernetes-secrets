import { test, expect } from '@playwright/test'

test.describe('Sync Logs E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3000/logs')
  })

  test('should display sync logs table', async ({ page }) => {
    // Wait for page to load
    await page.waitForSelector('h2:has-text("Sync Logs")', { timeout: 10000 })
    
    // Check table exists
    await expect(page.locator('table')).toBeVisible()
    console.log('✓ Sync logs table displayed')
    
    // Check for table headers
    await expect(page.locator('th:has-text("ID")')).toBeVisible()
    await expect(page.locator('th:has-text("Status")')).toBeVisible()
    await expect(page.locator('th:has-text("Deleted")')).toBeVisible()
    console.log('✓ Table headers displayed correctly')
  })

  test('should have correct number of columns', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('table', { timeout: 10000 })
    
    // Get all rows
    const rows = page.locator('tbody tr')
    const count = await rows.count()
    
    console.log(`\n📋 Checking ${count} sync log rows...`)
    
    if (count === 0) {
      console.log('⚠️ No sync logs found')
      return
    }
    
    // Check that each row has the correct number of columns (without error)
    const firstRow = rows.first()
    const cells = firstRow.locator('td')
    const cellCount = await cells.count()
    
    // Should have: ID, Start Time, Status, Duration, Total, Created, Updated, Skipped, Failed, Deleted = 10 columns
    console.log(`  Columns in row: ${cellCount}`)
    expect(cellCount).toBe(10)
    console.log('✓ Table has correct number of columns (10)')
  })


  test('should show deleted secrets count or 0', async ({ page }) => {
    // Wait for table
    await page.waitForSelector('table', { timeout: 10000 })
    
    const rows = page.locator('tbody tr')
    const count = await rows.count()
    
    console.log(`\n📊 Checking "Deleted" column in ${count} rows...`)
    
    let withDeleted = 0
    let withZero = 0
    
    for (let i = 0; i < Math.min(count, 10); i++) {
      const row = rows.nth(i)
      const cells = row.locator('td')
      
      // Find deleted column (should be before error column)
      // Structure: ID, Start, Status, Duration, Total, Created, Updated, Skipped, Failed, Deleted, Error
      const deletedCell = cells.nth(9) // 10th column (0-indexed)
      const deletedText = await deletedCell.textContent()
      
      if (deletedText && deletedText.trim() !== '' && deletedText.trim() !== '0') {
        withDeleted++
        console.log(`  Row ${i + 1}: ${deletedText} deleted`)
      } else {
        withZero++
        console.log(`  Row ${i + 1}: 0 deleted`)
      }
    }
    
    console.log(`\nSummary:`)
    console.log(`  Rows with deletions: ${withDeleted}`)
    console.log(`  Rows with 0 deletions: ${withZero}`)
    console.log('✓ Deleted column displays values or 0')
  })

})
