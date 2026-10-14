/**
 * Last-resort humanizers for domain codes (B32). Screens translate enums through explicit
 * label maps; these helpers exist so that a value the frontend does not know yet is shown as
 * readable words instead of `inventory_status_critical`, `DIRECT_TRANSPORT` or `PartiallyDelivered`.
 * Codes stay unchanged internally; only the rendering changes.
 */

/** "DIRECT_TRANSPORT" → "Direct transport", "PartiallyDelivered" → "Partially delivered". */
export function humanizeCode(value: string | null | undefined): string {
  if (value === null || value === undefined) return ''
  const text = String(value).trim()
  if (!text) return ''
  const spaced = text
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim()
  const lower = spaced.toLowerCase()
  return lower.charAt(0).toUpperCase() + lower.slice(1)
}

type Translate = (key: string) => string

/**
 * Translate a code through the first catalogue that knows it (`t` echoes the key when a
 * translation is missing), falling back to a humanized form of the code itself.
 */
export function labelFor(t: Translate, value: string | null | undefined, ...prefixes: string[]): string {
  if (value === null || value === undefined || value === '') return ''
  for (const prefix of prefixes) {
    const key = `${prefix}.${value}`
    const translated = t(key)
    if (translated && translated !== key) return translated
  }
  return humanizeCode(value)
}
