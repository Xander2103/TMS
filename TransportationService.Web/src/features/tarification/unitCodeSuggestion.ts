/**
 * Suggests a code from the unit name (uppercase, stripped, max 12) purely as a convenience:
 * the user always stays in control of the final code, and an existing code is never
 * regenerated when the name changes.
 */
export function suggestUnitCode(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, '')
    .slice(0, 12)
}
