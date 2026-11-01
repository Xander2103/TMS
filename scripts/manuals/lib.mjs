// Helpers for the demo stack behind the manual screenshots. Everything runs against test data.
// Settings come from the environment (see docs/technical/manuals.md); no credentials live in the repository.
import fs from 'node:fs'
import { execSync } from 'node:child_process'
import { chromium } from 'playwright-core'

const need = (k) => { const v = process.env[k]; if (!v) throw new Error(`${k} is not set`); return v }
export const APP = process.env.MANUAL_APP ?? 'http://localhost:5199'
export const API = process.env.MANUAL_API ?? 'http://localhost:5080'
export const U = {
  admin: 'beheerder@noorderkempen.example.test', adminB: 'beheerder@tenantb.example.test',
  hr: 'hr@noorderkempen.example.test', management: 'management@noorderkempen.example.test', planner: 'planner@noorderkempen.example.test',
  dispatcher: 'dispatcher@noorderkempen.example.test', fleet: 'fleet@noorderkempen.example.test', boekhouding: 'boekhouding@noorderkempen.example.test',
  magazijn: 'magazijn@noorderkempen.example.test', chauffeur: 'chauffeur@noorderkempen.example.test', chauffeurb: 'chauffeurb@noorderkempen.example.test',
  klantA: 'klantA@klant.example.test', klantB: 'klantB@klant.example.test',
}
const pwOf = (email) => (email.startsWith('beheerder@') ? need('MANUAL_ADMIN_PASSWORD') : need('MANUAL_DOMAIN_PASSWORD'))
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
export const ids = JSON.parse(fs.readFileSync(need('MANUAL_IDS'), 'utf8'))
export const saveIds = () => fs.writeFileSync(need('MANUAL_IDS'), JSON.stringify(ids, null, 1))
const tokens = new Map()
export async function token(who) {
  const email = U[who] ?? who; const c = tokens.get(email)
  if (c && Date.now() - c.at < 8 * 60000) return c.t
  for (let i = 0; i < 6; i++) {
    const r = await fetch(API + '/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password: pwOf(email) }) })
    const j = await r.json().catch(() => ({}))
    if (j.accessToken) { tokens.set(email, { t: j.accessToken, at: Date.now() }); return j.accessToken }
    if (r.status === 429) { await sleep(65000); continue }
    throw new Error(`login ${email} → ${r.status}`)
  }
  throw new Error('login retries exhausted ' + email)
}
export async function api(method, path, body, who = 'admin') {
  const r = await fetch(API + path, { method, headers: { 'content-type': 'application/json', authorization: 'Bearer ' + await token(who) }, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await r.text(); let json = null; try { json = JSON.parse(text) } catch { /* not json */ }
  return { status: r.status, json, text }
}
export async function ok(method, path, body, who = 'admin') {
  const r = await api(method, path, body, who)
  if (r.status >= 300) throw new Error(`${method} ${path} as ${who} → ${r.status} ${r.text.slice(0, 500)}`)
  return r.json
}
export const sql = (s) => execSync(need('MANUAL_PSQL'), { input: s, encoding: 'utf8', shell: '/bin/bash' }).trim()
export const day = (d = 0) => new Date(Date.now() + d * 86400000).toLocaleDateString('sv-SE', { timeZone: 'Europe/Brussels' })
export async function newBrowser() { return chromium.launch({ executablePath: process.env.MANUAL_CHROME || undefined, args: ['--lang=nl-BE'] }) }
export async function openPage(browser, { width = 1440, height = 900, scale = 1, colorScheme = 'light' } = {}) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: scale, locale: 'nl-BE', timezoneId: 'Europe/Brussels', colorScheme })
  const page = await context.newPage()
  page.errors = []
  page.on('pageerror', (e) => page.errors.push(e.message.slice(0, 200)))
  return { context, page }
}
export async function login(page, who) {
  const email = U[who] ?? who
  for (let i = 0; i < 5; i++) {
    await page.goto(APP + '/login')
    await page.getByLabel(/e-?mail/i).fill(email)
    await page.getByLabel(/wachtwoord/i).first().fill(pwOf(email))
    await page.getByRole('button', { name: /aanmelden|inloggen/i }).click()
    try { await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 12000 }); await sleep(1500); return }
    catch { await sleep(65000) }
  }
  throw new Error('login failed ' + who)
}
