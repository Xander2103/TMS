/**
 * Postal-code format per country (master sprint 2026-09-21, D3). Deliberately small: only the
 * countries the business ships to daily get a format rule; ANY other country is accepted as typed.
 *
 * Callers validate only a value that was entered or changed in the current session, so stored
 * data that predates the rule can never block a save.
 */

interface PostalCodeRule {
  pattern: RegExp
  /** Translation key of the user-facing message ("Een Belgische postcode heeft 4 cijfers."). */
  messageKey: string
}

const RULES: Record<string, PostalCodeRule> = {
  BE: { pattern: /^\d{4}$/, messageKey: 'stopEditor.postalCode.BE' },
  // "1234 AB"; the space is optional on input ("1234AB") — both are the same postcode.
  NL: { pattern: /^\d{4} ?[A-Za-z]{2}$/, messageKey: 'stopEditor.postalCode.NL' },
  FR: { pattern: /^\d{5}$/, messageKey: 'stopEditor.postalCode.FR' },
  DE: { pattern: /^\d{5}$/, messageKey: 'stopEditor.postalCode.DE' },
  // "L-1234" is how Luxembourg addresses are commonly written; the prefix is optional.
  LU: { pattern: /^(L-)?\d{4}$/i, messageKey: 'stopEditor.postalCode.LU' },
}

/**
 * Returns the translation KEY of the format error, or null when the value is acceptable: empty,
 * a country without a rule, or a value matching its country's format.
 */
export function postalCodeErrorKey(countryCode: string | null | undefined, postalCode: string): string | null {
  const value = postalCode.trim()
  if (value === '') return null
  const rule = RULES[(countryCode ?? '').trim().toUpperCase()]
  if (!rule) return null
  return rule.pattern.test(value) ? null : rule.messageKey
}
