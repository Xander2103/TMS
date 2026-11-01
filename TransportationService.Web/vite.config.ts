import fs from 'node:fs'
import path from 'node:path'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'

const DOCS = path.resolve(__dirname, '../docs')
// Not "/help": a folder of that name in dist/ would make nginx answer the /help route with a directory.
const ASSET_ROOT = '/manual-assets'
const PDF_FILES = ['TMS-Complete-Gebruikershandleiding.pdf', 'TMS-Beheerdershandleiding.pdf', 'TMS-Klantportaal-Handleiding.pdf']

/** docs/ is the one source of the manuals: its screenshots and PDFs are served from there and copied into the build. */
function manualAssets(): Plugin {
  let outDir = 'dist'
  const imageFile = (name: string) => {
    const annotated = path.join(DOCS, 'screenshots/annotated', name)
    return fs.existsSync(annotated) ? annotated : path.join(DOCS, 'screenshots/original', name)
  }
  return {
    name: 'manual-assets',
    configResolved(config) { outDir = path.resolve(config.root, config.build.outDir) },
    configureServer(server) {
      server.middlewares.use(ASSET_ROOT, (req, res, next) => {
        const name = path.basename((req.url ?? '').split('?')[0])
        const file = (req.url ?? '').startsWith('/img/') ? imageFile(name) : path.join(DOCS, 'generated', name)
        if (!fs.existsSync(file)) return next()
        res.setHeader('Content-Type', file.endsWith('.pdf') ? 'application/pdf' : 'image/png')
        fs.createReadStream(file).pipe(res)
      })
    },
    closeBundle() {
      const target = path.join(outDir, ASSET_ROOT)
      fs.mkdirSync(path.join(target, 'img'), { recursive: true })
      const originals = path.join(DOCS, 'screenshots/original')
      if (fs.existsSync(originals)) for (const name of fs.readdirSync(originals)) fs.copyFileSync(imageFile(name), path.join(target, 'img', name))
      for (const name of PDF_FILES) {
        const file = path.join(DOCS, 'generated', name)
        if (fs.existsSync(file)) fs.copyFileSync(file, path.join(target, name))
      }
    },
  }
}

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
    plugins: [react(), manualAssets()],
    server: { fs: { allow: [path.resolve(__dirname, '..')] } },
  }
})
