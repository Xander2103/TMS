import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    // Layout-invariantentests lezen deze stylesheets als ?raw; zonder deze include
    // stubt Vitest CSS-imports naar een lege string.
    css: { include: [/nav\.css/, /Sidebar\.css/, /customers\.css/] },
  },
})
