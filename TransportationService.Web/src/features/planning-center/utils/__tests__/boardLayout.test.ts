import { describe, expect, it } from 'vitest'
import { DEFAULT_LAYOUT, parseStoredLayout } from '../boardLayout'

describe('parseStoredLayout', () => {
  it('restores a stored layout', () => {
    expect(parseStoredLayout(JSON.stringify({ density: 'compact', leftCollapsed: true, rightCollapsed: false, days: 7 })))
      .toEqual({ density: 'compact', leftCollapsed: true, rightCollapsed: false, days: 7 })
  })

  it('falls back field by field on unknown or corrupt values', () => {
    expect(parseStoredLayout(null)).toEqual(DEFAULT_LAYOUT)
    expect(parseStoredLayout('not json')).toEqual(DEFAULT_LAYOUT)
    expect(parseStoredLayout(JSON.stringify({ density: 'huge', days: 5, leftCollapsed: 'yes' })))
      .toEqual({ density: 'normal', leftCollapsed: false, rightCollapsed: false, days: 3 })
  })
})
