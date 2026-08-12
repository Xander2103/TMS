import type { PricingAgreement, PricingAgreementInput } from './api/pricingApi'

/** Builds a full save payload from a loaded agreement, so a panel only needs to override the fields it edits. */
export function agreementToInput(agreement: PricingAgreement): PricingAgreementInput {
  return {
    customerId: agreement.customerId,
    name: agreement.name,
    effectiveFrom: agreement.effectiveFrom,
    effectiveUntil: agreement.effectiveUntil,
    isActive: agreement.isActive,
    minimumAmount: agreement.minimumAmount,
    notes: agreement.notes,
    surcharges: agreement.surcharges.map((s) => ({ name: s.name, kind: s.kind, value: s.value })),
    isShared: agreement.isShared,
    maximumAmount: agreement.maximumAmount,
    baseAgreementId: agreement.baseAgreementId,
    modifiers: agreement.modifiers.map((m) => ({
      sequence: m.sequence,
      name: m.name,
      countryCode: m.countryCode,
      zoneId: m.zoneId,
      percent: m.percent,
      fixedAmount: m.fixedAmount,
    })),
    includedLoadingMinutes: agreement.includedLoadingMinutes,
    includedUnloadingMinutes: agreement.includedUnloadingMinutes,
    includedCombinedMinutes: agreement.includedCombinedMinutes,
    extraHourlyRate: agreement.extraHourlyRate,
    salesCategoryId: agreement.salesCategoryId,
  }
}
