/**
 * Camera frames repeat the same barcode dozens of times per second; a scan is only
 * accepted when the value differs from the last accepted one or the window elapsed.
 */
export function createScanDebouncer(windowMs = 2000, now: () => number = Date.now) {
  let lastValue: string | null = null
  let lastAt = 0
  return {
    accept(value: string): boolean {
      const at = now()
      if (value === lastValue && at - lastAt < windowMs) {
        return false
      }
      lastValue = value
      lastAt = at
      return true
    },
    reset(): void {
      lastValue = null
      lastAt = 0
    },
  }
}
