#!/usr/bin/env node
// Generates the PDF manuals from the Markdown under docs/. Run from the repository root:
//   npm --prefix scripts/manuals ci && node scripts/generate-manuals.mjs
// Needs a Chromium: `npx --prefix scripts/manuals playwright-core install chromium-headless-shell`,
// or point MANUAL_CHROME at an executable. Output: docs/generated/*.pdf.
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'
import { parseArticle, validateArticles, CATEGORY_ORDER } from '../TransportationService.Web/src/features/help/manualParser.ts'

const ROOT = path.resolve(import.meta.dirname, '..')
const DOCS = path.join(ROOT, 'docs')
const require = createRequire(path.join(ROOT, 'scripts/manuals/package.json'))
const { chromium } = require('playwright-core')
const pdfjs = await import(pathToFileURL(require.resolve('pdfjs-dist/legacy/build/pdf.mjs')).href)

const read = (dir) => fs.readdirSync(path.join(DOCS, dir)).filter((f) => f.endsWith('.md')).sort((a, b) => parseInt(a) - parseInt(b))
  .map((f) => parseArticle(fs.readFileSync(path.join(DOCS, dir, f), 'utf8'), parseInt(f)))
const user = read('user-manual'), admin = read('admin-manual'), history = read('project-history')
const manifest = JSON.parse(fs.readFileSync(path.join(DOCS, 'screenshots/manifest.json'), 'utf8'))
const shots = new Set(fs.readdirSync(path.join(DOCS, 'screenshots/original')).map((f) => f.replace('.png', '')))
const problems = validateArticles([...user, ...admin], shots)
if (problems.length) { console.error(problems.join('\n')); process.exit(1) }
const rank = (c) => { const i = CATEGORY_ORDER.indexOf(c); return i < 0 ? 99 : i }
const sortArticles = (list) => [...list].sort((a, b) => rank(a.category) - rank(b.category) || a.order - b.order)

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
const imageFile = (shot) => {
  const a = path.join(DOCS, 'screenshots/annotated', `${shot}.png`)
  return pathToFileURL(fs.existsSync(a) ? a : path.join(DOCS, 'screenshots/original', `${shot}.png`)).href
}
function renderDoc(articles) {
  const known = new Set(articles.map((a) => a.id))
  let figure = 0
  const inline = (nodes) => nodes.map((n) => {
    if (n.kind === 'bold') return `<strong>${esc(n.text)}</strong>`
    if (n.kind === 'code') return `<code>${esc(n.text)}</code>`
    if (n.kind === 'link' && n.articleId) return known.has(n.articleId) ? `<a href="#a-${n.articleId}">${esc(n.text)}</a>` : esc(n.text)
    if (n.kind === 'link') return `<a href="${esc(n.href)}">${esc(n.text)}</a>`
    return esc(n.text)
  }).join('')
  const block = (b) => {
    switch (b.type) {
      case 'heading': return b.level === 2 ? `<h3>${esc(b.text)}</h3>` : `<h4>${esc(b.text)}</h4>`
      case 'paragraph': return `<p>${inline(b.inline)}</p>`
      case 'list': { const t = b.ordered ? 'ol' : 'ul'; return `<${t}${b.ordered ? ` start="${b.start}"` : ''}>${b.items.map((i) => `<li>${inline(i)}</li>`).join('')}</${t}>` }
      case 'table': return `<table><thead><tr>${b.header.map((c) => `<th>${inline(c)}</th>`).join('')}</tr></thead><tbody>${b.rows.map((r) => `<tr>${r.map((c) => `<td>${inline(c)}</td>`).join('')}</tr>`).join('')}</tbody></table>`
      case 'image': {
        figure++
        const mobile = manifest[b.shot]?.viewport?.startsWith('390')
        return `<figure class="${mobile ? 'mobile' : ''}"><img src="${imageFile(b.shot)}" alt="${esc(b.alt)}"><figcaption>Afbeelding ${figure}: ${esc(b.caption)}</figcaption></figure>`
      }
      case 'code': return `<pre>${esc(b.text)}</pre>`
    }
  }
  const chapters = []
  for (const a of articles) {
    let c = chapters.find((x) => x.name === a.category)
    if (!c) chapters.push(c = { name: a.category, id: `c-${chapters.length}`, articles: [] })
    c.articles.push(a)
  }
  const body = chapters.map((c, i) => `<section class="chapter"><h1 id="${c.id}"><span class="num">${i + 1}</span> ${esc(c.name)}</h1>${c.articles.map((a) => `<article>${selfTitled(c) ? '' : `<h2 id="a-${a.id}">${esc(a.title)}</h2>`}${a.blocks.map(block).join('\n')}</article>`).join('\n')}</section>`).join('\n')
  return { chapters, body }
}
const selfTitled = (c) => c.articles.length === 1 && c.articles[0].title === c.name
const CSS = `
  @page { size: A4; }
  html { font-family: -apple-system, "Helvetica Neue", Arial, sans-serif; font-size: 10.5pt; line-height: 1.45; color: #1a1a22; }
  body { margin: 0; }
  a { color: #6d1fb8; text-decoration: none; }
  .cover { height: 245mm; display: flex; flex-direction: column; justify-content: center; page-break-after: always; }
  .cover .brand { color: #8a2be2; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; font-size: 12pt; }
  .cover h1 { font-size: 32pt; line-height: 1.1; margin: 8mm 0 6mm; border: 0; padding: 0; page-break-before: auto; }
  .cover p { font-size: 13pt; color: #555; margin: 0 0 3mm; max-width: 140mm; }
  .toc { page-break-after: always; }
  .toc h1 { page-break-before: auto; }
  .toc ul { list-style: none; margin: 0; padding: 0; }
  .toc li { display: flex; align-items: baseline; gap: 2mm; }
  .toc li span.dots { flex: 1; border-bottom: .6pt dotted #aaa; transform: translateY(-2pt); }
  .toc li.chap { font-weight: 700; margin-top: 2.6mm; font-size: 11pt; }
  .toc li.art { padding-left: 6mm; font-size: 10pt; }
  .toc a { color: inherit; }
  h1 { font-size: 22pt; color: #3d1466; margin: 0 0 6mm; padding-bottom: 2mm; border-bottom: 2pt solid #8a2be2; page-break-before: always; }
  .chapter:first-of-type h1 { page-break-before: auto; }
  h1 .num { color: #8a2be2; margin-right: 2mm; }
  h2 { font-size: 15pt; color: #1a1a22; margin: 8mm 0 2mm; page-break-after: avoid; }
  article:first-of-type h2 { margin-top: 2mm; }
  h3 { font-size: 10pt; letter-spacing: .03em; color: #6d1fb8; margin: 4.5mm 0 1.2mm; page-break-after: avoid; }
  h4 { font-size: 11pt; margin: 3mm 0 1mm; page-break-after: avoid; }
  p, ol, ul { margin: 0 0 2mm; }
  ol, ul { padding-left: 6mm; }
  li { margin-bottom: .6mm; }
  code, pre { font-family: Menlo, Consolas, monospace; font-size: 9pt; background: #f4f3ec; border-radius: 2pt; }
  code { padding: 0 1mm; }
  pre { padding: 2mm 3mm; white-space: pre-wrap; }
  table { border-collapse: collapse; width: 100%; font-size: 9.5pt; margin: 2mm 0 3mm; page-break-inside: auto; }
  thead { display: table-header-group; }
  tr { page-break-inside: avoid; }
  th, td { border: .5pt solid #bbb; padding: 1.2mm 1.8mm; text-align: left; vertical-align: top; }
  th { background: #f1ebf8; }
  figure { margin: 3mm 0 4mm; page-break-inside: avoid; text-align: center; }
  figure img { width: 158mm; max-width: 100%; max-height: 165mm; object-fit: contain; border: .5pt solid #bbb; }
  figure.mobile img { width: 72mm; }
  figcaption { font-size: 9pt; color: #555; margin-top: 1mm; }
`
const footer = (title) => `<div style="width:100%;font-size:8pt;color:#666;padding:0 18mm;display:flex;justify-content:space-between;font-family:Arial,sans-serif"><span>${esc(title)}</span><span>Pagina <span class="pageNumber"></span> van <span class="totalPages"></span></span></div>`

