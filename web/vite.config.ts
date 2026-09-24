/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  return {
    plugins: [react(), tailwindcss()],
    server: {
      // Same-origin in development: the API returns relative image URLs (/api/images/...).
      proxy: {
        '/api': { target: env.VITE_API_PROXY_TARGET || 'http://localhost:5080' },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      // jsdom + MUI + a virtualized grid is CPU-heavy per file; too many parallel workers on a
      // modest machine starve individual tests past their timeout (seen as flaky findBy failures).
      poolOptions: { threads: { maxThreads: 4 } },
      testTimeout: 10_000,
    },
  }
})
