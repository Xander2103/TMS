import { afterEach, describe, expect, it } from 'vitest'
import { formatQuantity, resetDecimalSeparatorPreference, setDecimalSeparatorPreference } from '../../../../utils/numbers'
import {
  applyCargoWeightPatch,
  canRecalculateTotalWeight,
  cargoWeightTotalsFromRows,
  computeAutoTotalWeightKg,
  computeCargoWeightTotals,
  inferTotalWeightIsManual,
  isUnitWeightUnknown,
  recalculateTotalWeight,
} from '../sections/cargoWeight'
import { buildSubmitPayload } from '../sections/orderFormPayload'
import {
  applyUnitToCargoRow,
  cargoFromOrder,
  cargoRowFromHeader,
  duplicateCargoRowInList,
  emptyCargoRow,
  type CargoFormRow,
  type OrderFormValues,
} from '../sections/orderFormState'
import type { CargoItem, TransportOrderDetail } from '../../types'
import type { UnitTypeMaster } from '../../../tarification/api/pricingApi'

/**
 * Master sprint 2026-09-21, D4 — goods-line weight. Total = quantity × weight per unit while the
 * total is automatic; a typed total is manual and survives; a total-only line NEVER yields a
 * weight per unit (not in the form, not in the payload).
 */

afterEach(() => resetDecimalSeparatorPreference())

function row(patch: Partial<CargoFormRow> = {}): CargoFormRow {
  return { ...emptyCargoRow(), ...patch }
}

describe('computeAutoTotalWeightKg', () => {
  it('multiplies quantity by weight per unit: 3 × 2000 = 6000', () => {
    expect(computeAutoTotalWeightKg('3', '2000')).toBe(6000)
  })

  it('reads both decimal separators through the shared parse helper and formats per tenant setting', () => {
    expect(computeAutoTotalWeightKg('3', '2000,5')).toBe(6001.5)
    expect(computeAutoTotalWeightKg('2,5', '10.2')).toBe(25.5)
    expect(formatQuantity(computeAutoTotalWeightKg('3', '2000,5'))).toBe('6.001,5')
    setDecimalSeparatorPreference('.')
    expect(formatQuantity(computeAutoTotalWeightKg('3', '2000,5'))).toBe('6,001.5')
  })

  it('strips float noise and keeps grams', () => {
    expect(computeAutoTotalWeightKg('3', '0.1')).toBe(0.3)
    expect(computeAutoTotalWeightKg('7', '0.001')).toBe(0.007)
  })

  it('is null while either side is missing or invalid', () => {
    expect(computeAutoTotalWeightKg('', '2000')).toBeNull()
    expect(computeAutoTotalWeightKg('3', '')).toBeNull()
    expect(computeAutoTotalWeightKg('0', '2000')).toBeNull()
    expect(computeAutoTotalWeightKg('abc', '2000')).toBeNull()
  })
})

describe('applyCargoWeightPatch', () => {
  it('fills the total once quantity and weight per unit are both known', () => {
    const next = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { weightPerUnitKg: '2000' })
    expect(next.totalWeightKg).toBe('6000')
    expect(next.totalWeightIsManual).toBe(false)
  })

  it('recomputes when the quantity changes', () => {
    const filled = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { weightPerUnitKg: '2000' })
    expect(applyCargoWeightPatch(filled, { expectedQuantity: '4' }).totalWeightKg).toBe('8000')
  })

  it('recomputes when the weight per unit changes', () => {
    const filled = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { weightPerUnitKg: '2000' })
    expect(applyCargoWeightPatch(filled, { weightPerUnitKg: '1500' }).totalWeightKg).toBe('4500')
  })

  it('a hand-typed total becomes manual and survives later quantity / weight changes', () => {
    const filled = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { weightPerUnitKg: '2000' })
    const manual = applyCargoWeightPatch(filled, { totalWeightKg: '6150' })
    expect(manual.totalWeightIsManual).toBe(true)
    const afterQuantity = applyCargoWeightPatch(manual, { expectedQuantity: '5' })
    expect(afterQuantity.totalWeightKg).toBe('6150')
    const afterWeight = applyCargoWeightPatch(afterQuantity, { weightPerUnitKg: '100' })
    expect(afterWeight.totalWeightKg).toBe('6150')
    expect(afterWeight.totalWeightIsManual).toBe(true)
  })

  it('"Herbereken" returns a manual total to automatic', () => {
    const manual = row({ expectedQuantity: '3', weightPerUnitKg: '2000', totalWeightKg: '6150', totalWeightIsManual: true })
    expect(canRecalculateTotalWeight(manual)).toBe(true)
    const auto = recalculateTotalWeight(manual)
    expect(auto.totalWeightKg).toBe('6000')
    expect(auto.totalWeightIsManual).toBe(false)
    expect(canRecalculateTotalWeight(auto)).toBe(false)
    // …and it follows the inputs again.
    expect(applyCargoWeightPatch(auto, { expectedQuantity: '2' }).totalWeightKg).toBe('4000')
  })

  it('clears an automatic total whose inputs disappear, but never a typed one', () => {
    const filled = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { weightPerUnitKg: '2000' })
    expect(applyCargoWeightPatch(filled, { weightPerUnitKg: '' }).totalWeightKg).toBe('')
    const manual = applyCargoWeightPatch(filled, { totalWeightKg: '6150' })
    expect(applyCargoWeightPatch(manual, { weightPerUnitKg: '' }).totalWeightKg).toBe('6150')
  })

  it('leaves the weight fields alone for unrelated patches', () => {
    const manual = row({ expectedQuantity: '3', weightPerUnitKg: '2000', totalWeightKg: '6150', totalWeightIsManual: true })
    expect(applyCargoWeightPatch(manual, { description: 'Staal' })).toEqual({ ...manual, description: 'Staal' })
  })
})

