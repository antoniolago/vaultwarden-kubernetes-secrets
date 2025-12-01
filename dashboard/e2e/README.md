# E2E Testing Guide

## Battle-Tested Dashboard Verification

This E2E test suite provides comprehensive verification that all data displayed in the dashboard matches the API responses exactly. No assumptions - every number, every count, every table row is validated against the source of truth (the API).

## Test Coverage

### 1. API Data Fetching
- Tests fetch data directly from API endpoints before checking UI
- Establishes baseline truth from backend
- Compares UI display against this baseline

### 2. Dashboard Stats Verification
✅ **Active Secrets Count** - Exact match between API and stat card  
✅ **Total Syncs Count** - Exact match between API and stat card  
✅ **Namespaces Count** - Exact match between API and stat card  
✅ **Success Rate** - Validates percentage calculation and display  

### 3. Namespace Table Data Integrity
✅ **Row Count** - Table has exact number of rows as API namespaces  
✅ **Namespace Names** - All namespace names match API  
✅ **Total Secrets** - Each row's total matches API  
✅ **Active Secrets** - Each row's active count matches API  
✅ **Failed Secrets** - Each row's failed count matches API  
✅ **Data Keys** - Each row's data keys count matches API  

### 4. Quick Stats Chips
✅ **Successful Syncs** - Chip displays correct count from API  
✅ **Failed Syncs** - Chip displays correct count from API  
✅ **Namespaces** - Chip displays correct count from API  
✅ **Active Secrets** - Chip displays correct count from API  

### 5. Modal Functionality & Data
✅ **Active Secrets Modal** - Clicking Active chip opens modal with correct data  
✅ **Failed Secrets Modal** - Clicking Failed chip opens modal (if failures exist)  
✅ **Total Secrets Modal** - Clicking Total chip opens modal with all secrets  
✅ **Modal Row Count** - Modal table rows match API secret count  
✅ **Secret Details** - Individual secret fields match API (name, status, keys, etc.)  

### 6. Console Error Detection
✅ **No Console Errors** - Test fails if browser console shows any errors  
✅ **Interactive Testing** - Tests user interactions (clicking, opening modals)  

### 7. Comprehensive Data Integrity
✅ **Cross-Validation** - Aggregated namespace data matches overview totals  
✅ **Data Flow** - Validates complete chain: Database → API → Frontend  

## Running Tests

### Prerequisites

```bash
# Using Bun (recommended)
cd dashboard
bun install
bunx playwright install chromium

# Or using npm
npm install
npx playwright install chromium
```

### Run All Tests

```bash
# From dashboard directory
bun run test:e2e

# Or with npm
npm run test:e2e
```

### Debug Mode

```bash
# Step through tests interactively
bun run test:e2e:debug

# Run with visible browser
bun run test:e2e:headed

# Interactive UI mode
bun run test:e2e:ui
```

### Run Specific Tests

```bash
# Run only specific test file
bunx playwright test dashboard.spec.ts

# Run specific test by name
bunx playwright test -g "should display correct Active Secrets"

# Run in headed mode for specific test
bunx playwright test -g "modal" --headed
```

## Test Requirements

### Services Must Be Running

Before running tests, ensure these services are active:

1. **Sync Service** - Must have run at least once to populate database
2. **API Service** - Must be running on http://localhost:8080
3. **Dashboard** - Must be running on http://localhost:3000

Quick start:
```bash
# Terminal 1: API
cd VaultwardenK8sSync.Api
dotnet run

# Terminal 2: Dashboard  
cd dashboard
bun run dev

# Terminal 3: Run tests
cd dashboard
bun run test:e2e
```

Or use the automated script:
```bash
bash scripts/e2e-test.sh
```

## Understanding Test Output

### Successful Test
```
✓ should display correct Active Secrets count from API
  Active Secrets - API: 8, Dashboard: 8
```

### Failed Test
```
✗ should display correct Active Secrets count from API
  Active Secrets - API: 8, Dashboard: 0
  Error: expect(received).toBe(expected)
  Expected: 8
  Received: 0
```

### Test Summary
```
📋 Comprehensive Data Integrity Check
=====================================

📊 Overview Stats:
  Active Secrets: 8
  Total Syncs: 3
  Successful: 3
  Failed: 0
  Success Rate: 100.00%
  Namespaces: 5

📁 Namespace Breakdown:
  1. default:
     Total: 5, Active: 5, Failed: 0, Keys: 15
     Success Rate: 100.00%
  ...

✅ Comprehensive data integrity check passed!
```

## Common Issues

### Test Fails: "API is not running"
**Solution**: Start the API service first
```bash
cd VaultwardenK8sSync.Api
dotnet run
```

### Test Fails: "Dashboard not accessible"
**Solution**: Start the dashboard dev server
```bash
cd dashboard
bun run dev
```

### Test Fails: All counts are zero
**Solution**: Run the sync service to populate database
```bash
cd VaultwardenK8sSync
dotnet run sync
```

### Test Fails: Modal tests
**Solution**: Ensure you have active/failed secrets in database. Check:
```bash
curl http://localhost:8080/api/dashboard/namespaces | jq
```

### Browser crashes or hangs
**Solution**: Reinstall Chromium
```bash
bunx playwright install --force chromium
```

## Extending Tests

### Add New Test

```typescript
test('should verify new feature', async ({ page, request }) => {
  // 1. Fetch API data
  const apiResponse = await request.get(`${API_URL}/endpoint`)
  const apiData = await apiResponse.json()

  // 2. Get UI data
  const uiElement = page.locator('selector')
  const uiData = await uiElement.textContent()

  // 3. Compare
  expect(uiData).toBe(apiData.expectedField)
})
```

### Test API Endpoint Directly

```typescript
test('should verify API endpoint', async ({ request }) => {
  const response = await request.get(`${API_URL}/dashboard/overview`)
  expect(response.ok()).toBeTruthy()
  
  const data = await response.json()
  expect(data.activeSecrets).toBeGreaterThan(0)
})
```

## CI/CD Integration

### GitHub Actions Example

```yaml
- name: Install dependencies
  run: |
    cd dashboard
    bun install
    bunx playwright install chromium

- name: Start services
  run: |
    # Start API and dashboard in background
    # Wait for services to be ready

- name: Run E2E tests
  run: |
    cd dashboard
    bun run test:e2e
```

## Performance Notes

- Tests run in parallel where possible
- Average test suite runtime: ~30-45 seconds
- Individual test timeout: 30 seconds
- Can run headless for CI/CD or headed for debugging

## Best Practices

1. **Always fetch API data first** - Establish baseline truth
2. **Use explicit waits** - Wait for elements to be visible before interacting
3. **Validate exact matches** - Don't use "contains" or approximate checks
4. **Test user interactions** - Click, type, navigate as users would
5. **Check console for errors** - Real users will see these issues
6. **Test edge cases** - Empty states, zero counts, no failed secrets
7. **Keep tests isolated** - Each test should be independent

## Troubleshooting Checklist

- [ ] API service running on port 8080?
- [ ] Dashboard running on port 3000?
- [ ] Database populated with sync data?
- [ ] Chromium browser installed?
- [ ] No port conflicts?
- [ ] Check /tmp/playwright.log for detailed errors
- [ ] Try running single test to isolate issue
- [ ] Try headed mode to see what's happening

## Support

For issues or questions:
1. Check `/tmp/playwright.log` for detailed error messages
2. Run with `--headed` flag to see browser interactions
3. Use `--debug` flag to step through tests
4. Review API responses: `curl http://localhost:8080/api/dashboard/overview | jq`
