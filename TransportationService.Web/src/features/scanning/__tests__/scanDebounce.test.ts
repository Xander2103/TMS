import { describe, expect, it } from 'vitest'
import { createScanDebouncer } from '../scanDebounce'

describe('createScanDebouncer', () => {
  it('accepts the first read and suppresses repeats inside the window', () => {
    let now = 1000
    const debouncer = createScanDebouncer(2000, () => now)

    expect(debouncer.accept('A')).toBe(true)
    now += 500
    expect(debouncer.accept('A')).toBe(false)
    now += 1600
    expect(debouncer.accept('A')).toBe(true)
  })

  it('accepts a different value immediately', () => {
    let now = 1000
    const debouncer = createScanDebouncer(2000, () => now)

    expect(debouncer.accept('A')).toBe(true)
    now += 100
    expect(debouncer.accept('B')).toBe(true)
    now += 100
    expect(debouncer.accept('A')).toBe(true)
  })

  it('reset clears the memory', () => {
    let now = 1000
    const debouncer = createScanDebouncer(2000, () => now)

    expect(debouncer.accept('A')).toBe(true)
    debouncer.reset()
    now += 10
    expect(debouncer.accept('A')).toBe(true)
  })
})