describe('total-only lines never yield a weight per unit', () => {
  const totalOnly = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { totalWeightKg: '6000' })

  it('keeps the weight per unit empty — 3 pallets / 6000 kg does not prove 2000 kg each', () => {
    expect(totalOnly.weightPerUnitKg).toBe('')
    expect(totalOnly.totalWeightIsManual).toBe(true)
    expect(isUnitWeightUnknown(totalOnly)).toBe(true)
    expect(applyCargoWeightPatch(totalOnly, { expectedQuantity: '4' }).weightPerUnitKg).toBe('')
  })

  it('"Herbereken" cannot touch a lone total', () => {
    expect(canRecalculateTotalWeight(totalOnly)).toBe(false)
    expect(recalculateTotalWeight(totalOnly)).toBe(totalOnly)
  })

  it('an emptied manual total goes back to automatic', () => {
    const emptied = applyCargoWeightPatch(totalOnly, { totalWeightKg: '' })
    expect(recalculateTotalWeight(emptied).totalWeightIsManual).toBe(false)
  })

  it('reports no heaviest unit for lines without a weight per unit', () => {
    const totals = cargoWeightTotalsFromRows([
      totalOnly,
      row({ expectedQuantity: '2', weightPerUnitKg: '750', totalWeightKg: '1500' }),
      row({ expectedQuantity: '1', weightPerUnitKg: '1200,5', totalWeightKg: '1200.5' }),
    ])
    expect(totals).toEqual({ totalWeightKg: 8700.5, heaviestUnitKg: 1200.5, linesWithoutUnitWeight: 1 })
    expect(computeCargoWeightTotals([{ totalWeightKg: 6000, weightPerUnitKg: null }])).toEqual({
      totalWeightKg: 6000, heaviestUnitKg: null, linesWithoutUnitWeight: 1,
    })
    expect(computeCargoWeightTotals([])).toEqual({ totalWeightKg: null, heaviestUnitKg: null, linesWithoutUnitWeight: 0 })
  })
})

function cargoItem(overrides: Partial<CargoItem>): CargoItem {
  return {
    id: 'cargo-1', sequence: 1, description: null, barcode: null, expectedQuantity: 1, quantityUnit: null,
    quantityUnitCode: null, notes: null, unitType: null, unitTypeLabel: null, totalWeightKg: null,
    weightPerUnitKg: null, lengthMeters: null, widthMeters: null, heightMeters: null, volumeM3: null,
    volumeIsManual: false, adrRequired: false, adrDetails: null, stackable: true, reference: null,
    loadingStopId: null, unloadingStopId: null, palletCount: null,
    ...overrides,
  }
}

