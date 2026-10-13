import { afterEach, describe, expect, it } from 'vitest'
import { bodyScrollLockCount, lockBodyScroll } from '../bodyScrollLock'

/**
 * The body is the app scroller, so a lock that is not released exactly once leaves the page
 * stuck (root cause of the "cannot scroll to the bottom" report, sprint 2026-09-21).
 */
describe('lockBodyScroll', () => {
  afterEach(() => {
    document.body.style.overflow = ''
  })

  it('locks the body and restores the ORIGINAL value, not an empty string', () => {
    document.body.style.overflow = 'auto'
    const release = lockBodyScroll()
    expect(document.body.style.overflow).toBe('hidden')
    release()
    expect(document.body.style.overflow).toBe('auto')
    expect(bodyScrollLockCount()).toBe(0)
  })

  it('keeps the body locked until the last nested lock is released', () => {
    const outer = lockBodyScroll()
    const inner = lockBodyScroll()
    expect(bodyScrollLockCount()).toBe(2)
    inner()
    expect(document.body.style.overflow).toBe('hidden')
    outer()
    expect(document.body.style.overflow).toBe('')
  })

  it('survives an out-of-order release (outer overlay unmounts before the inner one)', () => {
    document.body.style.overflow = 'scroll'
    const outer = lockBodyScroll()
    const inner = lockBodyScroll()
    outer()
    expect(document.body.style.overflow).toBe('hidden')
    inner()
    expect(document.body.style.overflow).toBe('scroll')
  })

  it('ignores a double release, so one overlay can never unlock another', () => {
    const first = lockBodyScroll()
    const second = lockBodyScroll()
    first()
    first()
    expect(bodyScrollLockCount()).toBe(1)
    expect(document.body.style.overflow).toBe('hidden')
    second()
    expect(bodyScrollLockCount()).toBe(0)
    expect(document.body.style.overflow).toBe('')
  })

  it('re-reads the original value for every new lock cycle', () => {
    lockBodyScroll()()
    document.body.style.overflow = 'clip'
    const release = lockBodyScroll()
    release()
    expect(document.body.style.overflow).toBe('clip')
  })
})
