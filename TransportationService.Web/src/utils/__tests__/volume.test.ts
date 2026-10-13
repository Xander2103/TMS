import { describe, expect, it } from 'vitest'
import { computeVolumeM3 } from '../volume'

describe('computeVolumeM3', () => {
  it('multiplies complete positive dimensions, rounded to 3 decimals', () => {
    expect(computeVolumeM3(13.6, 2.48, 2.7)).toBe(91.066)
    expect(computeVolumeM3(2, 2, 2)).toBe(8)
  })

  it('returns null for incomplete or non-positive dimensions', () => {
    expect(computeVolumeM3(13.6, null, 2.7)).toBeNull()
    expect(computeVolumeM3(null, null, null)).toBeNull()
    expect(computeVolumeM3(0, 2, 2)).toBeNull()
    expect(computeVolumeM3(-1, 2, 2)).toBeNull()
  })
})
