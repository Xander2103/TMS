import type { VatTreatmentInfo } from '../types'

/**
 * Pure decision model for the default-VAT-rate control, derived from a catalog entry
 * (GET /api/customers/vat-treatments). The backend catalog stays authoritative; this only
 * translates one entry + the user's fiscal permission into how the rate control renders.
 */
export interface RateControl {
  /** 'locked': the treatment forces one rate — hide the select. 'select': offer choices. */
  mode: 'locked' | 'select'
  /** Locked mode only: the enforced rate (null = company default, no explicit rate). */
  lockedRate: number | null
  /** Select mode only: the selectable standard rates. */
  rates: number[]
  /** Select mode only: whether the "Aangepast…" (custom rate) option is offered. */
  allowCustom: boolean
}

/**
 * Resolves how the rate control behaves for a treatment:
 * - no catalog entry (still loading / unknown) → fall back to the domestic standard rates;
 * - fixed treatments (no custom rate, at most one standard rate) → locked to that rate;
 * - otherwise a select; "Aangepast…" only when the treatment allows it, or — for treatments
 *   with multiple standard rates such as DomesticVat — when the user may manage fiscal data.
 */
export function resolveRateOptions(entry: VatTreatmentInfo | undefined, hasFiscalPermission: boolean): RateControl {
  if (!entry) {
    return { mode: 'select', lockedRate: null, rates: [0, 6, 12, 21], allowCustom: hasFiscalPermission }
  }

  if (!entry.allowsCustomRate && entry.standardRates.length <= 1) {
    return {
      mode: 'locked',
      lockedRate: entry.defaultRatePercent ?? entry.standardRates[0] ?? null,
      rates: [],
      allowCustom: false,
    }
  }

  return {
    mode: 'select',
    lockedRate: null,
    rates: entry.standardRates,
    allowCustom: entry.allowsCustomRate || hasFiscalPermission,
  }
}
