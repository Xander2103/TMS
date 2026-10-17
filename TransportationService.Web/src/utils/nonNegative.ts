/**
 * FL-M5/M6: forms run with `noValidate`, so the browser's `min="0"` never blocks a submit.
 * Returns one error per field whose value is a negative number (null/empty is fine).
 */
export function negativeFieldErrors(values: Record<string, number | null | undefined>, message: string): Record<string, string> {
  const errors: Record<string, string> = {}
  for (const [key, value] of Object.entries(values)) {
    if (typeof value === 'number' && value < 0) errors[key] = message
  }
  return errors
}
