import { afterEach, describe, expect, it, vi } from 'vitest'
import { refresh } from '../authApi'

/** B01: every refresh failure is classified; only a rejected credential means "not signed in". */
describe('authApi.refresh outcome classification', () => {
  afterEach(() => vi.unstubAllGlobals())

  const withResponse = (status: number, body: unknown = {}) =>
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status })))

  it('returns the tokens on 200', async () => {
    withResponse(200, { accessToken: 'a', user: { id: 'u' } })
    await expect(refresh()).resolves.toMatchObject({ status: 'ok', tokens: { accessToken: 'a' } })
  })

  it('treats 401, 403 and 400 as a rejected credential', async () => {
    for (const status of [401, 403, 400]) {
      withResponse(status)
      await expect(refresh()).resolves.toEqual({ status: 'unauthenticated' })
    }
  })

  it('treats 429 as a rate limit, 5xx as a server problem and a thrown fetch as a network problem', async () => {
    withResponse(429)
    await expect(refresh()).resolves.toEqual({ status: 'unavailable', reason: 'rate_limited', httpStatus: 429 })
    withResponse(503)
    await expect(refresh()).resolves.toEqual({ status: 'unavailable', reason: 'server', httpStatus: 503 })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch') }))
    await expect(refresh()).resolves.toEqual({ status: 'unavailable', reason: 'network' })
  })
})
