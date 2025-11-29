import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig(({ mode }) => {
  // Load env from repo root (parent directory)
  const env = loadEnv(mode, path.resolve(__dirname, '..'), '')
  
  // Check if using mock data (for GitHub Pages demo)
  const useMockData = env.VITE_USE_MOCK_DATA === 'true'
  
  return {
    plugins: [react()],
    publicDir: 'public',
    server: {
      port: 3000,
      host: true
    },
    build: {
      outDir: 'dist',
      sourcemap: false,
      assetsInlineLimit: 0,
      rollupOptions: {
        output: {
          assetFileNames: (assetInfo) => {
            // Keep vks.png without hash
            if (assetInfo.name === 'vks.png') {
              return 'vks.png'
            }
            return 'assets/[name]-[hash][extname]'
          }
        }
      }
    },
    define: {
      // Expose LOGINLESS_MODE from root .env as VITE_LOGINLESS_MODE
      'import.meta.env.VITE_LOGINLESS_MODE': JSON.stringify(env.LOGINLESS_MODE || env.VITE_LOGINLESS_MODE || 'false'),
      'import.meta.env.VITE_API_URL': JSON.stringify(
        mode === 'production' ? '/api' : 'http://localhost:8080/api'
      ),
      // Enable mock data mode for GitHub Pages demo
      'import.meta.env.VITE_USE_MOCK_DATA': JSON.stringify(useMockData ? 'true' : 'false')
    }
  }
})
