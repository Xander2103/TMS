/**
 * Client-side mirror of the backend VatNumberValidator: Belgian numbers ("BE" prefix) get
 * the strict structure + mod-97 checksum check, foreign numbers only a loose sanity check.
 * The backend stays authoritative — this exists for immediate field-level feedback.
 */

/** Strips whitespace, dots and hyphens and uppercases. Empty input → null. */
export function normalizeVatNumber(input: string): string | null {
  const normalized = input.replace(/[\s.-]/g, '').toUpperCase()
  return normalized === '' ? null : normalized
}

/**
 * Validation message KEY (customers.vatValidation.*) for the (raw) input, or null when
 * acceptable/empty. Callers render via t(key).
 */
export function validateVatNumber(input: string): string | null {
  const normalized = normalizeVatNumber(input)
  if (normalized === null) return null

  if (normalized.startsWith('BE')) {
    const digits = normalized.slice(2)
    if (!/^\d{10}$/.test(digits)) {
      return 'customers.vatValidation.beStructure'
    }
    if (digits[0] !== '0' && digits[0] !== '1') {
      return 'customers.vatValidation.beStart'
    }
    const body = Number(digits.slice(0, 8))
    const check = Number(digits.slice(8))
    if (check !== 97 - (body % 97)) {
      return 'customers.vatValidation.beChecksum'
    }
    return null
  }

  if (normalized.length < 4 || normalized.length > 20 || !/^[A-Z0-9]+$/.test(normalized)) {
    return 'customers.vatValidation.genericFormat'
  }
  return null
}
