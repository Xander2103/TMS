import { describe, expect, it } from 'vitest'
import { MAX_MOVEMENT_QUANTITY, parseMovementQuantity, parseStockLevel } from '../quantities'

// WH-H1 / M2 / M8: whole units within bounds; never coerced into 1.
describe('quantities', () => {
  it.each(['0', '-1'])('refuses %s as a movement quantity instead of turning it into 1', (raw) => {
    expect(parseMovementQuantity(raw)).toEqual({ ok: false, key: 'issuedItems.quantity.min', params: { min: 1 } })
  })

  it('refuses decimals with a whole-number message (comma or dot)', () => {
    expect(parseMovementQuantity('1.5')).toMatchObject({ ok: false, key: 'issuedItems.quantity.whole' })
    expect(parseMovementQuantity('1,5')).toMatchObject({ ok: false, key: 'issuedItems.quantity.whole' })
  })

  it('refuses an empty field and a massive receipt', () => {
    expect(parseMovementQuantity('')).toMatchObject({ ok: false, key: 'issuedItems.quantity.required' })
    expect(parseMovementQuantity('2147483647')).toMatchObject({ ok: false, key: 'issuedItems.quantity.max' })
    expect(parseMovementQuantity(String(MAX_MOVEMENT_QUANTITY + 1))).toMatchObject({ ok: false })
  })

  it('accepts whole numbers in range, and a correction may target zero or below', () => {
    expect(parseMovementQuantity('3')).toEqual({ ok: true, value: 3 })
    expect(parseStockLevel('0')).toEqual({ ok: true, value: 0 })
    expect(parseStockLevel('-2')).toEqual({ ok: true, value: -2 })
  })
})
