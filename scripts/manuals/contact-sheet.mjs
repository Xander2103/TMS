// node contact-sheet.mjs out.png id id id ...: side-by-side overview for reviewing screenshots.
import fs from 'node:fs'; import path from 'node:path'
import { newBrowser, openPage } from './lib.mjs'
const [out, cols, ...names] = process.argv.slice(2)
const root = path.resolve(import.meta.dirname, '../../docs/screenshots')
const b = await newBrowser(); const { page } = await openPage(b, { width: 1800, height: 1000 })
const cell = (n) => { const a = path.join(root, 'annotated', n + '.png'); const p = fs.existsSync(a) ? a : path.join(root, 'original', n + '.png'); return `<figure><figcaption>${n}</figcaption><img src="data:image/png;base64,${fs.readFileSync(p).toString('base64')}"></figure>` }
await page.setContent(`<style>body{margin:6px;font:14px sans-serif;display:grid;grid-template-columns:repeat(${cols},1fr);gap:6px}figure{margin:0}img{width:100%;border:1px solid #888}</style>${names.map(cell).join('')}`, { waitUntil: 'load' })
await page.screenshot({ path: out, fullPage: true }); await b.close()
