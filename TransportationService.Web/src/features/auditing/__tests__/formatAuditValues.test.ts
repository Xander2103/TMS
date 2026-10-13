import { describe, expect, it } from 'vitest'
import { formatAuditValues, isSensitiveAuditKey } from '../formatAuditValues'

describe('formatAuditValues', () => {
  it('formats plain values unchanged', () => {
    expect(formatAuditValues('{"Name":"Depot Noord","IsActive":true}')).toBe(
      'Name: Depot Noord · IsActive: true',
    )
  })

  it('renders null and missing values as an em dash', () => {
    expect(formatAuditValues('{"Note":null}')).toBe('Note: —')
  })

  it('masks confidential identifiers even when the backend stored them partially masked', () => {
    const formatted = formatAuditValues(
      '{"NationalRegisterNumber":"93••••••21","Iban":"BE68••••7034","Name":"Jan"}',
    )
    expect(formatted).toBe('NationalRegisterNumber: •••• · Iban: •••• · Name: Jan')
  })

  it('masks credential-shaped keys regardless of casing or separators', () => {
    expect(formatAuditValues('{"passwordHash":"x","refresh_token":"y","SecurityStamp":"z"}')).toBe(
      'passwordHash: •••• · refresh_token: •••• · SecurityStamp: ••••',
    )
  })

  it('does not mask empty sensitive values (nothing to hide, keeps the diff readable)', () => {
    expect(formatAuditValues('{"Iban":null}')).toBe('Iban: —')
  })

  it('returns raw text for non-JSON payloads', () => {
    expect(formatAuditValues('legacy free text')).toBe('legacy free text')
  })

  it('returns an empty string for null input', () => {
    expect(formatAuditValues(null)).toBe('')
  })
})

describe('isSensitiveAuditKey', () => {
  it.each(['Iban', 'BIC', 'nationalRegisterNumber', 'Identity_Card_Number', 'ApiSecret', 'AccessToken'])(
    'flags %s',
    (key) => {
      expect(isSensitiveAuditKey(key)).toBe(true)
    },
  )

  it.each(['Name', 'Status', 'Amount', 'Tokens'])('passes %s through', (key) => {
    // "Tokens" DOES match the token pattern — document the deliberately-broad net.
    expect(isSensitiveAuditKey(key)).toBe(key === 'Tokens')
  })
})
