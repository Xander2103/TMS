/**
 * WH-H1 / M2 / M8: the quantity domain of company items is WHOLE units (also for meter, liter and
 * kilogram, which are counted as whole units). These bounds mirror the backend
 * (`StockQuantities.MaxMovement` / `MaxStock`); the backend stays authoritative — this only gives
 * the user a clear Dutch message before the request leaves.
 */
export const MAX_MOVEMENT_QUANTITY = 100_000
export const MAX_STOCK_LEVEL = 1_000_000

export type QuantityResult =
  | { ok: true; value: number }
  | { ok: false; key: string; params?: Record<string, string | number> }

interface QuantityBounds {
  min: number
  max: number
}

/**
 * Parses an `<input type="number">` value as a whole quantity. Never coerces: 0, −1, 1.5 or an
 * empty field come back as an error key (translated by the caller), not as 1.
 */
export function parseWholeQuantity(raw: string, { min, max }: QuantityBounds): QuantityResult {
  const text = raw.trim().replace(',', '.')
  if (text === '') return { ok: false, key: 'issuedItems.quantity.required' }
  const value = Number(text)
  if (!Number.isFinite(value)) return { ok: false, key: 'issuedItems.quantity.required' }
  if (!Number.isInteger(value)) return { ok: false, key: 'issuedItems.quantity.whole' }
  if (value < min) return { ok: false, key: 'issuedItems.quantity.min', params: { min } }
  if (value > max) return { ok: false, key: 'issuedItems.quantity.max', params: { max: max.toLocaleString('nl-BE') } }
  return { ok: true, value }
}

/** A movement quantity (receipt, issue): 1 … MAX_MOVEMENT_QUANTITY. */
export function parseMovementQuantity(raw: string): QuantityResult {
  return parseWholeQuantity(raw, { min: 1, max: MAX_MOVEMENT_QUANTITY })
}

/** A target stock level (correction): −MAX_STOCK_LEVEL … MAX_STOCK_LEVEL. */
export function parseStockLevel(raw: string): QuantityResult {
  return parseWholeQuantity(raw, { min: -MAX_STOCK_LEVEL, max: MAX_STOCK_LEVEL })
}
