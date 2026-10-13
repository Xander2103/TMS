import { describe, expect, it } from 'vitest'
import { blockPosition, shiftAnchor, toIsoDate, viewDates } from '../boardMath'
import { decodeDragPayload, encodeDragPayload } from '../../types'

describe('viewDates', () => {
  it('returns consecutive days starting at the anchor', () => {
    expect(viewDates('2026-07-20', 3)).toEqual(['2026-07-20', '2026-07-21', '2026-07-22'])
  })

  it('crosses month boundaries', () => {
    expect(viewDates('2026-07-31', 3)).toEqual(['2026-07-31', '2026-08-01', '2026-08-02'])
  })
})

describe('shiftAnchor', () => {
  it('moves forward and backward by the view size', () => {
    expect(shiftAnchor('2026-07-20', 7, 1)).toBe('2026-07-27')
    expect(shiftAnchor('2026-07-20', 7, -1)).toBe('2026-07-13')
  })
})

describe('blockPosition', () => {
  it('is null without a planned start (block stacks in the unscheduled lane)', () => {
    expect(blockPosition(null, null)).toBeNull()
  })

  it('positions a block inside the 05:00-22:00 grid', () => {
    const pos = blockPosition('2026-07-20T08:00:00', '2026-07-20T12:00:00')
    // 08:00 is 180 minutes past 05:00 in a 1020-minute grid.
    expect(pos!.topPct).toBeCloseTo((180 / 1020) * 100, 5)
    expect(pos!.heightPct).toBeCloseTo((240 / 1020) * 100, 5)
  })

  it('clamps to the grid and enforces a minimum height', () => {
    const early = blockPosition('2026-07-20T03:00:00', '2026-07-20T04:00:00')
    expect(early!.topPct).toBe(0)
    expect(early!.heightPct).toBeGreaterThanOrEqual(3)
  })
})

describe('drag payload codec', () => {
  it('round-trips a payload', () => {
    const raw = encodeDragPayload({ kind: 'order', id: 'abc', label: 'ORD-1' })
    expect(decodeDragPayload(raw)).toEqual({ kind: 'order', id: 'abc', label: 'ORD-1' })
  })

  it('rejects malformed payloads', () => {
    expect(decodeDragPayload('not json')).toBeNull()
    expect(decodeDragPayload('{"kind":"bogus","id":"x"}')).toBeNull()
    expect(decodeDragPayload('42')).toBeNull()
  })
})

describe('toIsoDate', () => {
  it('pads month and day', () => {
    expect(toIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05')
  })
})
