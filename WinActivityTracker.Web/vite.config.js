// Vite configuration for Vue 3 SPA.
//
// Development (pnpm dev):
//   - Dev server on port 5000 with HMR
//   - /api/* proxied to the backend; set VITE_API_PORT env var if not 32579.
//     e.g.: VITE_API_PORT=54431 pnpm dev
//
// Production (pnpm build):
//   - Output → wwwroot/ (copied into Service publish dir)
//   - API calls go to same origin (apiBase = '')
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

const apiPort = process.env.VITE_API_PORT || '32579'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5000,
    proxy: {
      '/api': `http://localhost:${apiPort}`
    }
  },
  build: {
    outDir: 'wwwroot',
    emptyOutDir: true
  }
})
