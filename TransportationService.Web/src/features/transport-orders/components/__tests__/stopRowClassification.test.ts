import { describe, expect, it } from 'vitest'
import { emptyStop, isEmptyStopRow, stopsFromOrder } from '../sections/orderFormState'
import { orderDetail } from '../../../dossiers/__tests__/fixtures'

/**
 * Data-safety contract (browser-smoke 2026-09-11): only a row the planner never touched is
 * "empty" and may be dropped (after confirmation). A row carrying ANY entered value is
 * incomplete and must be validated, never discarded; a persisted stop is never empty.
 */
describe('isEmptyStopRow', () => {
  it('is true for a freshly added stop', () => {
    expect(isEmptyStopRow(emptyStop('Loading'))).toBe(true)
    expect(isEmptyStopRow(emptyStop('Unloading'))).toBe(true)
  })

  it('is false for a persisted stop, even when every visible field was cleared', () => {
    const [persisted] = stopsFromOrder(orderDetail())
    const cleared = { ...persisted, locationId: '', locationName: '', address: '', postalCode: '', city: '', date: '', fromTime: '', toTime: '' }
    expect(cleared.id).toBe('s-1')
    expect(isEmptyStopRow(cleared)).toBe(false)
  })

  it.each([
    ['locationId', 'loc-1'],
    ['locationName', 'Depot'],
    ['address', 'Kaai 5'],
    ['postalCode', '2000'],
    ['city', 'Antwerpen'],
    ['countryCode', 'NL'],
    ['date', '2026-09-12'],
    ['fromTime', '08:00'],
    ['toTime', '10:00'],
    ['timeRequirement', 'Before'],
    ['timeReqFrom', '08:00'],
    ['timeReqTo', '10:00'],
    ['requestedFrom', '2026-09-12T08:00'],
    ['requestedTo', '2026-09-12T10:00'],
    ['confirmedFrom', '2026-09-12T08:00'],
    ['confirmedTo', '2026-09-12T10:00'],
    ['earliestAllowed', '2026-09-12T06:00'],
    ['latestAllowed', '2026-09-12T18:00'],
    ['appointmentRequired', true],
    ['appointmentReference', 'APT-1'],
    ['reference', 'REF-1'],
    ['instructions', 'Bel vooraf'],
    ['accessInstructions', 'Poort 3'],
    ['loadingInstructions', 'Zijlader'],
    ['unloadingInstructions', 'Achterlosser'],
    ['includedTimeMinutesOverride', '45'],
  ] as const)('is false once "%s" carries a value', (field, value) => {
    expect(isEmptyStopRow({ ...emptyStop('Unloading'), [field]: value })).toBe(false)
  })

  it('ignores UI-only state such as the collapsed flag and whitespace-only text', () => {
    expect(isEmptyStopRow({ ...emptyStop('Unloading'), collapsed: true })).toBe(true)
    expect(isEmptyStopRow({ ...emptyStop('Unloading'), reference: '   ', city: ' ' })).toBe(true)
  })
})
