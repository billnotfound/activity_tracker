// Vite configuration for Vue 3 SPA.
//
// Development (pnpm dev):
//   - Dev server on port 5000
//   - /api/* requests are proxied to the backend on port 5200
//   - HMR enabled for instant component reload
//
// Production (pnpm build):
//   - Output goes to wwwroot/ (served by the .NET Web project)
//   - Empty outDir ensures clean builds (stale files removed)
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5000,
    proxy: {
      // Proxy API calls to the .NET backend during development.
      // In production, the API_BASE is set to the backend host directly
      // (see src/main.js for the logic).
      '/api': 'http://localhost:5200'
    }
  },
  build: {
    outDir: 'wwwroot',      // Matches .NET's default web root
    emptyOutDir: true       // Remove old hashed assets on rebuild
  }
})
