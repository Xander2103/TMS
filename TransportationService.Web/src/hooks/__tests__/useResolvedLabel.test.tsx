import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useResolvedLabel, type LabelResolver } from '../useResolvedLabel'

/** The label of an id a screen only knows as an id (URL filter, stored reference), fetched once. */
describe('useResolvedLabel', () => {
  it('resolves the label once per id and prefers a label the host already knows', async () => {
    const resolve = vi.fn<LabelResolver>(async (id) => `Naam ${id}`)
    const { result, rerender } = renderHook(({ id, known }) => useResolvedLabel(id, resolve, known), {
      initialProps: { id: 'c-1' as string | null, known: undefined as string | undefined },
    })

    expect(result.current).toBeUndefined()
    await waitFor(() => expect(result.current).toBe('Naam c-1'))
    expect(resolve).toHaveBeenCalledTimes(1)

    // Same id again: no second call. A known label short-circuits the lookup entirely.
    rerender({ id: 'c-1', known: undefined })
    expect(resolve).toHaveBeenCalledTimes(1)
    rerender({ id: 'c-2', known: 'Bekende klant' })
    expect(result.current).toBe('Bekende klant')
    expect(resolve).toHaveBeenCalledTimes(1)

    rerender({ id: null, known: undefined })
    expect(result.current).toBeUndefined()
  })

  it('stays undefined when the lookup fails (never a raw id)', async () => {
    const resolve = vi.fn<LabelResolver>(async () => {
      throw new Error('404')
    })
    const { result } = renderHook(() => useResolvedLabel('c-9', resolve))
    await act(async () => {
      await Promise.resolve()
    })
    await waitFor(() => expect(resolve).toHaveBeenCalledTimes(1))
    expect(result.current).toBeUndefined()
  })
})
