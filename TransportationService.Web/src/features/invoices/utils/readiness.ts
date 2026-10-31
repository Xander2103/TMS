/**
 * Wave 2 §6: vertaalsleutels per readiness-redencode (bv. "pricing.stale"). Onbekende
 * codes vallen bij render terug op de rauwe code — logica blijft altijd op de code zelf.
 */
export const READINESS_REASON_KEYS: Record<string, string> = {
  'pricing.none': 'invoices.readiness.reasons.pricingNone',
  'pricing.coverage.partial': 'invoices.readiness.reasons.pricingCoveragePartial',
  'pricing.coverage.none': 'invoices.readiness.reasons.pricingCoverageNone',
  'pricing.stale': 'invoices.readiness.reasons.pricingStale',
  'pod.missing': 'invoices.readiness.reasons.podMissing',
  // MD-H10: never show the raw code "po.missing" to a user.
  'po.missing': 'invoices.readiness.reasons.poMissing',
  'pricing.zero_unconfirmed': 'invoices.readiness.reasons.pricingZeroUnconfirmed',
  // R-N2: one transport is billed once; {number} is the other order of the failed-delivery pair.
  'redelivery.original_charged': 'invoices.readiness.reasons.redeliveryOriginalCharged',
  'redelivery.already_charged': 'invoices.readiness.reasons.redeliveryAlreadyCharged',
}
