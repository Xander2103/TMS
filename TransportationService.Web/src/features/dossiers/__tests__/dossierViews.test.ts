import { describe, expect, it } from 'vitest'
import { EMPTY_DOSSIER_FILTERS } from '../pages/dossierSearchParams'
import { activePreset, applyPreset, matchesPatch, parseSavedViews, presetPatch } from '../pages/dossierViews'

const context = { userId: 'u-1', now: new Date(2026, 9, 14) }

/** B11: presets are plain filter patches over the one server-side search. */
describe('dossier quick views', () => {
  it('each preset is a filter patch the server already understands', () => {
    expect(presetPatch('all', context)).toEqual({})
    expect(presetPatch('myOpen', context)).toEqual({ status: 'Open', responsibleUserId: 'u-1' })
    expect(presetPatch('unplannedWeek', context)).toEqual({ status: 'Open', planning: 'unplanned', dateFrom: '2026-10-14', dateTo: '2026-10-21' })
    expect(presetPatch('unpriced', context)).toEqual({ status: 'Open', priceStatus: 'Unpriced' })
    expect(presetPatch('toInvoice', context)).toEqual({ status: 'Closed', invoiceStatus: 'NotInvoiced' })
    expect(presetPatch('openIncident', context)).toEqual({ status: 'Open', hasOpenIncident: 'true' })
  })

  it('"Mijn open" needs a signed-in user', () => {
    expect(presetPatch('myOpen', { userId: null })).toBeNull()
  })

  it('applying a preset replaces the filters but keeps the search text and sort', () => {
    const current = { ...EMPTY_DOSSIER_FILTERS, search: 'nex', sort: 'customer' as const, dir: 'asc' as const, page: 3, postalCode: '2000' }
    const next = applyPreset(current, presetPatch('unpriced', context)!)
    expect(next).toEqual({ ...EMPTY_DOSSIER_FILTERS, status: 'Open', priceStatus: 'Unpriced', search: 'nex', sort: 'customer', dir: 'asc', page: 1 })
    expect(next.postalCode).toBe('')
  })

  it('recognises the active preset exactly, and none once the planner refines', () => {
    const unpriced = applyPreset(EMPTY_DOSSIER_FILTERS, presetPatch('unpriced', context)!)
    expect(activePreset(unpriced, context)).toBe('unpriced')
    expect(activePreset({ ...unpriced, postalCode: '2000' }, context)).toBeNull()
    expect(activePreset(EMPTY_DOSSIER_FILTERS, context)).toBe('all')
    expect(activePreset({ ...EMPTY_DOSSIER_FILTERS, search: 'x', sort: 'number' }, context)).toBe('all')
  })

  it('matchesPatch ignores search, sort and page', () => {
    expect(matchesPatch({ ...EMPTY_DOSSIER_FILTERS, status: 'Open', search: 'q', page: 4 }, { status: 'Open' })).toBe(true)
    expect(matchesPatch({ ...EMPTY_DOSSIER_FILTERS, status: 'Open' }, { status: 'Closed' })).toBe(false)
  })

  it('saved views parse defensively: unknown keys and empty values are dropped, names trimmed', () => {
    const raw = JSON.stringify([
      { id: 'a', name: '  Mijn Antwerpen  ', filters: { status: 'Open', postalCode: '2000', bogus: 'x', priceStatus: '' } },
      { id: 42, name: 'broken' },
      'not an object',
    ])
    expect(parseSavedViews(raw)).toEqual([{ id: 'a', name: 'Mijn Antwerpen', filters: { status: 'Open', postalCode: '2000' } }])
    expect(parseSavedViews('nope')).toEqual([])
    expect(parseSavedViews(null)).toEqual([])
  })
})
