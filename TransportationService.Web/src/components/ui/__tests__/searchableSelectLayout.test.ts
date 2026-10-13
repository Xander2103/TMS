import { describe, expect, it } from 'vitest'
import selectCss from '../SearchableSelect.css?raw'

/**
 * SearchableSelect end-adornment zone (sprint 2026-09-21).
 *
 * Clear button and caret were two absolutely positioned glyphs with their own `right` offsets
 * and a hard-coded 56px input padding: misaligned vertically and free to drift apart. Layout is
 * not measurable in jsdom, so the stylesheet contract is guarded instead. The stylesheet is
 * listed under `test.css.include` in vitest.config.ts; without that entry `?raw` is empty.
 */
const css = selectCss.replace(/\/\*[\s\S]*?\*\//g, '')

function ruleBody(selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return css.match(new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
}

describe('SearchableSelect end-adornment zone stylesheet', () => {
  it('loads the stylesheet', () => {
    expect(css.length).toBeGreaterThan(0)
  })

  it('pins one full-height, vertically centred flex zone to the right edge', () => {
    const zone = ruleBody('.ui-searchable-select-adornments')
    expect(zone).toMatch(/position:\s*absolute/)
    expect(zone).toMatch(/top:\s*0/)
    expect(zone).toMatch(/bottom:\s*0/)
    expect(zone).toMatch(/right:\s*0/)
    expect(zone).toMatch(/display:\s*flex/)
    expect(zone).toMatch(/align-items:\s*center/)
    expect(zone).toMatch(/gap:\s*var\(--ss-gap\)/)
    expect(zone).toMatch(/padding-right:\s*var\(--ss-edge\)/)
    // Clicks on the zone open the list; only the clear button takes pointer events.
    expect(zone).toMatch(/pointer-events:\s*none/)
  })

  it('lays the adornments out in the zone instead of positioning each one', () => {
    for (const selector of ['.ui-searchable-select-clear', '.ui-searchable-select-caret']) {
      const body = ruleBody(selector)
      expect(body, `${selector} rule not found`).not.toBe('')
      expect(body).not.toMatch(/position:\s*absolute/)
      expect(body).not.toMatch(/right:/)
    }
    expect(ruleBody('.ui-searchable-select-clear')).toMatch(/pointer-events:\s*auto/)
  })

  it('gives the clear button a hit area of at least 24px', () => {
    const hit = ruleBody('.ui-searchable-select').match(/--ss-hit:\s*(\d+)px/)
    expect(Number(hit?.[1])).toBeGreaterThanOrEqual(24)
    const clear = ruleBody('.ui-searchable-select-clear')
    expect(clear).toMatch(/width:\s*var\(--ss-hit\)/)
    expect(clear).toMatch(/height:\s*var\(--ss-hit\)/)
  })

  it('derives the input padding from the zone, with and without the clear button', () => {
    expect(css).toMatch(/\.ui-searchable-select-input\s*\{[^}]*padding-right:\s*var\(--ss-zone\)/)
    expect(ruleBody('.ui-searchable-select')).toMatch(/--ss-zone:[^;]*--ss-caret/)
    const withClear = ruleBody('.ui-searchable-select.has-clear')
    expect(withClear).toMatch(/--ss-zone:[^;]*--ss-caret/)
    expect(withClear).toMatch(/--ss-zone:[^;]*--ss-hit/)
    // No hard-coded reserve left on the input.
    expect(css).not.toMatch(/padding:\s*10px\s+56px/)
  })
})
