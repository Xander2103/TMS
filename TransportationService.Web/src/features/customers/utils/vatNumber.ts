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

/** Dutch validation message for the (raw) input, or null when acceptable/empty. */
export function validateVatNumber(input: string): string | null {
  const normalized = normalizeVatNumber(input)
  if (normalized === null) return null

  if (normalized.startsWith('BE')) {
    const digits = normalized.slice(2)
    if (!/^\d{10}$/.test(digits)) {
      return 'Belgisch BTW-nummer moet uit BE + 10 cijfers bestaan (bv. BE0123456749).'
    }
    if (digits[0] !== '0' && digits[0] !== '1') {
      return 'Belgisch BTW-nummer moet met BE0 of BE1 beginnen.'
    }
    const body = Number(digits.slice(0, 8))
    const check = Number(digits.slice(8))
    if (check !== 97 - (body % 97)) {
      return 'Belgisch BTW-nummer heeft een ongeldig controlegetal.'
    }
    return null
  }

  if (normalized.length < 4 || normalized.length > 20 || !/^[A-Z0-9]+$/.test(normalized)) {
    return 'BTW-nummer heeft een ongeldig formaat.'
  }
  return null
}
