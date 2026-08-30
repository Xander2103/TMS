import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { buildSubmitPayload } from '../sections/orderFormPayload'
import { stopsFromOrder, type OrderFormValues, type StopFormRow } from '../sections/orderFormState'
import { resetTimeZonePreference, setTimeZonePreference } from '../../../../utils/dates'
import type { TransportOrderDetail, TransportOrderStop } from '../../types'

/**
 * C-03 — create → GET → edit → GET must not drift.
 *
 * The regression this locks down: the form wrote the typed wall clock straight after a "Z"
 * (`${date}T${time}:00Z`) and read it back with `slice(11, 16)`. Both halves were wrong in the
 * same direction, so a naive round trip looked stable while every stored window sat one or two
 * hours off — and any surface that DID convert (a driver app, an ETA comparison) disagreed.
 * With the tenant-zone conversion the wire value is a real instant AND the round trip is exact.
 *
 * The machine zone is forced to America/New_York so nothing here can pass by browser-zone luck.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

afterEach(() => resetTimeZonePreference())

function stopDto(overrides: Partial<TransportOrderStop> = {}): TransportOrderStop {
  return {
    id: 'stop-1', sequence: 1, stopType: 'Loading',
    locationId: null, locationCode: null, locationName: 'Magazijn Antwerpen',
    address: 'Noorderlaan 10', postalCode: '2030', city: 'Antwerpen', countryCode: 'BE',
    plannedFrom: null, plannedTo: null,
    requestedFrom: null, requestedTo: null,
    confirmedFrom: null, confirmedTo: null,
    earliestAllowed: null, latestAllowed: null,
    appointmentRequired: false, appointmentReference: null,
    reference: null, instructions: null,
    accessInstructions: null, loadingInstructions: null, unloadingInstructions: null,
    timeRequirement: 'None', timeRequirementFrom: null, timeRequirementTo: null,
    includedTimeMinutesOverride: null,
    ...overrides,
  }
}

function orderDto(stops: TransportOrderStop[]): TransportOrderDetail {
  return { stops, cargoItems: [], services: [] } as unknown as TransportOrderDetail
}

/** Minimal but COMPLETE form values — buildSubmitPayload trims every string field. */
function formValues(stops: StopFormRow[]): OrderFormValues {
  return {
    customerId: 'cust-1', customerReference: '', orderDate: '2026-07-15', goodsDescription: '',
    quantity: '', quantityUnit: '', quantityUnitCode: null, weightKg: '', volumeM3: '',
    palletCount: '', distanceKm: '', loadingMeters: '',
    adrRequired: false, craneRequired: false, plateauRequired: false, moffettRequired: false,
    isReturnMovement: false, agreedPrice: '', notes: '', legalEntityId: '',
    dieselSurchargeOverride: false, dieselSurchargePercentOverride: '', dieselSurchargeOverrideReason: '',
    stops, cargoItems: [],
    serviceOptions: [], selectedServiceOptionIds: [],
    serviceQuantities: {}, servicePallets: {}, serviceDays: {}, serviceNotes: {},
    priceIsManual: false, priceOverrideReason: '', pricingSource: 'Contract',
    oneOffFixedAmount: '', oneOffTimeMode: 'none',
    oneOffIncludedLoadingMinutes: '', oneOffIncludedUnloadingMinutes: '', oneOffIncludedCombinedMinutes: '',
    oneOffExtraHourlyRate: '', oneOffNotes: '',
    includedLoadingMinutesOverride: '', includedUnloadingMinutesOverride: '',
    extraTimeHourlyRateOverride: '', extraTimeRoundingStepMinutes: '', extraTimeMinimumBillableMinutes: '',
  }
}

/** GET → form → submit, the exact path an edit takes. */
function reSubmit(order: TransportOrderDetail) {
  return buildSubmitPayload(formValues(stopsFromOrder(order))).stops[0]
}

