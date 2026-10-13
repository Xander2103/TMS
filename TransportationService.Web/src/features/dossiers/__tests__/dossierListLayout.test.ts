import { describe, expect, it } from 'vitest'
import dossiersCss from '../pages/dossiers.css?raw'
import dossierDetailCss from '../pages/dossier-detail.css?raw'

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

/** Splits the stylesheet into the plain rules and the `@container (max-width: …)` blocks, in source order. */
function splitContainerQueries(css: string): { desktop: string; queries: { maxWidth: number; block: string }[] } {
  const queries: { maxWidth: number; block: string }[] = []
  let desktop = ''
  let cursor = 0
  const pattern = /@container\s*\(max-width:\s*(\d+)px\)\s*\{/g
  for (let match = pattern.exec(css); match; match = pattern.exec(css)) {
    desktop += css.slice(cursor, match.index)
    let depth = 1
    let index = pattern.lastIndex
    while (depth > 0 && index < css.length) {
      if (css[index] === '{') depth += 1
      else if (css[index] === '}') depth -= 1
      index += 1
    }
    queries.push({ maxWidth: Number(match[1]), block: css.slice(pattern.lastIndex, index - 1) })
    cursor = index
    pattern.lastIndex = index
  }
  desktop += css.slice(cursor)
  return { desktop, queries }
}

const { desktop, queries } = splitContainerQueries(list)

describe('dossier list layout rules', () => {
  it('the dossier number never wraps (code + incident badge in one nowrap line)', () => {
    expect(ruleBody(desktop, '.dossier-list-number {')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(desktop, '.dossier-list-number code')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(desktop, '.dossier-list .data-table thead th:nth-child(1)')).toMatch(/width:\s*116px/)
  })

  it('composed cells stay on one line: status, price and the truncated text cells are nowrap with ellipsis', () => {
    expect(ruleBody(desktop, '.dossier-list-status {')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(desktop, '.dossier-list-price {')).toMatch(/white-space:\s*nowrap/)
    expect(ruleBody(desktop, '.dossier-list-price {')).toMatch(/text-overflow:\s*ellipsis/)
    expect(ruleBody(desktop, '.dossier-list-nowrap')).toMatch(/white-space:\s*nowrap/)
    // The separator lives in CSS so every part keeps its own exact DOM text.
    expect(ruleBody(desktop, '.dossier-list-sep::before')).toMatch(/content:/)
    expect(list).not.toMatch(/\.dossier-list-stack/)
  })

  it('compact rows: fixed table layout, 14px text, 8px vertical padding on a 24px line, cells vertically centred', () => {
    const table = ruleBody(desktop, '.dossier-list .data-table {')
    expect(table).toMatch(/table-layout:\s*fixed/)
    expect(table).toMatch(/font-size:\s*14px/)
    const cells = ruleBody(desktop, '.dossier-list .data-table thead th,\n.dossier-list .data-table tbody td')
    expect(cells).toMatch(/padding:\s*8px 10px/)
    expect(cells).toMatch(/line-height:\s*24px/)
    expect(cells).toMatch(/vertical-align:\s*middle/)
  })

  it('the compact columns have fixed widths (incl. the short Transport/Planning summaries); klant and referentie share the rest', () => {
    const widths = [...desktop.matchAll(/thead th:nth-child\((\d+)\)\s*\{\s*width:\s*(\d+)px/g)].map((m) => [Number(m[1]), Number(m[2])])
    expect(Object.fromEntries(widths)).toEqual({ 1: 116, 2: 170, 5: 136, 6: 184, 7: 156, 8: 196, 9: 96, 10: 44 })
    // min-width = fixed columns + 2 × 150px for the flexible ones (klant, referentie).
    const fixed = widths.reduce((sum, [, width]) => sum + width, 0)
    expect(ruleBody(desktop, '.dossier-list .data-table {')).toContain(`min-width: ${fixed + 2 * 150}px`)
  })

  it('column priority: Facturatie (9) hides first, then Planning (7), then Referentie (4); the sticky actions column stays', () => {
    const hidden = queries.map(({ block }) => Number(block.match(/nth-child\((\d+)\)\s*\{\s*display:\s*none/)?.[1]))
    expect(hidden).toEqual([9, 7, 4])
    const breakpoints = queries.map((query) => query.maxWidth)
    expect(breakpoints).toEqual([...breakpoints].sort((a, b) => b - a))
    // Each step lowers the table's min-width by exactly the column it drops (plus the compact status step).
    expect(queries.map(({ block }) => Number(block.match(/min-width:\s*(\d+)px/)?.[1]))).toEqual([1252, 1096, 946])
    expect(ruleBody(desktop, '.dossier-list .data-table thead th:last-child')).toMatch(/position:\s*sticky/)
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
