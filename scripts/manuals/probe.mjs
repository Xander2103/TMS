// usage: node probe.mjs role url [url...]: prints what is on a page (development help for shots.mjs)
import { newBrowser, openPage, login, APP, sleep, ids, sql } from './lib.mjs'
const [role, ...urls] = process.argv.slice(2)
const b = await newBrowser(); const { page } = await openPage(b); await login(page, role)
for (const u of urls) {
  await page.goto(APP + u); await page.waitForLoadState('networkidle', { timeout: 6000 }).catch(() => {}); await sleep(500)
  const r = await page.evaluate(() => {
    const root = document.querySelector('.app-main, main, #root')
    const ctl = [...root.querySelectorAll('button,[role=tab],a[href],h1,h2,h3,label')].filter((x) => x.offsetParent).map((x) => `${x.tagName[0]}${x.getAttribute('role') === 'tab' ? 'T' : ''}:${x.textContent.trim().slice(0, 40)}`)
    return { hasMain: !!document.querySelector('main'), root: root.tagName + '.' + root.className, ctl: ctl.join(' | ').slice(0, 1500) }
  })
  console.log('==', u, JSON.stringify(r))
}
await b.close()
