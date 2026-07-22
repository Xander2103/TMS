import { beforeEach, describe, expect, it } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useExpandedModules } from '../useExpandedModules'

describe('useExpandedModules', () => {
  beforeEach(() => window.localStorage.clear())

  it('expands the active module by default when nothing is stored', () => {
    const { result } = renderHook(() => useExpandedModules('u1', 'vloot'))
    expect(result.current.isExpanded('vloot')).toBe(true)
    expect(result.current.isExpanded('klanten')).toBe(false)
  })

  it('toggles a module and persists to a per-user key', () => {
    const { result } = renderHook(() => useExpandedModules('u1', 'vloot'))
    act(() => result.current.toggle('klanten'))
    expect(result.current.isExpanded('klanten')).toBe(true)
    const stored = JSON.parse(window.localStorage.getItem('nav.expanded.u1.v1')!)
    expect(stored).toContain('klanten')
  })

  it('restores stored state instead of the active default', () => {
    window.localStorage.setItem('nav.expanded.u2.v1', JSON.stringify(['beheer']))
    const { result } = renderHook(() => useExpandedModules('u2', 'vloot'))
    expect(result.current.isExpanded('beheer')).toBe(true)
    // Active module still auto-expands on top of stored state.
    expect(result.current.isExpanded('vloot')).toBe(true)
  })

  it('keeps separate state per user id', () => {
    window.localStorage.setItem('nav.expanded.u1.v1', JSON.stringify(['klanten']))
    const { result } = renderHook(() => useExpandedModules('u2', null))
    expect(result.current.isExpanded('klanten')).toBe(false)
  })

  it('re-seeds from the new user key when the user id changes, without clobbering the previous user', () => {
    window.localStorage.setItem('nav.expanded.u1.v1', JSON.stringify(['klanten']))
    window.localStorage.setItem('nav.expanded.u2.v1', JSON.stringify(['beheer']))
    const { result, rerender } = renderHook(({ uid }: { uid: string }) => useExpandedModules(uid, null), {
      initialProps: { uid: 'u1' },
    })
    expect(result.current.isExpanded('klanten')).toBe(true)

    rerender({ uid: 'u2' })
    expect(result.current.isExpanded('beheer')).toBe(true)
    expect(result.current.isExpanded('klanten')).toBe(false)
    // u1's stored state must survive u2 mounting on the same browser.
    expect(JSON.parse(window.localStorage.getItem('nav.expanded.u1.v1')!)).toEqual(['klanten'])
  })
})
