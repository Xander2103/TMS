import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  // `npm test` is a bare `vitest run`, and Vitest prefers this file over vite.config.ts, so
  // everything below is what CI actually runs — no CLI flag required or allowed to differ.
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Wave 1 fix B (B8). The suite spends ~5 s per file just building the jsdom environment,
    // so on a loaded runner the default 5 s timeout fails userEvent/waitFor tests at random —
    // reviewers were all told to pass --testTimeout=30000, i.e. CI ran stricter than anything
    // anyone validated against. 15 s is headroom, not a licence for slow tests.
    testTimeout: 15000,
    hookTimeout: 15000,
    // Guard, not a fix: the suite is proven zone-independent (green under Europe/Brussels, UTC
    // and Asia/Tokyo). Pinning the zone keeps a future zone-dependent test from passing on the
    // author's machine and failing in CI — and makes the per-file `process.env.TZ` overrides in
    // the time-zone suites explicit deviations from a known baseline instead of from "whatever
    // this runner happens to be".
    env: { TZ: 'Europe/Brussels' },
    include: ['src/**/*.test.{ts,tsx}'],
    // Layout-invariantentests lezen deze stylesheets als ?raw; zonder deze include
    // stubt Vitest CSS-imports naar een lege string.
    css: { include: [/nav\.css/, /Sidebar\.css/, /customers\.css/] },
  },
})
