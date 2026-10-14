import { describe, expect, it } from 'vitest'
import dossiersCss from '../pages/dossiers.css?raw'
import dossierDetailCss from '../pages/dossier-detail.css?raw'
import { DOSSIER_COLUMN_WIDTHS } from '../pages/dossierListColumns'

/**
 * Dossier list refinement (2026-10-08): one dossier = one row. These assertions pin the CSS rules
 * that make that true — nowrap identifiers, one-line composed cells, fixed widths for the
 * compact columns and the column-priority cascade — without pixel-perfect layout tests.
 */
function strip(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '')
}

/** The body of the first rule whose selector list contains `selector` (after the given offset). */
function ruleBody(css: string, selector: string, from = 0): string {
  const index = css.indexOf(selector, from)
  if (index < 0) throw new Error(`rule not found: ${selector}`)
  const open = css.indexOf('{', index)
  const close = css.indexOf('}', open)
  return css.slice(open + 1, close)
}

const list = strip(dossiersCss)

/**
 * B10: the widths live per column CLASS (pages/dossierListColumns.ts is the model; the page
 * drops columns by priority when the container is too narrow), so the stylesheet contract is:
 * one width per fixed column, klant/referentie flexible, the actions column sticky.
 */
describe('dossier list layout rules', () => {
  it('the dossier number never wraps (code + incident badge in one nowrap line)', () => {
    expect(ruleBody(list, '.dossier-list-number {')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(list, '.dossier-list-number code')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(list, '.dossier-list .dossier-col-number')).toMatch(/width:\s*92px/)
  })

  it('composed cells stay on one line: status, price and the truncated text cells are nowrap with ellipsis', () => {
    expect(ruleBody(list, '.dossier-list-status {')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(list, '.dossier-list-price {')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(list, '.dossier-list-price {')).toMatch(/text-overflow:\s*ellipsis/)
    expect(ruleBody(list, '.dossier-list-nowrap')).toMatch(/white-space:\s*nowrap/)
    // The separator lives in CSS so every part keeps its own exact DOM text.
    expect(ruleBody(list, '.dossier-list-sep::before')).toMatch(/content:/)
    expect(list).not.toMatch(/\.dossier-list-stack/)
  })

  it('rows: fixed table layout, 14px text, 8px vertical padding on a 24px line (4px in compact density), cells vertically centred', () => {
    const table = ruleBody(list, '.dossier-list .data-table {')
    expect(table).toMatch(/table-layout:\s*fixed/)
    expect(table).toMatch(/font-size:\s*14px/)
    // The minimum width follows the shown columns (set by the page), never a stale constant.
    expect(table).toMatch(/min-width:\s*var\(--dossier-table-min/)
    const cells = ruleBody(list, '.dossier-list .data-table thead th,\n.dossier-list .data-table tbody td')
    expect(cells).toMatch(/padding:\s*8px 10px/)
    expect(cells).toMatch(/line-height:\s*24px/)
    expect(cells).toMatch(/vertical-align:\s*middle/)
    const compact = ruleBody(list, ".dossier-list[data-density='compact'] .data-table thead th")
    expect(compact).toMatch(/padding-top:\s*4px/)
    expect(compact).toMatch(/padding-bottom:\s*4px/)
  })

  it('every fixed column has its width in CSS and matches the model; klant and referentie share the rest', () => {
    const widths = Object.fromEntries(
      [...list.matchAll(/\.dossier-list \.dossier-col-(\w+)\s*\{\s*width:\s*(\d+)px/g)].map((m) => [m[1], Number(m[2])]),
    )
    // The status column widens on a wide container (second rule); the base width is the model's.
    expect(widths).toEqual({ ...DOSSIER_COLUMN_WIDTHS, status: 170 })
    expect(ruleBody(list, '.dossier-list .dossier-col-status {')).toMatch(/width:\s*104px/)
    expect(widths).not.toHaveProperty('customer')
    expect(widths).not.toHaveProperty('reference')
  })

  it('the sticky actions column stays; the confirmation date only shows on a wide container', () => {
    expect(ruleBody(list, '.dossier-list .data-table thead th:last-child')).toMatch(/position:\s*sticky/)
    expect(list).toMatch(/@container \(min-width: 1300px\)/)
    expect(list).not.toMatch(/@container \(max-width/)
  })
})

describe('dossier header action hierarchy', () => {
  const detail = strip(dossierDetailCss)

  it('the secondary group and the primary action are separated by a clear gap; the primary gets extra weight', () => {
    expect(ruleBody(detail, '.dossier-header-actions {')).toMatch(/gap:\s*8px 20px/)
    expect(ruleBody(detail, '.dossier-header-actions-secondary')).toMatch(/gap:\s*8px/)
    expect(ruleBody(detail, '.dossier-header-actions .dossier-header-primary')).toMatch(/font-weight:\s*700/)
  })

  it('on narrow screens the primary action takes the full width (last and obvious)', () => {
    const mobile = detail.split('@media (max-width: 640px)')[1] ?? ''
    expect(ruleBody(mobile, '.dossier-header-actions .dossier-header-primary')).toMatch(/flex:\s*1 1 100%/)
  })
})
