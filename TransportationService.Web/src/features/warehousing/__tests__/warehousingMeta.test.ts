import { describe, expect, it } from 'vitest'
import { DOCK_STATUS_LABELS, DOCK_STATUS_TONE, dockBlockPosition } from '../types'

describe('dockBlockPosition', () => {
  it('positions a block within the operating window', () => {
    const pos = dockBlockPosition('2026-07-20T08:00:00', '2026-07-20T10:00:00', '06:00:00', '20:00:00')
    // 08:00 is 120 minutes into a 840-minute window.
    expect(pos.leftPct).toBeCloseTo((120 / 840) * 100, 5)
    expect(pos.widthPct).toBeCloseTo((120 / 840) * 100, 5)
  })

  it('clamps to the window and keeps a minimum width', () => {
    const early = dockBlockPosition('2026-07-20T04:00:00', '2026-07-20T05:00:00', '06:00:00', '20:00:00')
    expect(early.leftPct).toBe(0)
    expect(early.widthPct).toBeGreaterThanOrEqual(2)
  })

  it('falls back to 06:00-20:00 without configured hours', () => {
    const pos = dockBlockPosition('2026-07-20T06:00:00', '2026-07-20T20:00:00', null, null)
    expect(pos.leftPct).toBe(0)
    expect(pos.widthPct).toBeCloseTo(100, 5)
  })
})

describe('dock status meta', () => {
  it('labels and tones cover every status', () => {
    expect(Object.keys(DOCK_STATUS_LABELS)).toHaveLength(9)
    expect(Object.keys(DOCK_STATUS_TONE)).toHaveLength(9)
    expect(DOCK_STATUS_TONE.NoShow).toBe('danger')
    expect(DOCK_STATUS_TONE.Completed).toBe('success')
  })
})
