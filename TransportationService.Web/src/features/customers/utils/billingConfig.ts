/** Pure form helpers for the diesel-surcharge panel (validation is mirrored server-side). */

export interface SurchargeFormValues {
  /** Raw percentage input; comma or dot as decimal separator. */
  percent: string
  /** yyyy-MM-dd or '' (empty = onbeperkt). */
  effectiveFrom: string
  effectiveUntil: string
}

export interface SurchargeFormErrors {
  percent?: string
  effectiveUntil?: string
}

/** Parses a user-typed percentage ('3,5' or '3.5'); null when not a finite number. */
export function parsePercent(raw: string): number | null {
  const trimmed = raw.trim()
  if (trimmed === '') return null
  const parsed = Number(trimmed.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

/** Same rules as SaveCustomerDieselSurchargeRequest: 0 ≤ percent ≤ 100, until ≥ from. */
export function validateSurchargeForm(values: SurchargeFormValues): SurchargeFormErrors {
  const errors: SurchargeFormErrors = {}
  const percent = parsePercent(values.percent)
  if (percent === null || percent < 0 || percent > 100) {
    errors.percent = 'Het toeslagpercentage moet tussen 0 en 100 liggen.'
  }
  if (values.effectiveFrom && values.effectiveUntil && values.effectiveUntil < values.effectiveFrom) {
    errors.effectiveUntil = 'De einddatum ligt vóór de begindatum.'
  }
  return errors
}