describe('stop time round-trip (create → GET → edit → GET)', () => {
  it('encodes the typed tenant wall clock as a UTC instant', () => {
    const rows = stopsFromOrder(undefined)
    rows[0] = { ...rows[0], date: '2026-07-15', fromTime: '08:00', toTime: '10:00' }
    const payload = buildSubmitPayload(formValues(rows)).stops[0]

    expect(payload.plannedFrom).toBe('2026-07-15T06:00:00Z')
    expect(payload.plannedTo).toBe('2026-07-15T08:00:00Z')
  })

  it('shows the stored instant back as the wall clock that was typed', () => {
    const rows = stopsFromOrder(orderDto([
      stopDto({ plannedFrom: '2026-07-15T06:00:00Z', plannedTo: '2026-07-15T08:00:00Z' }),
    ]))

    expect(rows[0].date).toBe('2026-07-15')
    expect(rows[0].fromTime).toBe('08:00')
    expect(rows[0].toTime).toBe('10:00')
  })

  it('re-submits an unchanged summer order byte-identically (no drift)', () => {
    const stored = stopDto({
      plannedFrom: '2026-07-15T06:00:00Z', plannedTo: '2026-07-15T08:00:00Z',
      requestedFrom: '2026-07-15T05:30:00Z', requestedTo: '2026-07-15T08:30:00Z',
      confirmedFrom: '2026-07-15T06:15:00Z', confirmedTo: '2026-07-15T07:45:00Z',
      earliestAllowed: '2026-07-15T04:00:00Z', latestAllowed: '2026-07-15T16:00:00Z',
    })
    const payload = reSubmit(orderDto([stored]))

    expect(payload.plannedFrom).toBe(stored.plannedFrom)
    expect(payload.plannedTo).toBe(stored.plannedTo)
    expect(payload.requestedFrom).toBe(stored.requestedFrom)
    expect(payload.requestedTo).toBe(stored.requestedTo)
    expect(payload.confirmedFrom).toBe(stored.confirmedFrom)
    expect(payload.confirmedTo).toBe(stored.confirmedTo)
    expect(payload.earliestAllowed).toBe(stored.earliestAllowed)
    expect(payload.latestAllowed).toBe(stored.latestAllowed)
  })

  it('re-submits an unchanged WINTER order byte-identically (the offset differs)', () => {
    const stored = stopDto({ plannedFrom: '2026-01-15T07:00:00Z', plannedTo: '2026-01-15T09:00:00Z' })
    const payload = reSubmit(orderDto([stored]))

    expect(payload.plannedFrom).toBe('2026-01-15T07:00:00Z')
    expect(payload.plannedTo).toBe('2026-01-15T09:00:00Z')
  })

  it('re-submits a window that straddles the spring DST switch byte-identically', () => {
    // 01:30 CET → 03:30 CEST on 29 March 2026: a one-hour wire span, a two-hour clock span.
    const stored = stopDto({ plannedFrom: '2026-03-29T00:30:00Z', plannedTo: '2026-03-29T01:30:00Z' })
    const rows = stopsFromOrder(orderDto([stored]))
    expect(rows[0]).toMatchObject({ date: '2026-03-29', fromTime: '01:30', toTime: '03:30' })

    const payload = buildSubmitPayload(formValues(rows)).stops[0]
    expect(payload.plannedFrom).toBe('2026-03-29T00:30:00Z')
    expect(payload.plannedTo).toBe('2026-03-29T01:30:00Z')
  })

  it('keeps a date-only stop date-only through the round trip', () => {
    // §14 wire encoding: midnight tenant time in plannedFrom, plannedTo null.
    const stored = stopDto({ plannedFrom: '2026-07-14T22:00:00Z', plannedTo: null })
    const rows = stopsFromOrder(orderDto([stored]))
    expect(rows[0]).toMatchObject({ date: '2026-07-15', fromTime: '', toTime: '' })

    const payload = buildSubmitPayload(formValues(rows)).stops[0]
    expect(payload.plannedFrom).toBe('2026-07-14T22:00:00Z')
    expect(payload.plannedTo).toBeNull()
  })

  it('follows a reconfigured tenant zone in both directions', () => {
    setTimeZonePreference('UTC')
    const rows = stopsFromOrder(orderDto([stopDto({ plannedFrom: '2026-07-15T08:00:00Z' })]))
    expect(rows[0].fromTime).toBe('08:00')
    expect(buildSubmitPayload(formValues(rows)).stops[0].plannedFrom).toBe('2026-07-15T08:00:00Z')
  })

  it('leaves a stop without a date empty on the wire', () => {
    const rows = stopsFromOrder(undefined)
    const payload = buildSubmitPayload(formValues(rows)).stops[0]
    expect(payload.plannedFrom).toBeNull()
    expect(payload.plannedTo).toBeNull()
  })
})
