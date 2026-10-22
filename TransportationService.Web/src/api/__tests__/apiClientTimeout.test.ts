import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiClient, DEFAULT_TIMEOUT_MS, isTimeoutError, LONG_RUNNING_TIMEOUT_MS, TIMEOUT_MESSAGE } from '../apiClient'
import { isForbiddenError, isRetryableError } from '../notFound'

/**
 * PERF-H7: a hung API used to leave "… laden…" on screen forever (verified 35 s+). Every request
 * now has one deadline (30 s; 120 s for exports/documents) that ends in a RETRYABLE ApiError —
 * never in "Geen toegang", a validation error, or a bare AbortError the screens would swallow.
 */
const tokenStore = vi.hoisted(() => ({ value: 'access-1' as string | null }))
vi.mock('../../features/auth/authStorage', () => ({
  getAccessToken: () => tokenStore.value,
  setAccessToken: (token: string | null) => {
    tokenStore.value = token
  },
  clearTokens: () => {
    tokenStore.value = null
  },
}))
vi.mock('../../features/auth/authApi', () => ({ refresh: vi.fn() }))

const json = (status: number, body: unknown = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** A fetch that never answers but does honour its abort signal, like a browser does. */
function hangingFetch(): (input: RequestInfo | URL, init?: RequestInit) => Promise<Response> {
  return (_input, init) =>
    new Promise<Response>((_, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
    })
}

/** A fetch that never answers AND ignores its abort signal (a misbehaving runtime). */
const deafFetch = () => new Promise<Response>(() => undefined)

describe('apiClient timeout policy (PERF-H7)', () => {
  const fetchSpy = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()

  beforeEach(() => {
    vi.useFakeTimers()
    tokenStore.value = 'access-1'
    fetchSpy.mockReset()
    vi.stubGlobal('fetch', fetchSpy)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('rejects a never-answering GET with a retryable 408 "timeout" ApiError after 30 s', async () => {
    fetchSpy.mockImplementation(hangingFetch())
    const pending = apiClient.getJson('/api/customers')
    const outcome = pending.then(
      () => 'resolved',
      (err: unknown) => err,
    )

    // Just under the deadline nothing has happened yet.
    await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS - 1)
    expect(fetchSpy).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)

    const err = await outcome
    expect(err).toBeInstanceOf(ApiError)
    const apiError = err as ApiError
    expect(apiError.status).toBe(408)
    expect(apiError.code).toBe('timeout')
    expect(apiError.message).toBe(TIMEOUT_MESSAGE)
    expect(isTimeoutError(apiError)).toBe(true)
    expect(isRetryableError(apiError)).toBe(true)
    expect(isForbiddenError(apiError)).toBe(false)
    // The deadline also aborted the underlying request.
    expect((fetchSpy.mock.calls[0][1]?.signal as AbortSignal).aborted).toBe(true)
  })

  it('applies the same deadline to mutations', async () => {
    fetchSpy.mockImplementation(hangingFetch())
    const outcome = apiClient.postJson('/api/incidents', { title: 'x' }).catch((err: unknown) => err)
    await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS)
    expect(await outcome).toMatchObject({ status: 408, code: 'timeout' })
  })

  it('honours a per-call timeoutMs override', async () => {
    fetchSpy.mockImplementation(hangingFetch())
    const outcome = apiClient.getJson('/api/slow', { timeoutMs: 5_000 }).catch((err: unknown) => err)
    await vi.advanceTimersByTimeAsync(4_999)
    let settled = false
    void outcome.then(() => {
      settled = true
    })
    await Promise.resolve()
    expect(settled).toBe(false)
    await vi.advanceTimersByTimeAsync(1)
    expect(await outcome).toMatchObject({ status: 408, code: 'timeout' })
  })

  it('still times out when the runtime ignores the abort signal', async () => {
    fetchSpy.mockImplementation(deafFetch)
    const outcome = apiClient.getJson('/api/customers').catch((err: unknown) => err)
    await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS)
    expect(await outcome).toMatchObject({ status: 408, code: 'timeout' })
  })

  it('surfaces a caller-initiated abort as an AbortError, never as a timeout', async () => {
    fetchSpy.mockImplementation(hangingFetch())
    const controller = new AbortController()
    const outcome = apiClient.getJson('/api/customers', { signal: controller.signal }).catch((err: unknown) => err)
    await vi.advanceTimersByTimeAsync(1_000)
    controller.abort()
    const err = await outcome
    expect(err).toBeInstanceOf(DOMException)
    expect((err as DOMException).name).toBe('AbortError')
    expect(isTimeoutError(err)).toBe(false)
  })

  it('does not fire the deadline after a successful answer', async () => {
    fetchSpy.mockResolvedValueOnce(json(200, { ok: true }))
    await expect(apiClient.getJson('/api/customers')).resolves.toEqual({ ok: true })
    // No stray rejection when the (cleared) timer would have fired.
    await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS * 2)
  })

  it('leaves 403 and 409 exactly as before (status, message, body)', async () => {
    fetchSpy.mockResolvedValueOnce(json(403, { detail: 'Geen toegang tot deze rit.' }))
    const forbidden = await apiClient.getJson('/api/trips/1').catch((err: unknown) => err)
    expect(forbidden).toMatchObject({ status: 403, message: 'Geen toegang tot deze rit.' })
    expect(isForbiddenError(forbidden)).toBe(true)
    expect(isRetryableError(forbidden)).toBe(false)

    const conflictBody = { message: 'Versie verouderd.', staleVersion: true, conflicts: [{ code: 'x' }] }
    fetchSpy.mockResolvedValueOnce(json(409, conflictBody))
    const conflict = await apiClient.postJson('/api/trips/1/status', {}).catch((err: unknown) => err)
    expect(conflict).toMatchObject({ status: 409, message: 'Versie verouderd.', body: conflictBody })
    expect(isTimeoutError(conflict)).toBe(false)
  })

  it('reports an unreachable server as "Unable to reach", not as a timeout', async () => {
    fetchSpy.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const err = await apiClient.getJson('/api/customers').catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).message).toBe('Unable to reach /api/customers')
    expect((err as ApiError).status).toBeUndefined()
  })

  it('gives downloads the long-running deadline (120 s) and still times out after it', async () => {
    fetchSpy.mockImplementation(hangingFetch())
    const outcome = apiClient.fetchBlob('/api/kpi/export?report=fuel').catch((err: unknown) => err)
    await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS)
    let settled = false
    void outcome.then(() => {
      settled = true
    })
    await Promise.resolve()
    expect(settled).toBe(false)
    await vi.advanceTimersByTimeAsync(LONG_RUNNING_TIMEOUT_MS - DEFAULT_TIMEOUT_MS)
    expect(await outcome).toMatchObject({ status: 408, code: 'timeout' })
  })

  it('getJsonWithMeta hands back the headers so callers can read X-Result-Truncated', async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(JSON.stringify([{ id: 't-1' }]), {
        status: 200,
        headers: { 'Content-Type': 'application/json', 'X-Result-Truncated': 'true', 'X-Result-Limit': '500' },
      }),
    )
    const { data, headers, status } = await apiClient.getJsonWithMeta<{ id: string }[]>('/api/trips')
    expect(data).toEqual([{ id: 't-1' }])
    expect(status).toBe(200)
    expect(headers.get('X-Result-Truncated')).toBe('true')
    expect(headers.get('X-Result-Limit')).toBe('500')
  })
})
