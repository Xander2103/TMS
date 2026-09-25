import { describe, expect, it } from 'vitest'
import routeCss from '../sections/route-section.css?raw'
import autocompleteCss from '../../../locations/components/AddressAutocompleteInput.css?raw'
import routeSectionSource from '../sections/RouteSection.tsx?raw'

/**
 * Laden | Lossen side by side + the Van | Tot pair (master sprint 2026-09-21, spec §3/§4).
 *
 * jsdom cannot measure layout, so the stylesheet CONTRACT is guarded instead. Both stylesheets are
 * listed under `test.css.include` in vitest.config.ts; without that entry `?raw` is empty.
 */
const strip = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')
const css = strip(routeCss)
const popoverCss = strip(autocompleteCss)
const [desktop, mobile = ''] = css.split(/@media\s*\(max-width:\s*900px\)/)

function ruleBody(source: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return source.match(new RegExp(`(?:^|\\}|\\{)\\s*${escaped}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
}

describe('route-section.css — stop cards', () => {
  it('loads the stylesheets', () => {
    expect(css.length).toBeGreaterThan(0)
    expect(popoverCss.length).toBeGreaterThan(0)
  })

  it('RouteSection imports its own layout, so the intake gets it without the order-form chunk', () => {
    expect(routeSectionSource).toMatch(/import '\.\.\/transport-order-form\.css'/)
    expect(routeSectionSource).toMatch(/import '\.\/route-section\.css'/)
  })

  it('lays Laden and Lossen in two EQUAL columns that may shrink, cards equally tall', () => {
    const grid = ruleBody(desktop, '.tof-stops-grid')
    expect(grid).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)\s+minmax\(0,\s*1fr\)/)
    expect(grid).toMatch(/align-items:\s*stretch/)
    const card = ruleBody(desktop, '.tof-stops-grid .tof-stop')
    expect(card).toMatch(/box-sizing:\s*border-box/)
    expect(card).toMatch(/min-width:\s*0/)
  })

  it('stacks to ONE column on tablet/phone — no horizontal overflow', () => {
    expect(ruleBody(mobile, '.tof-stops-grid')).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)\s*;/)
  })

  it('keeps Van | Tot as two equal columns at EVERY width, the time field filling its column', () => {
    const pair = ruleBody(desktop, '.tof-time-pair')
    expect(pair).toMatch(/display:\s*grid/)
    expect(pair).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)\s+minmax\(0,\s*1fr\)/)
    // The pair is deliberately absent from the mobile block: two 24h fields fit side by side.
    expect(mobile).not.toMatch(/\.tof-time-pair/)
    const input = ruleBody(desktop, '.tof-time-pair .ui-form-field input.ui-time-input')
    expect(input).toMatch(/width:\s*100%/)
    expect(input).toMatch(/box-sizing:\s*border-box/)
    // Never narrower than a full "23:59".
    expect(input).toMatch(/min-width:\s*calc\(5\.5ch \+ 26px\)/)
  })

  it('uses design tokens only and never scales or zooms', () => {
    for (const source of [css, popoverCss]) {
      expect(source).not.toMatch(/--color-/)
      expect(source).not.toMatch(/\bzoom\s*:/)
      expect(source).not.toMatch(/transform:\s*scale/)
    }
  })
})

describe('AddressAutocompleteInput.css — compact popover that never shifts the layout', () => {
  it('positions the list absolutely under the field and caps it to the viewport', () => {
    const list = ruleBody(popoverCss, '.address-autocomplete-list')
    expect(list).toMatch(/position:\s*absolute/)
    expect(list).toMatch(/top:\s*calc\(100% \+ var\(--space-1\)\)/)
    expect(list).toMatch(/max-width:\s*min\(420px,\s*calc\(100vw - 2 \* var\(--space-4\)\)\)/)
    expect(list).toMatch(/max-height:\s*\d+px/)
    expect(list).toMatch(/overflow-y:\s*auto/)
    expect(list).toMatch(/box-sizing:\s*border-box/)
    expect(ruleBody(popoverCss, '.address-autocomplete')).toMatch(/position:\s*relative/)
  })
})