describe('saved lines → form rows', () => {
  it('infers the mode: computed totals are automatic, anything else was typed', () => {
    expect(inferTotalWeightIsManual(3, 2000, 6000)).toBe(false)
    expect(inferTotalWeightIsManual(3, 2000, 6150)).toBe(true)
    expect(inferTotalWeightIsManual(3, null, 6000)).toBe(true)
    expect(inferTotalWeightIsManual(3, 2000, null)).toBe(false)
    expect(inferTotalWeightIsManual(3, 0.1, 0.3)).toBe(false)
  })

  it('a historical total-only line loads unchanged and round-trips with a null weight per unit', () => {
    const order = {
      stops: [],
      cargoItems: [cargoItem({ id: 'c1', expectedQuantity: 3, totalWeightKg: 6000 })],
    } as unknown as TransportOrderDetail
    const rows = cargoFromOrder(order)
    expect(rows[0]).toMatchObject({ totalWeightKg: '6000', weightPerUnitKg: '', totalWeightIsManual: true })

    const payload = buildSubmitPayload({ stops: [], cargoItems: rows, ...payloadRest } as OrderFormValues)
    expect(payload.cargoItems[0].totalWeightKg).toBe(6000)
    expect(payload.cargoItems[0].weightPerUnitKg).toBeNull()
  })

  it('submits what was entered for a line with both weights', () => {
    const filled = applyCargoWeightPatch(row({ expectedQuantity: '3' }), { weightPerUnitKg: '2000' })
    const payload = buildSubmitPayload({ stops: [], cargoItems: [filled], ...payloadRest } as OrderFormValues)
    expect(payload.cargoItems[0]).toMatchObject({ expectedQuantity: 3, weightPerUnitKg: 2000, totalWeightKg: 6000 })
    // The mode is form-only: the API stores no flag.
    expect(payload.cargoItems[0]).not.toHaveProperty('totalWeightIsManual')
  })

  it('the header-summary conversion keeps its typed total manual', () => {
    const converted = cargoRowFromHeader({
      quantity: '3', quantityUnit: '', quantityUnitCode: 'EUROPALLET', weightKg: '6000', volumeM3: '', palletCount: '',
    })
    expect(converted).toMatchObject({ totalWeightKg: '6000', weightPerUnitKg: '', totalWeightIsManual: true })
  })
})

describe('row helpers', () => {
  const master = {
    code: 'EUROPALLET', dimensionBehavior: 'DefaultButOverridable',
    defaultLengthCm: 120, defaultWidthCm: 80, defaultHeightCm: null, defaultWeightKg: 25,
  } as unknown as UnitTypeMaster

  it('a unit default weight drives an automatic total, never a manual one', () => {
    expect(applyUnitToCargoRow(row({ expectedQuantity: '4' }), 'EUROPALLET', master)).toMatchObject({
      weightPerUnitKg: '25', totalWeightKg: '100', totalWeightIsManual: false,
    })
    const manual = row({ expectedQuantity: '4', totalWeightKg: '6000', totalWeightIsManual: true })
    expect(applyUnitToCargoRow(manual, 'EUROPALLET', master)).toMatchObject({ weightPerUnitKg: '25', totalWeightKg: '6000' })
  })

  it('duplicates a line right under its source as a NEW line without its barcode', () => {
    const first = row({ id: 'c1', description: 'Staal', barcode: 'BC-1', expectedQuantity: '3', weightPerUnitKg: '2000', totalWeightKg: '6000' })
    const second = row({ description: 'Hout' })
    const result = duplicateCargoRowInList([first, second], first.key)
    expect(result).toHaveLength(3)
    expect(result[0]).toBe(first)
    expect(result[2]).toBe(second)
    expect(result[1]).toMatchObject({ id: null, barcode: '', description: 'Staal', weightPerUnitKg: '2000', totalWeightKg: '6000' })
    expect(result[1].key).not.toBe(first.key)
  })
})

/** Everything `buildSubmitPayload` reads besides stops/cargo — irrelevant to these assertions. */
const payloadRest = {
  customerId: 'cust-1', customerReference: '', orderDate: '', goodsDescription: '', quantity: '', quantityUnit: '',
  quantityUnitCode: null, weightKg: '', volumeM3: '', palletCount: '', distanceKm: '', loadingMeters: '',
  adrRequired: false, craneRequired: false, plateauRequired: false, moffettRequired: false, isReturnMovement: false,
  agreedPrice: '', notes: '', legalEntityId: '', dieselSurchargeOverride: false, dieselSurchargePercentOverride: '',
  dieselSurchargeOverrideReason: '', serviceOptions: [], selectedServiceOptionIds: [], serviceQuantities: {},
  servicePallets: {}, serviceDays: {}, serviceNotes: {}, priceIsManual: false, priceOverrideReason: '',
  pricingSource: 'Contract', oneOffFixedAmount: '', oneOffTimeMode: 'none', oneOffIncludedLoadingMinutes: '',
  oneOffIncludedUnloadingMinutes: '', oneOffIncludedCombinedMinutes: '', oneOffExtraHourlyRate: '', oneOffNotes: '',
  includedLoadingMinutesOverride: '', includedUnloadingMinutesOverride: '', extraTimeHourlyRateOverride: '',
  extraTimeRoundingStepMinutes: '', extraTimeMinimumBillableMinutes: '',
}
