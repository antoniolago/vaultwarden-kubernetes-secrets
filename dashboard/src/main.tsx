import React from 'react'
import ReactDOM from 'react-dom/client'
import { CssVarsProvider, extendTheme } from '@mui/joy/styles'
import { CssBaseline } from '@mui/joy'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App.tsx'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
      staleTime: 30000,
    },
  },
})

// Deep Ocean Blue Theme - Following Joy UI best practices
const deepOceanTheme = extendTheme({
  colorSchemes: {
    dark: {
      palette: {
        background: {
          body: '#0a1628',
          surface: '#0f1f3a',
          level1: '#132844',
          level2: '#1a3351',
          level3: '#223e5e',
        },
        primary: {
          50: '#e0f2fe',
          100: '#bae6fd',
          200: '#7dd3fc',
          300: '#38bdf8',
          400: '#0ea5e9',
          500: '#0284c7',
          600: '#0369a1',
          700: '#075985',
          800: '#0c4a6e',
          900: '#0a3a56',
          solidBg: 'var(--joy-palette-primary-300)',
          solidColor: '#0a1628',
          solidHoverBg: 'var(--joy-palette-primary-200)',
          solidActiveBg: 'var(--joy-palette-primary-400)',
          outlinedBorder: 'var(--joy-palette-primary-300)',
          outlinedColor: 'var(--joy-palette-primary-200)',
          outlinedHoverBg: 'rgba(56, 189, 248, 0.1)',
          softBg: 'rgba(56, 189, 248, 0.15)',
          softColor: 'var(--joy-palette-primary-200)',
          softHoverBg: 'rgba(56, 189, 248, 0.2)',
          plainColor: 'var(--joy-palette-primary-200)',
          plainHoverBg: 'rgba(56, 189, 248, 0.1)',
        },
        neutral: {
          50: '#f8fafc',
          100: '#f1f5f9',
          200: '#e2e8f0',
          300: '#cbd5e1',
          400: '#94a3b8',
          500: '#64748b',
          600: '#475569',
          700: '#334155',
          800: '#1e293b',
          900: '#0f172a',
          outlinedBorder: 'rgba(148, 163, 184, 0.3)',
          plainColor: 'var(--joy-palette-neutral-200)',
          plainHoverBg: 'rgba(148, 163, 184, 0.1)',
        },
        success: {
          50: '#d1fae5',
          100: '#a7f3d0',
          200: '#6ee7b7',
          300: '#34d399',
          400: '#10b981',
          500: '#059669',
          600: '#047857',
          700: '#065f46',
          800: '#064e3b',
          900: '#022c22',
          solidBg: 'var(--joy-palette-success-300)',
          solidColor: '#022c22',
        },
        warning: {
          50: '#fef3c7',
          100: '#fde68a',
          200: '#fcd34d',
          300: '#fbbf24',
          400: '#f59e0b',
          500: '#d97706',
          600: '#b45309',
          700: '#92400e',
          800: '#78350f',
          900: '#451a03',
          solidBg: 'var(--joy-palette-warning-200)',
          solidColor: '#451a03',
        },
        danger: {
          50: '#fee2e2',
          100: '#fecaca',
          200: '#fca5a5',
          300: '#f87171',
          400: '#ef4444',
          500: '#dc2626',
          600: '#b91c1c',
          700: '#991b1b',
          800: '#7f1d1d',
          900: '#450a0a',
          solidBg: 'var(--joy-palette-danger-200)',
          solidColor: '#450a0a',
        },
        text: {
          // primary: 'var(--joy-palette-neutral-200)',
          // secondary: 'var(--joy-palette-neutral-400)',
          // tertiary: 'var(--joy-palette-neutral-500)',
        },
        divider: 'rgba(56, 189, 248, 0.2)',
      },
    }
  }
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <CssVarsProvider theme={deepOceanTheme} defaultMode="dark" disableTransitionOnChange>
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <App />
      </QueryClientProvider>
    </CssVarsProvider>
  </React.StrictMode>,
)
