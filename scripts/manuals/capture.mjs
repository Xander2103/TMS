// Captures real screenshots from a running demo stack, then draws the callouts on a copy.
// Usage: node capture.mjs [--only=id,id] [--group=name]
// The positions come from the live DOM, so an arrow cannot point at something that is not there:
// a target that is missing or off-screen makes that shot fail, loudly.
import fs from 'node:fs'
import path from 'node:path'
import { newBrowser, openPage, login, APP, sleep } from './lib.mjs'
import { annotationHtml } from './annotate.mjs'
import { groups } from './shots.mjs'

const ROOT = path.resolve(import.meta.dirname, '../../docs/screenshots')
const ORIG = path.join(ROOT, 'original'), ANN = path.join(ROOT, 'annotated')
fs.mkdirSync(ORIG, { recursive: true }); fs.mkdirSync(ANN, { recursive: true })
const arg = (k) => process.argv.find((a) => a.startsWith(`--${k}=`))?.split('=')[1]
const only = arg('only')?.split(','), onlyGroup = arg('group')
const MANIFEST = path.join(ROOT, 'manifest.json')
const manifest = fs.existsSync(MANIFEST) ? JSON.parse(fs.readFileSync(MANIFEST, 'utf8')) : {}
// Wording that must never appear again in a manual screenshot.
const FORBIDDEN = [/Dossier bevestigen/, /Planning Center/, /Boekhoudsnapshot/]
const browser = await newBrowser()
const results = { ok: [], failed: [] }

async function settle(page) { await page.waitForLoadState('networkidle', { timeout: 6000 }).catch(() => {}); await sleep(600) }

async function capture(page, annPage, shot, group) {
  if (shot.size) await page.setViewportSize(shot.size)
  if (shot.go) { await page.goto(APP + (typeof shot.go === 'function' ? shot.go() : shot.go)); await settle(page) }
  if (shot.before) { await shot.before(page); await settle(page) }
  const vp = page.viewportSize()
  const notes = shot.notes ? shot.notes(page) : []
  for (const [loc] of notes) { await loc.first().waitFor({ state: 'visible', timeout: 6000 }) }
  if (notes[0]) await notes[0][0].first().scrollIntoViewIfNeeded().catch(() => {})
  await sleep(250)
  const targets = []
  for (const [loc, label, opts = {}] of notes) {
    // Headings and text: box the words, not the full-width block they sit in.
    const b = await loc.first().evaluate((el) => {
      const whole = el.getBoundingClientRect()
      if (['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON', 'A', 'TABLE', 'UL', 'LI', 'FIELDSET'].includes(el.tagName) || el.getAttribute('role') === 'group') return { x: whole.x, y: whole.y, width: whole.width, height: whole.height }
      const r = document.createRange(); r.selectNodeContents(el); const t = r.getBoundingClientRect()
      const u = t.width > 0 && t.width < whole.width ? t : whole
      return { x: u.x, y: u.y, width: u.width, height: u.height }
    })
    if (!b) throw new Error(`no box for callout "${label}"`)
    if (b.y + b.height <= 0 || b.y >= vp.height || b.x >= vp.width) throw new Error(`callout "${label}" is off-screen ${JSON.stringify(b)}`)
    const box = { x: Math.max(0, b.x), y: Math.max(0, b.y), width: Math.min(b.width, vp.width - Math.max(0, b.x)), height: Math.min(b.height, vp.height - Math.max(0, b.y)) }
    targets.push({ box, label, side: opts.side, noBox: opts.noBox, corner: opts.corner })
  }
  const text = await page.locator('body').innerText()
  for (const re of FORBIDDEN) if (re.test(text)) throw new Error(`forbidden wording on screen: ${re}`)
  if (shot.noOutput) return
  const file = `${shot.id}.png`
  const png = await page.screenshot({ path: path.join(ORIG, file) })
  if (targets.length) {
    const mobile = vp.width < 700
    await annPage.setViewportSize({ width: vp.width, height: vp.height })
    await annPage.setContent(annotationHtml({ imageDataUrl: 'data:image/png;base64,' + png.toString('base64'), width: vp.width, height: vp.height, targets, mobile }), { waitUntil: 'load' })
    await annPage.locator('#canvas').screenshot({ path: path.join(ANN, file) })
  } else fs.rmSync(path.join(ANN, file), { force: true })
  manifest[shot.id] = { group, url: new URL(page.url()).pathname, viewport: `${vp.width}x${vp.height}`, callouts: targets.map((t) => t.label) }
}

for (const g of groups) {
  if (onlyGroup && g.name !== onlyGroup) continue
  const shots = only ? g.shots.filter((s) => only.includes(s.id)) : g.shots
  if (!shots.length) continue
  const { context, page } = await openPage(browser, g.viewport ?? {})
  const { context: annCtx, page: annPage } = await openPage(browser, g.viewport ?? {})
  try { if (g.role) await login(page, g.role) } catch (e) { results.failed.push([g.name, e.message]); console.log('LOGIN FAIL', g.name, e.message); continue }
  for (const shot of shots) {
    try { await capture(page, annPage, shot, g.name); results.ok.push(shot.id); console.log('ok  ', shot.id) }
    catch (e) {
      results.failed.push([shot.id, e.message.split('\n')[0]])
      const btns = await page.evaluate(() => [...document.querySelectorAll('button,[role=tab],a[href]')].filter((x) => x.offsetParent).map((x) => x.textContent.trim().slice(0, 28)).filter(Boolean).slice(0, 60).join(' | ')).catch(() => '')
      console.log('FAIL', shot.id, e.message.split('\n')[0].slice(0, 200), '\n     url', page.url().replace(APP, ''), '\n     seen:', btns.slice(0, 700))
      await page.screenshot({ path: path.join(process.env.TMPDIR ?? '/tmp', `fail-${shot.id}.png`) }).catch(() => {})
      await page.keyboard.press('Escape').catch(() => {})
    }
    if (shot.size) await page.setViewportSize(g.viewport ? { width: g.viewport.width, height: g.viewport.height } : { width: 1440, height: 900 })
  }
  await context.close(); await annCtx.close(); await sleep(g.role ? 6500 : 0)
}
await browser.close()
fs.writeFileSync(MANIFEST, JSON.stringify(Object.fromEntries(Object.entries(manifest).sort()), null, 1) + '\n')
console.log(`--- ${results.ok.length} ok, ${results.failed.length} failed`)
for (const [id, m] of results.failed) console.log('  ', id, m.slice(0, 160))
process.exitCode = results.failed.length ? 1 : 0
