import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/apiClient'
import { isRetryableError } from '../../api/notFound'
import type { PagedResult } from '../../api/types'
import { usePagedQuery, type PagedQueryArgs } from '../usePagedQuery'

/**
 * PERF-H7: the paged-list hook aborts a superseded or unmounted request (it used to merely drop
 * the answer) and a timed-out request ends the loading state with a retryable error.
 */
interface Deferred {
  args: PagedQueryArgs
  resolve: (value: PagedResult<string>) => void
  reject: (error: unknown) => void
}

function page(items: string[], totalCount = items.length): PagedResult<string> {
  return { items, totalCount, page: 1, pageSize: 25 }
}

describe('usePagedQuery', () => {
  const calls: Deferred[] = []
  const fetcher = vi.fn((args: PagedQueryArgs) => {
    return new Promise<PagedResult<string>>((resolve, reject) => {
      calls.push({ args, resolve, reject })
    })
  })

  beforeEach(() => {
    vi.useFakeTimers()
    calls.length = 0
    fetcher.mockClear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('hands the fetcher an AbortSignal and aborts a superseded request', async () => {
    const { result, rerender } = renderHook(({ search }) => usePagedQuery(fetcher, { search, page: 1, debounceMs: 0 }), {
      initialProps: { search: '' },
    })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(calls).toHaveLength(1)
    expect(calls[0].args.signal).toBeInstanceOf(AbortSignal)
    expect(calls[0].args.signal.aborted).toBe(false)
    expect(result.current.isLoading).toBe(true)

    rerender({ search: 'jan' })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(calls).toHaveLength(2)
    expect(calls[0].args.signal.aborted).toBe(true)
    expect(calls[1].args.search).toBe('jan')

    // A late answer of the superseded request changes nothing; the current one lands.
    await act(async () => {
      calls[0].resolve(page(['stale']))
      await Promise.resolve()
    })
    expect(result.current.items).toEqual([])
    await act(async () => {
      calls[1].resolve(page(['Jan Peeters'], 1))
      await Promise.resolve()
    })
    expect(result.current.items).toEqual(['Jan Peeters'])
    expect(result.current.totalCount).toBe(1)
    expect(result.current.isLoading).toBe(false)
  })

  it('aborts the in-flight request on unmount', async () => {
    const { unmount } = renderHook(() => usePagedQuery(fetcher, { search: '', page: 1, debounceMs: 0 }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(calls).toHaveLength(1)
    unmount()
    expect(calls[0].args.signal.aborted).toBe(true)
  })

  it('ends the loading state with a retryable error when the request times out, and reload works', async () => {
    const { result } = renderHook(() =>
      usePagedQuery(fetcher, { search: '', page: 1, debounceMs: 0, errorMessage: 'Klanten konden niet worden geladen.' }),
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current.isLoading).toBe(true)

    const timeout = new ApiError('De server reageert niet. Probeer het opnieuw.', 408, { code: 'timeout', transient: true })
    await act(async () => {
      calls[0].reject(timeout)
      await Promise.resolve()
    })
    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBe('Klanten konden niet worden geladen.')
    expect(result.current.errorCause).toBe(timeout)
    expect(isRetryableError(result.current.errorCause)).toBe(true)

    // "Opnieuw proberen": a fresh request with a fresh signal, loading again until it answers.
    act(() => result.current.reload())
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(calls).toHaveLength(2)
    expect(result.current.isLoading).toBe(true)
    await act(async () => {
      calls[1].resolve(page(['Acme'], 1))
      await Promise.resolve()
    })
    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBeNull()
    expect(result.current.items).toEqual(['Acme'])
  })
})
