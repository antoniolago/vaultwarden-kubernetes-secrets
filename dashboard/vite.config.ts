import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig(({ mode }) => {
  // Load env from repo root (parent directory)
  const env = loadEnv(mode, path.resolve(__dirname, '..'), '')
  
  return {
    plugins: [react()],
    server: {
      port: 3000,
      host: true
    },
    build: {
      outDir: 'dist',
      sourcemap: false
    },
    define: {
      // Expose LOGINLESS_MODE from root .env as VITE_LOGINLESS_MODE
      'import.meta.env.VITE_LOGINLESS_MODE': JSON.stringify(env.LOGINLESS_MODE || 'false'),
      'import.meta.env.VITE_API_URL': JSON.stringify(
        mode === 'production' ? '/api' : 'http://localhost:8080/api'
      )
    }
  }
})
