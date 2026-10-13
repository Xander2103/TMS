import { describe, expect, it } from 'vitest'
import routeCss from '../sections/route-section.css?raw'
import formCss from '../transport-order-form.css?raw'
import autocompleteCss from '../../../locations/components/AddressAutocompleteInput.css?raw'
import routeSectionSource from '../sections/RouteSection.tsx?raw'
import dossierDetailCss from '../../../dossiers/pages/dossier-detail.css?raw'

/**
 * Stop cards fill the width first (route UX sprint 2026-10-08) + the Van | Tot pair (master
 * sprint 2026-09-21, spec §3/§4).
 *
 * jsdom cannot measure layout, so the stylesheet CONTRACT is guarded instead. The stylesheets are
 * listed under `test.css.include` in vitest.config.ts; without that entry `?raw` is empty.
 */
const strip = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')
const css = strip(routeCss)
const popoverCss = strip(autocompleteCss)
const [desktop, mobile = ''] = css.split(/@media\s*\(max-width:\s*640px\)/)
/** Cards never narrower than this: two field columns and the search + "+ Nieuw klantadres" row stay usable. */
const AUTO_FILL = /grid-template-columns:\s*repeat\(auto-fill,\s*minmax\(min\(380px,\s*100%\),\s*1fr\)\)/

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

  it('fills the available width first: as many ≥380px cards per row as fit, equally tall, in plain flow', () => {
    const grid = ruleBody(desktop, '.tof-stops-grid')
    expect(grid).toMatch(AUTO_FILL)
    expect(grid).toMatch(/align-items:\s*stretch/)
    // No hard two-column cap anywhere: a wide desktop gets a third (and fourth) card per row.
    expect(desktop).not.toMatch(/\.tof-stops-grid\s*\{[^}]*1fr\)\s+minmax\(0,\s*1fr\)/)
    // Plain grid flow keeps the route sequence; nothing is laid out per stop type.
    expect(css).not.toMatch(/grid-auto-flow|grid-template-areas|grid-column\s*:|grid-row\s*:|(^|[^a-z-])order\s*:/m)
    const card = ruleBody(desktop, '.tof-stops-grid .tof-stop')
    expect(card).toMatch(/box-sizing:\s*border-box/)
    expect(card).toMatch(/min-width:\s*0/)
  })

  it('the order form stylesheet carries the SAME rule, so the layout never depends on stylesheet order', () => {
    const form = strip(formCss)
    expect(ruleBody(form, '.tof-stops-grid')).toMatch(AUTO_FILL)
    // Its old tablet cap (one column from 900px down) is gone too: the auto-fill rule decides
    // between one and two cards there; only the phone block (≤640px) forces one column.
    const [formDesktop, formPhone = ''] = form.split(/@media\s*\(max-width:\s*640px\)/)
    expect(formDesktop).not.toMatch(/\.tof-stops-grid\s*\{\s*grid-template-columns:\s*1fr\s*;/)
    expect(formDesktop).not.toMatch(/\.tof-stops-grid\s*\{\s*grid-template-columns:\s*1fr 1fr/)
    expect(formPhone).toMatch(/\.tof-stops-grid\s*\{\s*grid-template-columns:\s*1fr\s*;/)
  })

  it('the dossier Route tab lifts the order-form 1000px cap, so a wide desktop really gets 3–4 cards per row', () => {
    expect(ruleBody(strip(formCss), '.tof')).toMatch(/max-width:\s*1000px/)
    expect(ruleBody(strip(dossierDetailCss), '.dossier-route-sheet.tof')).toMatch(/max-width:\s*none/)
  })

  it('keeps the on-site work card at full width', () => {
    expect(ruleBody(desktop, '.tof-stops-grid.tof-stops-grid-site')).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)\s*;/)
  })

  it('stacks to ONE column on a phone — no horizontal overflow; the minmax(min(…, 100%)) keeps any narrower container at one column', () => {
    expect(ruleBody(mobile, '.tof-stops-grid')).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)\s*;/)
    expect(mobile).toMatch(/\.tof-stops-grid \.tof-row,\s*\.tof-stops-grid \.tof-row-4,\s*\.tof-onsite-grid\s*\{\s*grid-template-columns:\s*minmax\(0,\s*1fr\)/)
    expect(css).not.toMatch(/@media\s*\(max-width:\s*900px\)/)
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
