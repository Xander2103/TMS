import { describe, expect, it } from 'vitest'
import dossierDetailCss from '../pages/dossier-detail.css?raw'
import orderDetailCss from '../../transport-orders/detail/order-detail.css?raw'
import goodsCapacityCss from '../../transport-orders/components/goods-capacity.css?raw'
import dossierPricingCss from '../pricing/dossier-pricing.css?raw'
import dossierDocumentsCss from '../documents/dossier-documents.css?raw'
import issuedDocumentsCss from '../../transport-orders/components/issued-transport-documents.css?raw'

/**
 * Layout defects found in the first real-browser pass of the master sprint (2026-09-21).
 *
 * jsdom cannot measure layout, so the stylesheet CONTRACT is guarded instead. Every stylesheet
 * here is listed under `test.css.include` in vitest.config.ts; without that entry `?raw` is empty.
 */
const strip = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')

function ruleBody(source: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return strip(source).match(new RegExp(`(?:^|\\}|\\{)\\s*${escaped}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
}

describe('sprint layout invariants', () => {
  it('loads every stylesheet', () => {
    for (const css of [dossierDetailCss, orderDetailCss, goodsCapacityCss, dossierPricingCss, dossierDocumentsCss, issuedDocumentsCss]) {
      expect(css.length).toBeGreaterThan(0)
    }
  })

  it('the dossier and order tab strips scroll sideways only — the -1px link margin grew a vertical scrollbar', () => {
    for (const [css, selector] of [[dossierDetailCss, '.dossier-subnav ul'], [orderDetailCss, '.tod-subnav ul']] as const) {
      const strip_ = ruleBody(css, selector)
      expect(strip_).toMatch(/overflow-x:\s*auto/)
      expect(strip_).toMatch(/overflow-y:\s*hidden/)
    }
  })

  it('a wrapping goods label never pushes its input below the neighbours: label/control/hint share the row tracks', () => {
    const desktop = strip(goodsCapacityCss).split(/@media\s*\(min-width:\s*641px\)/)[1] ?? ''
    const field = ruleBody(desktop, '.tof-row-goods > .ui-form-field')
    expect(field).toMatch(/grid-template-rows:\s*subgrid/)
    expect(field).toMatch(/grid-row:\s*span 3/)
  })

  it('the sales-line cell inputs carry the app control look (they sit outside a FormField)', () => {
    const input = ruleBody(dossierPricingCss, '.dossier-activity-lines-table input')
    expect(input).toMatch(/border:\s*1px solid var\(--border\)/)
    expect(input).toMatch(/border-radius:/)
    expect(input).toMatch(/padding:/)
    expect(input).toMatch(/min-height:/)
  })

  it('the document row actions stay on ONE row on desktop and re-wrap in the card layout', () => {
    const [desktop, cards = ''] = strip(dossierDocumentsCss).split(/@container\s*\(max-width:\s*720px\)/)
    expect(ruleBody(desktop, '.ddoc-actions')).toMatch(/flex-wrap:\s*nowrap/)
    expect(ruleBody(cards, '.ddoc-actions')).toMatch(/flex-wrap:\s*wrap/)
  })

  it('the "Transportdocumenten" kind select and external-number input are styled controls', () => {
    const control = strip(issuedDocumentsCss).match(/\.itd-field select,\s*\.itd-field input\s*\{([^}]*)\}/)?.[1] ?? ''
    expect(control).toMatch(/border:\s*1px solid var\(--border\)/)
    expect(control).toMatch(/min-height:/)
  })
})
