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

// ---------------------------------------------------------------------------
// B08: width-aware board
// ---------------------------------------------------------------------------

import {
  dayColumnLevel, dayColumnWidth, daysFitWithoutScroll, MIN_DAY_WIDTH, startOfWeek,
} from '../boardMath'
import { formatRoute, formatWeightShort } from '../boardFormat'

describe('startOfWeek', () => {
  it('snaps any day to the Monday of its week (the Week view shows Ma … Zo)', () => {
    expect(startOfWeek('2026-10-14')).toBe('2026-10-12') // Wednesday → Monday
    expect(startOfWeek('2026-10-12')).toBe('2026-10-12') // Monday stays
    expect(startOfWeek('2026-10-18')).toBe('2026-10-12') // Sunday belongs to the week before
  })
})

describe('dayColumnWidth', () => {
  it('shares the measured timeline evenly over the days', () => {
    expect(dayColumnWidth(1050, 7, 'normal')).toBe(150)
    expect(dayColumnWidth(900, 3, 'normal')).toBe(300)
  })

  it('never goes below the readable minimum of the density (then the timeline scrolls)', () => {
    expect(dayColumnWidth(600, 7, 'normal')).toBe(MIN_DAY_WIDTH.normal)
    expect(dayColumnWidth(600, 7, 'compact')).toBe(MIN_DAY_WIDTH.compact)
    expect(daysFitWithoutScroll(600, 7, 'normal')).toBe(false)
    expect(daysFitWithoutScroll(7 * MIN_DAY_WIDTH.compact, 7, 'compact')).toBe(true)
  })

  it('falls back to the minimum before the first measurement', () => {
    expect(dayColumnWidth(0, 7, 'normal')).toBe(MIN_DAY_WIDTH.normal)
  })

  it('a 1440 laptop with both rails collapsed fits a full week in compact density', () => {
    // 1440 − 250 sidebar − 80 gutters − 2×44 rails − 2×8 gaps − 44 axis ≈ 930px for the days.
    expect(daysFitWithoutScroll(930, 7, 'compact')).toBe(true)
    expect(dayColumnLevel(dayColumnWidth(930, 7, 'compact'))).toBe('narrow')
  })
})

describe('dayColumnLevel', () => {
  it('grades what a card can show by its column width', () => {
    expect(dayColumnLevel(260)).toBe('full')
    expect(dayColumnLevel(200)).toBe('full')
    expect(dayColumnLevel(160)).toBe('medium')
    expect(dayColumnLevel(120)).toBe('narrow')
  })
})

describe('boardFormat', () => {
  it('formats weights as a dispatcher reads them', () => {
    expect(formatWeightShort(11000)).toBe('11 t')
    expect(formatWeightShort(11500)).toBe('11,5 t')
    expect(formatWeightShort(850)).toBe('850 kg')
  })

  it('formats a route with a "?" for a missing end', () => {
    expect(formatRoute('Aachen', 'Düsseldorf')).toBe('Aachen → Düsseldorf')
    expect(formatRoute(null, 'Gent')).toBe('? → Gent')
    expect(formatRoute(null, null)).toBeNull()
  })
})
