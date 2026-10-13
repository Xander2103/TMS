import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient, setUnauthorizedHandler } from '../apiClient'

/**
 * B01: a 401 whose refresh cannot be judged (rate limit, server error, network) must not sign
 * the user out. Only a REJECTED refresh credential does.
 */
const tokenStore = vi.hoisted(() => ({ value: 'access-1' as string | null }))
const refreshSpy = vi.hoisted(() => vi.fn())

vi.mock('../../features/auth/authStorage', () => ({
  getAccessToken: () => tokenStore.value,
  setAccessToken: (token: string | null) => {
    tokenStore.value = token
  },
  clearTokens: () => {
    tokenStore.value = null
  },
}))

vi.mock('../../features/auth/authApi', () => ({
  refresh: refreshSpy,
}))

const json = (status: number, body: unknown = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('apiClient refresh on 401 (B01)', () => {
  const fetchSpy = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
  const unauthorized = vi.fn()

  beforeEach(() => {
    tokenStore.value = 'access-1'
    refreshSpy.mockReset()
    fetchSpy.mockReset()
    unauthorized.mockReset()
    vi.stubGlobal('fetch', fetchSpy)
    setUnauthorizedHandler(unauthorized)
  })

  afterEach(() => {
    setUnauthorizedHandler(null)
    vi.unstubAllGlobals()
  })

  it('keeps the session and surfaces a transient error when the refresh is rate-limited', async () => {
    fetchSpy.mockResolvedValueOnce(json(401))
    refreshSpy.mockResolvedValueOnce({ status: 'unavailable', reason: 'rate_limited', httpStatus: 429 })

    await expect(apiClient.getJson('/api/dossiers')).rejects.toMatchObject({
      name: 'ApiError',
      status: 429,
      code: 'service_unavailable',
    })
    expect(unauthorized).not.toHaveBeenCalled()
    expect(tokenStore.value).toBe('access-1')
    expect(fetchSpy).toHaveBeenCalledTimes(1)
  })

  it('keeps the session when the refresh endpoint is unreachable or returns 5xx', async () => {
    fetchSpy.mockResolvedValueOnce(json(401))
    refreshSpy.mockResolvedValueOnce({ status: 'unavailable', reason: 'network' })
    await expect(apiClient.getJson('/api/dossiers')).rejects.toMatchObject({ code: 'service_unavailable', status: 503 })

    fetchSpy.mockResolvedValueOnce(json(401))
    refreshSpy.mockResolvedValueOnce({ status: 'unavailable', reason: 'server', httpStatus: 502 })
    await expect(apiClient.getJson('/api/dossiers')).rejects.toMatchObject({ code: 'service_unavailable', status: 502 })

    expect(unauthorized).not.toHaveBeenCalled()
    expect(tokenStore.value).toBe('access-1')
  })

  it('retries once with the new token when the refresh succeeds', async () => {
    fetchSpy.mockResolvedValueOnce(json(401)).mockResolvedValueOnce(json(200, { ok: true }))
    refreshSpy.mockResolvedValueOnce({ status: 'ok', tokens: { accessToken: 'access-2' } })

    await expect(apiClient.getJson('/api/dossiers')).resolves.toEqual({ ok: true })
    expect((fetchSpy.mock.calls[1][1]?.headers as Record<string, string>).Authorization).toBe('Bearer access-2')
    expect(unauthorized).not.toHaveBeenCalled()
  })

  it('signs the user out only when the refresh credential is rejected', async () => {
    fetchSpy.mockResolvedValueOnce(json(401))
    refreshSpy.mockResolvedValueOnce({ status: 'unauthenticated' })

    await expect(apiClient.getJson('/api/dossiers')).rejects.toMatchObject({ status: 401 })
    expect(unauthorized).toHaveBeenCalledTimes(1)
    expect(tokenStore.value).toBeNull()
  })
})
