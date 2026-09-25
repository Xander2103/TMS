import { describe, expect, it } from 'vitest'
import { emptyStop, type StopFormRow } from '../../components/sections/orderFormState'
import { applyStopPatch, computePlannedEnd, parseDurationHours, withAutoPlannedEnd } from '../plannedEnd'

describe('computePlannedEnd — einde = start + geplande duur', () => {
  it('08:00 + 4 u = 12:00 the same day', () => {
    expect(computePlannedEnd('2026-09-22', '08:00', 4)).toEqual({ date: '2026-09-22', time: '12:00' })
  })

  it('08:30 + 1,5 u = 10:00 (decimal hours: 1,5 = 90 minutes)', () => {
    expect(computePlannedEnd('2026-09-22', '08:30', 1.5)).toEqual({ date: '2026-09-22', time: '10:00' })
  })

  it('22:00 + 4 u = 02:00 the NEXT day — the end date moves', () => {
    expect(computePlannedEnd('2026-09-22', '22:00', 4)).toEqual({ date: '2026-09-23', time: '02:00' })
  })

  it('crosses a month and a year boundary', () => {
    expect(computePlannedEnd('2026-09-30', '23:30', 1)).toEqual({ date: '2026-10-01', time: '00:30' })
    expect(computePlannedEnd('2026-12-31', '20:00', 30)).toEqual({ date: '2027-01-02', time: '02:00' })
  })

  it('is plain wall-clock arithmetic on a DST night (the server stays authoritative)', () => {
    // 2026-10-25: clocks go back in Europe/Brussels; the FORM still shows 01:00 + 3 u = 04:00.
    expect(computePlannedEnd('2026-10-25', '01:00', 3)).toEqual({ date: '2026-10-25', time: '04:00' })
  })

  it('returns null when start or duration is missing or not positive', () => {
    expect(computePlannedEnd('', '08:00', 4)).toBeNull()
    expect(computePlannedEnd('2026-09-22', '', 4)).toBeNull()
    expect(computePlannedEnd('2026-09-22', '08:00', null)).toBeNull()
    expect(computePlannedEnd('2026-09-22', '08:00', 0)).toBeNull()
    expect(computePlannedEnd('2026-09-22', '08:00', -2)).toBeNull()
    expect(computePlannedEnd('2026-09-22', '08:00', Number.NaN)).toBeNull()
  })
})

describe('parseDurationHours', () => {
  it('accepts a decimal comma and a decimal point', () => {
    expect(parseDurationHours('1,5')).toBe(1.5)
    expect(parseDurationHours('4.25')).toBe(4.25)
    expect(parseDurationHours(' 4 ')).toBe(4)
  })
  it('rejects empty, negative and non-numeric input', () => {
    expect(parseDurationHours('')).toBeNull()
    expect(parseDurationHours('-1')).toBeNull()
    expect(parseDurationHours('vier')).toBeNull()
  })
})

const site = (patch: Partial<StopFormRow> = {}): StopFormRow => ({ ...emptyStop('Site'), ...patch })

