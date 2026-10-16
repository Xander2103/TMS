/**
 * Transport acceptance T-H4: euro amounts from quantity × unit price, computed EXACTLY in decimal
 * (no binary floating point) and rounded half away from zero to cents — the same rule as the API
 * (Common/Money/MoneyRounding.cs) and the Peppol/UBL invoice. 2,5 × 125,33 = 313,325 → 313,33.
 */

/** Exact decimal digits of a finite number as a scaled BigInt: 125.33 → [12533n, 2]. */
function toScaled(value: number): [bigint, number] {
  // Shortest round-trip representation of the number the user typed (e.g. "125.33", "2.5").
  const text = String(value)
  if (!/^-?\d+(\.\d+)?$/.test(text)) {
    // Exponent notation (very large/small): fall back to a fixed 10-digit expansion.
    return toScaled(Number(value.toFixed(10)))
  }
  const [whole, fraction = ''] = text.split('.')
  return [BigInt(whole + fraction), fraction.length]
}

/** Rounds a scaled BigInt to cents, half away from zero. */
function scaledToCents(scaled: bigint, scale: number): bigint {
  if (scale <= 2) return scaled * 10n ** BigInt(2 - scale)
  const divisor = 10n ** BigInt(scale - 2)
  const negative = scaled < 0n
  const abs = negative ? -scaled : scaled
  let cents = abs / divisor
  if ((abs % divisor) * 2n >= divisor) cents += 1n
  return negative ? -cents : cents
}

/** Rounds an amount to cents, half away from zero, without floating-point drift. */
export function roundMoney(value: number): number {
  const [scaled, scale] = toScaled(value)
  return Number(scaledToCents(scaled, scale)) / 100
}

/** quantity × unit price in euro cents precision (exact, half away from zero). */
export function lineTotal(quantity: number, unitPrice: number): number {
  const [q, qs] = toScaled(quantity)
  const [p, ps] = toScaled(unitPrice)
  return Number(scaledToCents(q * p, qs + ps)) / 100
}
