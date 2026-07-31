// Release gate around `npm audit`: high/critical advisories in production dependencies fail
// the build, EXCEPT advisories on the explicit allowlist below. The allowlist exists for the
// rare case where an advisory has no fixed version published on the registry AND provably does
// not apply to this application; every entry must carry its rationale and must be re-evaluated
// whenever the affected dependency is upgraded. An allowlisted advisory that gains a published
// fix should be removed from this list (the fix upgrade closes it properly).
import { execSync } from 'node:child_process'

const ALLOWLIST = new Map([
  [
    'GHSA-qwww-vcr4-c8h2',
    'react-router RSC Mode CSRF: only exploitable with React Server Components/server-action ' +
      'mode. This app is a pure Vite SPA on BrowserRouter (no SSR, no RSC endpoints), so the ' +
      'vulnerable code path is unreachable. No fixed version is published on the registry ' +
      '(advisory names 8.3.0; latest is 7.18.2, and the suggested downgrade 7.11.0 reintroduces ' +
      'five applicable open-redirect/XSS/DoS advisories). Re-evaluate on every router upgrade.',
  ],
])

// Constant command string, no interpolated input — execSync's shell is required here anyway
// because npm is npm.cmd on Windows.
let raw
try {
  raw = execSync('npm audit --omit=dev --json', { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
} catch (error) {
  // npm audit exits non-zero when it finds anything; the JSON report is still on stdout.
  raw = error.stdout
  if (!raw) {
    console.error('npm audit produced no output:', error.message)
    process.exit(1)
  }
}

const report = JSON.parse(raw)
const advisoryId = (url) => (typeof url === 'string' ? url.split('/').pop() : null)

const failures = []
const allowed = []
for (const [name, vuln] of Object.entries(report.vulnerabilities ?? {})) {
  if (vuln.severity !== 'high' && vuln.severity !== 'critical') continue
  // Direct advisories live on this node's `via` as objects; string entries just point at the
  // vulnerable dependency and are judged on that dependency's own node.
  const advisories = (vuln.via ?? []).filter((v) => typeof v === 'object')
  if (advisories.length === 0) continue
  for (const adv of advisories) {
    const id = advisoryId(adv.url)
    if (id && ALLOWLIST.has(id)) {
      allowed.push(`${name}: ${adv.title} (${id})`)
    } else {
      failures.push(`${name} [${adv.severity}]: ${adv.title} (${adv.url})`)
    }
  }
}

for (const entry of allowed) {
  console.log(`ALLOWLISTED (documented, re-evaluate on upgrade): ${entry}`)
}

if (failures.length > 0) {
  console.error('npm audit gate FAILED — high/critical advisories without an allowlist entry:')
  for (const failure of failures) console.error(`  - ${failure}`)
  process.exit(1)
}

console.log('npm audit gate passed.')
