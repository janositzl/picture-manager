/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  // Sub-path hosting behind a reverse proxy (VITE_BASE_PATH=/pm → "/pm/"); unset means the site root.
  const trimmedBase = (env.VITE_BASE_PATH ?? '').trim().replace(/^\/+|\/+$/g, '')
  const base = trimmedBase === '' ? '/' : `/${trimmedBase}/`
  return {
    base,
    plugins: [react(), tailwindcss()],
    server: {
      // Same-origin in development: the API returns relative image URLs (/api/images/...).
      // Under a base path the prefix is forwarded as-is; run the API with PathBase set to match.
      proxy: {
        [`${base}api`]: { target: env.VITE_API_PROXY_TARGET || 'http://localhost:5080' },
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
