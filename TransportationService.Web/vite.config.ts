import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ command, mode }) => {
  // CFG-11: a production bundle must never silently fall back to the dev API on localhost. The
  // API base is baked in at build time, so a production build without VITE_API_BASE_URL (env or
  // .env.production) fails here instead of shipping a SPA that calls http://localhost:5019.
  if (command === 'build' && mode === 'production') {
    const env = loadEnv(mode, process.cwd(), 'VITE_')
    const apiBase = (process.env.VITE_API_BASE_URL ?? env.VITE_API_BASE_URL ?? '').trim()
    if (!apiBase) {
      throw new Error(
        'VITE_API_BASE_URL is not set for this production build. Set it to the public API base URL '
          + '(e.g. VITE_API_BASE_URL=https://tms.example.be) in the environment or in .env.production.',
      )
    }
  }

  return {
    plugins: [react()],
  }
})
