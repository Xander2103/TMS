import { describe, expect, it } from 'vitest'
import { resolveRateOptions } from '../utils/vatTreatment'
import type { VatTreatmentInfo } from '../types'

const domesticVat: VatTreatmentInfo = {
  treatment: 'DomesticVat',
  label: 'Btw-plichtige klant (binnenland)',
  requiresVatNumber: false,
  standardRates: [0, 6, 12, 21],
  defaultRatePercent: null,
  invoiceLegalText: null,
  allowsCustomRate: false,
}

const reverseCharge: VatTreatmentInfo = {
  treatment: 'ReverseCharge',
  label: 'Medecontractant / btw verlegd',
  requiresVatNumber: true,
  standardRates: [0],
  defaultRatePercent: 0,
  invoiceLegalText: 'Btw verlegd — art. 20 KB nr. 1 / art. 51 §4 W.Btw.',
  allowsCustomRate: false,
}

const other: VatTreatmentInfo = {
  treatment: 'Other',
  label: 'Andere regeling',
  requiresVatNumber: false,
  standardRates: [],
  defaultRatePercent: null,
  invoiceLegalText: null,
  allowsCustomRate: true,
}

describe('resolveRateOptions', () => {
  it('locks the rate for a fixed treatment with a single standard rate', () => {
    const control = resolveRateOptions(reverseCharge, true)
    expect(control.mode).toBe('locked')
    expect(control.lockedRate).toBe(0)
    expect(control.allowCustom).toBe(false)
  })

  it('locks fixed treatments regardless of the fiscal permission', () => {
    expect(resolveRateOptions(reverseCharge, false).mode).toBe('locked')
  })

  it('offers the standard rates for DomesticVat, with custom only for fiscal managers', () => {
    const withPermission = resolveRateOptions(domesticVat, true)
    expect(withPermission.mode).toBe('select')
    expect(withPermission.rates).toEqual([0, 6, 12, 21])
    expect(withPermission.allowCustom).toBe(true)

    const withoutPermission = resolveRateOptions(domesticVat, false)
    expect(withoutPermission.mode).toBe('select')
    expect(withoutPermission.rates).toEqual([0, 6, 12, 21])
    expect(withoutPermission.allowCustom).toBe(false)
  })

  it('always allows a custom rate when the treatment itself allows it', () => {
    const control = resolveRateOptions(other, false)
    expect(control.mode).toBe('select')
    expect(control.rates).toEqual([])
    expect(control.allowCustom).toBe(true)
  })

  it('falls back to the domestic standard rates while the catalog has not loaded', () => {
    const control = resolveRateOptions(undefined, false)
    expect(control.mode).toBe('select')
    expect(control.rates).toEqual([0, 6, 12, 21])
    expect(control.allowCustom).toBe(false)
  })

  it('uses defaultRatePercent over the standard rate when locking', () => {
    const exempt: VatTreatmentInfo = { ...reverseCharge, treatment: 'VatExempt', standardRates: [], defaultRatePercent: 0 }
    expect(resolveRateOptions(exempt, true).lockedRate).toBe(0)
    const noRate: VatTreatmentInfo = { ...reverseCharge, standardRates: [], defaultRatePercent: null }
    expect(resolveRateOptions(noRate, true).lockedRate).toBeNull()
  })
})
