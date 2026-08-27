import { describe, expect, it } from 'vitest'
import navCss from '../../nav.css?raw'
import sidebarCss from '../../Sidebar.css?raw'

/**
 * Sidebar-layoutinvarianten (sprint 1A/1B).
 *
 * De sidebar knipte rechts af omdat er GEEN globale `box-sizing: border-box`-reset
 * bestaat (alleen `#root`): elke navrij zette `width: 100%` én horizontale padding,
 * en werd daardoor 24px breder dan zijn container. Gevolg: overlopende actief-
 * achtergrond, afgeknipte badge en een horizontale scrollbar.
 *
 * CSS-layout is niet meetbaar in jsdom, dus bewaken we de regels zelf.
 */

/** Bodies of every rule whose selector list mentions `selector`, concatenated. */
function ruleBody(css: string, selector: string): string {
  // Comments first: an explanatory comment above a rule would otherwise be parsed
  // as part of that rule's selector list.
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const bodies: string[] = []
  for (const match of stripped.matchAll(/([^{}]+)\{([^}]*)\}/g)) {
    const selectors = match[1].split(',').map((s) => s.trim())
    if (selectors.some((s) => s === selector || s.startsWith(`${selector} `) || s.startsWith(`${selector}:`))) {
      bodies.push(match[2])
    }
  }
  return bodies.join('\n')
}

describe('sidebar layout invariants', () => {
  it('loads the stylesheets', () => {
    expect(navCss.length).toBeGreaterThan(0)
    expect(sidebarCss.length).toBeGreaterThan(0)
  })

  // 1A — rows must not grow past their container.
  it.each(['.nav-module-header', '.nav-item', '.nav-subitem-toggle'])(
    '%s uses border-box so a full-width row plus padding cannot overflow',
    (selector) => {
      const body = ruleBody(navCss, selector)
      expect(body, `${selector} rule not found`).not.toBe('')
      expect(body).toMatch(/box-sizing:\s*border-box/)
    },
  )

  // 1A — the text is the only part allowed to shrink; it must ellipsize, not spill.
  it.each(['.nav-item-label', '.nav-module-title'])('%s truncates instead of overflowing', (selector) => {
    const body = ruleBody(navCss, selector)
    expect(body, `${selector} rule not found`).not.toBe('')
    expect(body).toMatch(/min-width:\s*0/)
    expect(body).toMatch(/overflow:\s*hidden/)
    expect(body).toMatch(/text-overflow:\s*ellipsis/)
  })

  // 1A — right-hand accessories must keep their size.
  it.each(['.nav-badge', '.nav-module-dot'])('%s never shrinks away', (selector) => {
    const body = ruleBody(navCss, selector)
    expect(body, `${selector} rule not found in nav.css`).not.toBe('')
    expect(body).toMatch(/flex-shrink:\s*0/)
  })

  // The badge belongs to the sidebar, not to a feature page stylesheet that is only
  // present when that lazily-loaded chunk happens to be there.
  it('owns the nav badge in the layout stylesheet', () => {
    expect(navCss).toMatch(/\.nav-badge\s*[,{]/)
  })

  it('never shows a horizontal scrollbar and reserves the scrollbar gutter', () => {
    const body = ruleBody(sidebarCss, '.sidebar')
    expect(body).toMatch(/overflow-x:\s*hidden/)
    expect(body).toMatch(/scrollbar-gutter:\s*stable/)
  })

  // 1B — subsection labels must read as headings, clearly stronger than today.
  it('gives subsection labels heading treatment distinct from clickable rows', () => {
    const body = ruleBody(navCss, '.nav-subgroup-label')
    expect(body, '.nav-subgroup-label rule not found').not.toBe('')
    expect(body).toMatch(/text-transform:\s*uppercase/)
    // The old 0.45 opacity made them read as disabled links rather than headings.
    const opacity = body.match(/opacity:\s*([\d.]+)/)
    if (opacity) expect(Number(opacity[1])).toBeGreaterThanOrEqual(0.7)
  })
})