describe('applyStopPatch — predictable end-time mode of a site stop', () => {
  it('automatic: the end follows the start', () => {
    let stop = applyStopPatch(site(), { date: '2026-09-22' }, 4)
    stop = applyStopPatch(stop, { fromTime: '08:00' }, 4)
    expect(stop).toMatchObject({ toDate: '2026-09-22', toTime: '12:00', plannedToIsManual: false })

    stop = applyStopPatch(stop, { fromTime: '22:00' }, 4)
    expect(stop).toMatchObject({ toDate: '2026-09-23', toTime: '02:00', plannedToIsManual: false })
  })

  it('automatic: the end follows a changed duration', () => {
    const stop = site({ date: '2026-09-22', fromTime: '08:30', toDate: '2026-09-22', toTime: '12:30' })
    expect(withAutoPlannedEnd(stop, 1.5)).toMatchObject({ toDate: '2026-09-22', toTime: '10:00' })
  })

  it('editing Tot makes the end manual, and a manual end survives a start AND a duration change', () => {
    let stop = site({ date: '2026-09-22', fromTime: '08:00', toDate: '2026-09-22', toTime: '12:00' })
    stop = applyStopPatch(stop, { toTime: '15:30' }, 4)
    expect(stop).toMatchObject({ toTime: '15:30', plannedToIsManual: true })

    stop = applyStopPatch(stop, { fromTime: '09:00' }, 4)
    expect(stop).toMatchObject({ fromTime: '09:00', toTime: '15:30', plannedToIsManual: true })

    expect(withAutoPlannedEnd(stop, 2)).toBe(stop)
  })

  it('a typed end TIME never inherits the computed next-day end DATE (22:00 + 4 u, then Tot 23:30)', () => {
    // Found in the browser: 20:00–23:30 was stored as 23:30 the NEXT day (a 27,5 h job).
    let stop = site({ date: '2026-09-25', fromTime: '22:00', toDate: '2026-09-26', toTime: '02:00' })
    stop = applyStopPatch(stop, { toTime: '23:30' }, 4)
    expect(stop).toMatchObject({ toDate: '2026-09-25', toTime: '23:30', plannedToIsManual: true })

    // An end at or before the start still crosses midnight.
    const overnight = applyStopPatch(site({ date: '2026-09-25', fromTime: '20:00', toDate: '2026-09-26', toTime: '00:00' }), { toTime: '01:00' }, 4)
    expect(overnight).toMatchObject({ toDate: '2026-09-26', toTime: '01:00', plannedToIsManual: true })

    // Clearing first (TimeInput emits '') and typing afterwards ends up the same.
    let cleared = applyStopPatch(site({ date: '2026-09-25', fromTime: '22:00', toDate: '2026-09-26', toTime: '02:00' }), { toTime: '' }, 4)
    expect(cleared).toMatchObject({ toDate: '', toTime: '' })
    cleared = applyStopPatch(cleared, { toTime: '23:30' }, 4)
    expect(cleared).toMatchObject({ toDate: '2026-09-25', toTime: '23:30' })
  })

  it('an end DATE chosen by hand survives a later edit of the end time', () => {
    let stop = site({ date: '2026-09-25', fromTime: '08:00', toDate: '2026-09-25', toTime: '12:00' })
    stop = applyStopPatch(stop, { toDate: '2026-09-27' }, 4)
    stop = applyStopPatch(stop, { toTime: '16:00' }, 4)
    expect(stop).toMatchObject({ toDate: '2026-09-27', toTime: '16:00', plannedToIsManual: true })
  })

  it('"Automatisch berekenen" returns to automatic and recomputes at once', () => {
    const manual = site({ date: '2026-09-22', fromTime: '09:00', toDate: '2026-09-22', toTime: '15:30', plannedToIsManual: true })
    expect(applyStopPatch(manual, { plannedToIsManual: false }, 4)).toMatchObject({
      toDate: '2026-09-22', toTime: '13:00', plannedToIsManual: false,
    })
  })

  it('a manual same-day end rides along with a moved start DATE, its time untouched', () => {
    const manual = site({ date: '2026-09-22', fromTime: '09:00', toDate: '2026-09-22', toTime: '15:30', plannedToIsManual: true })
    expect(applyStopPatch(manual, { date: '2026-09-24' }, 4)).toMatchObject({ toDate: '2026-09-24', toTime: '15:30' })
  })

  it('automatic without a computable end clears it instead of leaving a stale one', () => {
    const stop = site({ date: '2026-09-22', fromTime: '08:00', toDate: '2026-09-22', toTime: '12:00' })
    expect(applyStopPatch(stop, { fromTime: '' }, 4)).toMatchObject({ toDate: '', toTime: '' })
  })

  it('an unrelated edit of a site stop neither flips the mode nor loops', () => {
    const stop = site({ date: '2026-09-22', fromTime: '08:00', toDate: '2026-09-22', toTime: '12:00' })
    const next = applyStopPatch(stop, { city: 'Waver' }, 4)
    expect(next).toMatchObject({ city: 'Waver', toTime: '12:00', plannedToIsManual: false })
    // Idempotent: applying the same patch again changes nothing further.
    expect(applyStopPatch(next, { city: 'Waver' }, 4)).toEqual(next)
  })

  it('NEVER applies to loading/unloading stops or their time requirement', () => {
    const unloading: StopFormRow = {
      ...emptyStop('Unloading'), date: '2026-09-22', fromTime: '06:00', toTime: '07:00',
      timeRequirement: 'Before', timeReqTo: '08:00',
    }
    const next = applyStopPatch(unloading, { fromTime: '06:30' }, 4)
    expect(next).toMatchObject({
      fromTime: '06:30', toTime: '07:00', toDate: '', plannedToIsManual: false,
      timeRequirement: 'Before', timeReqTo: '08:00',
    })
    expect(applyStopPatch(unloading, { toTime: '07:30' }, 4).plannedToIsManual).toBe(false)
    expect(withAutoPlannedEnd(unloading, 4)).toBe(unloading)
  })
})