const browser = await chromium.launch({ executablePath: process.env.MANUAL_CHROME || undefined })
const page = await (await browser.newContext({ locale: 'nl-BE' })).newPage()
async function makePdf(file, { title, subtitle, intro, articles }) {
  const { chapters, body } = renderDoc(articles)
  const html = (pages) => {
    const num = (id) => (pages ? `<span>${pages[id] ?? ''}</span>` : '<span>000</span>')
    const toc = chapters.map((c) => `<li class="chap"><a href="#${c.id}">${esc(c.name)}</a><span class="dots"></span>${num(c.id)}</li>${!selfTitled(c) ? c.articles.map((a) => `<li class="art"><a href="#a-${a.id}">${esc(a.title)}</a><span class="dots"></span>${num(`a-${a.id}`)}</li>`).join('') : ''}`).join('')
    return `<!doctype html><html lang="nl"><head><meta charset="utf-8"><title>${esc(title)}</title><style>${CSS}</style></head><body><div class="cover"><div class="brand">TransportationService TMS</div><h1>${esc(title)}</h1><p>${esc(subtitle)}</p><p>${esc(intro)}</p></div><nav class="toc"><h1>Inhoud</h1><ul>${toc}</ul></nav>${body}</body></html>`
  }
  const tmp = path.join(DOCS, 'generated', `.tmp-${file}.html`)
  const print = async (markup) => {
    fs.writeFileSync(tmp, markup)
    await page.goto(pathToFileURL(tmp).href, { waitUntil: 'load' })
    await page.evaluate(async () => { await Promise.all([...document.images].map((i) => i.decode().catch(() => {}))) })
    return page.pdf({ format: 'A4', printBackground: true, displayHeaderFooter: true, headerTemplate: '<span></span>', footerTemplate: footer(title), margin: { top: '18mm', bottom: '18mm', left: '18mm', right: '18mm' }, outline: true, tagged: true })
  }
  // Two passes: the first PDF tells on which page each heading landed, the second prints those numbers.
  const first = await print(html(null))
  const doc = await pdfjs.getDocument({ data: new Uint8Array(first), isEvalSupported: false, verbosity: 0 }).promise
  const dests = await doc.getDestinations()
  const pages = {}
  for (const [id, dest] of Object.entries(dests)) { if (Array.isArray(dest)) pages[id] = (await doc.getPageIndex(dest[0])) + 1 }
  const missing = chapters.flatMap((c) => [c.id, ...(selfTitled(c) ? [] : c.articles.map((a) => `a-${a.id}`))]).filter((id) => !pages[id])
  if (missing.length) throw new Error(`${file}: no page found for ${missing.join(', ')}`)
  const pdf = await print(html(pages))
  fs.rmSync(tmp)
  const out = path.join(DOCS, 'generated', file)
  fs.writeFileSync(out, pdf)
  const final = await pdfjs.getDocument({ data: new Uint8Array(pdf), isEvalSupported: false, verbosity: 0 }).promise
  console.log(`${file}: ${final.numPages} pages, ${(pdf.length / 1048576).toFixed(1)} MB`)
}
fs.mkdirSync(path.join(DOCS, 'generated'), { recursive: true })
const date = new Date().toLocaleDateString('nl-BE', { day: 'numeric', month: 'long', year: 'numeric' })
const manualPart = sortArticles(user)
await makePdf('TMS-Complete-Gebruikershandleiding.pdf', { title: 'Complete gebruikershandleiding', subtitle: 'Stap voor stap, met schermafbeeldingen, voor iedereen die met het systeem werkt.', intro: `Versie van ${date}.`, articles: manualPart })
await makePdf('TMS-Klantportaal-Handleiding.pdf', { title: 'Handleiding klantportaal', subtitle: 'Voor klanten die het portaal gebruiken.', intro: `Versie van ${date}.`, articles: manualPart.filter((a) => a.roles.includes('customer') || a.id === 'inloggen' || a.id === 'weergave' || a.id === 'geen-toegang') })
await makePdf('TMS-Beheerdershandleiding.pdf', { title: 'Beheerdershandleiding', subtitle: 'Voor de beheerder en de eigenaar van het systeem.', intro: `Versie van ${date}. Dit document bevat geen wachtwoorden of geheime sleutels.`, articles: admin })
await makePdf('TMS-Projectoverzicht-En-Wijzigingshistoriek.pdf', { title: 'Projectoverzicht en wijzigingshistoriek', subtitle: 'Wat er tijdens het herstelproject is gebeurd, waarom, en hoe het getest is.', intro: `Versie van ${date}. Voor toekomstige ontwikkelaars en de eigenaar van het bedrijf.`, articles: [...history].sort((a, b) => a.order - b.order).map((a) => ({ ...a, category: `${a.title}` })) })
await browser.close()
