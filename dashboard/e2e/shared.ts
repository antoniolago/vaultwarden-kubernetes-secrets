import { test as base } from '@playwright/test'

// API URL - use env var or default to localhost:8080
export const API_URL = process.env.API_URL || 'http://localhost:8080/api'
// Dashboard URL - use env var or default to localhost:3000
export const DASHBOARD_URL = process.env.DASHBOARD_URL || 'http://localhost:3000'
