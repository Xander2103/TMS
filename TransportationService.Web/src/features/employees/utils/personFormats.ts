/**
 * Client-side mirrors of the backend EmployeePersonValidators (Belgian national register
 * number + IBAN): normalisation for submission, formatting for display, and validation for
 * immediate field-level feedback. The backend stays authoritative.
 *
 * Safe development test values (checksums valid, not real people/accounts):
 * - NRN: 90.05.01-123.26
 * - IBAN: BE68 5390 0754 7034
 */

/** Keeps only the digits ("90.05.01-123.26" → "90050112326"). Empty → null. */
export function normalizeNrn(input: string): string | null {
  const digits = input.replace(/\D/g, '')
  return digits === '' ? null : digits
}

/** Formats an 11-digit NRN as "YY.MM.DD-XXX.CC"; other lengths are returned unchanged. */
export function formatNrn(input: string): string {
  const digits = normalizeNrn(input)
  if (digits === null || digits.length !== 11) return input
  return `${digits.slice(0, 2)}.${digits.slice(2, 4)}.${digits.slice(4, 6)}-${digits.slice(6, 9)}.${digits.slice(9)}`
}

/** i18n message KEY (employees.validation.*) for the (raw) input, or null when acceptable/empty.
 * Callers translate via t() before display. */
export function validateNrn(input: string): string | null {
  const digits = normalizeNrn(input)
  if (digits === null) return null
  if (digits.length !== 11) return 'employees.validation.nrnLength'

  // Belgian checksum: 97 - (first 9 digits % 97); born in/after 2000 prefixes a 2.
  const body = Number(digits.slice(0, 9))
  const check = Number(digits.slice(9))
  const validPre2000 = check === 97 - (body % 97)
  const validPost2000 = check === 97 - ((2000000000 + body) % 97)
  if (!validPre2000 && !validPost2000) {
    return 'employees.validation.nrnChecksum'
  }
  return null
}

/** Strips whitespace and uppercases ("be68 5390…" → "BE685390…"). Empty → null. */
export function normalizeIban(input: string): string | null {
  const normalized = input.replace(/\s/g, '').toUpperCase()
  return normalized === '' ? null : normalized
}

/** Formats a normalised IBAN in groups of four ("BE68539000754…" → "BE68 5390 …"). */
export function formatIban(input: string): string {
  const normalized = normalizeIban(input)
  if (normalized === null || validateIban(input) !== null) return input
  return normalized.replace(/(.{4})/g, '$1 ').trim()
}

/** i18n message KEY (employees.validation.*) using the official mod-97 algorithm, or null when
 * acceptable/empty. Callers translate via t() before display. */
export function validateIban(input: string): string | null {
  const normalized = normalizeIban(input)
  if (normalized === null) return null

  if (
    normalized.length < 15 ||
    normalized.length > 34 ||
    !/^[A-Z]{2}\d{2}[A-Z0-9]+$/.test(normalized)
  ) {
    return 'employees.validation.ibanFormat'
  }

  // Move the first 4 chars to the end, letters → numbers, incremental mod 97.
  const rearranged = normalized.slice(4) + normalized.slice(0, 4)
  let remainder = 0
  for (const char of rearranged) {
    const value = char >= '0' && char <= '9' ? char.charCodeAt(0) - 48 : char.charCodeAt(0) - 55
    remainder = value < 10 ? (remainder * 10 + value) % 97 : (remainder * 100 + value) % 97
  }
  if (remainder !== 1) {
    return 'employees.validation.ibanChecksum'
  }
  return null
}
