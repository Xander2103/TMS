import { describe, expect, it } from 'vitest'
import {
  DEFAULT_DOSSIER_LIST_PREFS,
  DOSSIER_DEFAULT_OPTIONAL,
  DOSSIER_DROP_ORDER,
  DOSSIER_FIXED_COLUMNS,
  fitColumns,
  parseDossierListPrefs,
  tableMinWidth,
  visibleColumns,
} from '../pages/dossierListColumns'

/**
 * B10: the dossier list answers the daily questions (planned? who drives? invoiced?) on a normal
 * laptop. The column model decides what is shown and what steps aside when the container is
 * narrower than the sum of the columns.
 */
describe('dossier list columns', () => {
  it('the default view shows the daily questions next to the identity columns, in display order', () => {
    expect(visibleColumns(DOSSIER_DEFAULT_OPTIONAL)).toEqual([
      'number', 'status', 'customer', 'reference', 'date', 'transport', 'planning', 'price', 'invoice', 'actions',
    ])
  })

  it('a 1440 laptop (≈1110px list) shows every default column; 1366 (≈1036px) drops only the reference', () => {
    const defaults = visibleColumns(DOSSIER_DEFAULT_OPTIONAL)
    expect(tableMinWidth(defaults)).toBeLessThanOrEqual(1110)
    expect(fitColumns(defaults, 1110)).toEqual(defaults)
    expect(fitColumns(defaults, 1036)).toEqual(defaults.filter((key) => key !== 'reference'))
  })

  it('Planning, Prijs and Facturatie are the last to go; the fixed columns never go', () => {
    const everything = visibleColumns(['reference', 'transport', 'planning', 'driver', 'price', 'invoice', 'responsible'])
    const narrow = fitColumns(everything, 750)
    expect(narrow).toEqual(['number', 'status', 'customer', 'date', 'planning', 'price', 'actions'])
    for (const key of DOSSIER_FIXED_COLUMNS) expect(fitColumns(everything, 100)).toContain(key)
    // Drop order: least operational first.
    expect(DOSSIER_DROP_ORDER.indexOf('reference')).toBeLessThan(DOSSIER_DROP_ORDER.indexOf('invoice'))
    expect(DOSSIER_DROP_ORDER.indexOf('invoice')).toBeLessThan(DOSSIER_DROP_ORDER.indexOf('planning'))
  })

  it('an unknown width (before the first measurement) shows everything', () => {
    const defaults = visibleColumns(DOSSIER_DEFAULT_OPTIONAL)
    expect(fitColumns(defaults, 0)).toEqual(defaults)
  })

  it('hiding a column through "Kolommen" frees its width for the rest', () => {
    const withoutReference = visibleColumns(['transport', 'planning', 'price', 'invoice'])
    expect(tableMinWidth(withoutReference)).toBe(tableMinWidth(visibleColumns(DOSSIER_DEFAULT_OPTIONAL)) - 100)
  })

  it('preferences round-trip and fall back field by field', () => {
    expect(parseDossierListPrefs(null)).toEqual(DEFAULT_DOSSIER_LIST_PREFS)
    expect(parseDossierListPrefs('{bad')).toEqual(DEFAULT_DOSSIER_LIST_PREFS)
    expect(parseDossierListPrefs(JSON.stringify({ optional: ['driver', 'bogus', 'price'], density: 'compact' })))
      .toEqual({ optional: ['driver', 'price'], density: 'compact' })
    expect(parseDossierListPrefs(JSON.stringify({ optional: [] })).optional).toEqual([])
  })
})
