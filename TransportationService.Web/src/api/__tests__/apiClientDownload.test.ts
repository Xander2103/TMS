import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiClient, fileNameFromDisposition } from '../apiClient'

/**
 * Regression for the receipt/export download flow: `downloadFile` must travel the same auth
 * path as JSON requests (bearer header, one refresh-and-retry on 401, ProblemDetails message
 * on failure) and honour the server's Content-Disposition file name. A raw `fetch` caller
 * used to skip the refresh, so a download after token expiry failed silently.
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

function pdfResponse(headers: Record<string, string> = {}): Response {
  return new Response(new Blob(['%PDF-1.4'], { type: 'application/pdf' }), { status: 200, headers })
}

function problemResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

describe('apiClient.downloadFile', () => {
  const fetchSpy = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
  let anchorDownloads: string[]
  let anchorHrefs: string[]

  beforeEach(() => {
    tokenStore.value = 'access-1'
    refreshSpy.mockReset()
    fetchSpy.mockReset()
    vi.stubGlobal('fetch', fetchSpy)
    // jsdom has no object URLs; record what the anchor would download instead of navigating.
    anchorDownloads = []
    anchorHrefs = []
    vi.stubGlobal('URL', Object.assign(URL, {
      createObjectURL: vi.fn(() => 'blob:mock-url'),
      revokeObjectURL: vi.fn(),
    }))
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      anchorDownloads.push(this.download)
      anchorHrefs.push(this.href)
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('sends the bearer token and hands the blob to the browser with the server file name', async () => {
    fetchSpy.mockResolvedValueOnce(pdfResponse({ 'Content-Disposition': 'attachment; filename="ontvangstbewijs-jan.pdf"' }))

    await apiClient.downloadFile('/api/employees/e1/issued-items/document', 'fallback.pdf')

    expect(fetchSpy).toHaveBeenCalledTimes(1)
    const [url, init] = fetchSpy.mock.calls[0]
    expect(String(url)).toMatch(/\/api\/employees\/e1\/issued-items\/document$/)
    expect(init?.method).toBe('GET')
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer access-1')
    // No JSON content type on a body-less GET.
    expect((init?.headers as Record<string, string>)['Content-Type']).toBeUndefined()
    expect(refreshSpy).not.toHaveBeenCalled()
    expect(anchorDownloads).toEqual(['ontvangstbewijs-jan.pdf'])
    expect(anchorHrefs).toEqual(['blob:mock-url'])
  })

  it('falls back to the caller-supplied name when the server sends no Content-Disposition', async () => {
    fetchSpy.mockResolvedValueOnce(pdfResponse())

    await apiClient.downloadFile('/api/x', 'fallback.pdf')

    expect(anchorDownloads).toEqual(['fallback.pdf'])
  })

  it('refreshes once on 401 and retries with the new token', async () => {
    fetchSpy
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(pdfResponse({ 'Content-Disposition': "attachment; filename*=UTF-8''ontvangstbewijs%20%C3%A9.pdf" }))
    refreshSpy.mockResolvedValueOnce({ accessToken: 'access-2', refreshToken: '' })

    await apiClient.downloadFile('/api/x', 'fallback.pdf')

    expect(refreshSpy).toHaveBeenCalledTimes(1)
    expect(fetchSpy).toHaveBeenCalledTimes(2)
    expect((fetchSpy.mock.calls[0][1]?.headers as Record<string, string>).Authorization).toBe('Bearer access-1')
    expect((fetchSpy.mock.calls[1][1]?.headers as Record<string, string>).Authorization).toBe('Bearer access-2')
    // RFC 5987 encoded name is decoded.
    expect(anchorDownloads).toEqual(['ontvangstbewijs é.pdf'])
  })

  it('gives up with a 401 ApiError when the refresh fails, without retrying', async () => {
    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 401 }))
    refreshSpy.mockResolvedValueOnce(null)

    await expect(apiClient.downloadFile('/api/x', 'fallback.pdf')).rejects.toMatchObject({ name: 'ApiError', status: 401 })
    expect(fetchSpy).toHaveBeenCalledTimes(1)
    expect(tokenStore.value).toBeNull()
    expect(anchorDownloads).toEqual([])
  })

  it('surfaces the ProblemDetails detail as the error message on a non-ok response', async () => {
    fetchSpy.mockResolvedValueOnce(problemResponse(400, { title: 'Bad Request', detail: 'Geen bedrijfsmiddelen om af te drukken.' }))

    const error = await apiClient.downloadFile('/api/x', 'fallback.pdf').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).message).toBe('Geen bedrijfsmiddelen om af te drukken.')
    expect(anchorDownloads).toEqual([])
  })

  it('falls back to a generic message when the error body is not JSON', async () => {
    fetchSpy.mockResolvedValueOnce(new Response('<html>oops</html>', { status: 500 }))

    const error = await apiClient.downloadFile('/api/x', 'fallback.pdf').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(500)
    expect((error as ApiError).message).toMatch(/failed with status 500/)
  })
})

describe('fileNameFromDisposition', () => {
  it('reads quoted, plain and RFC 5987 names', () => {
    expect(fileNameFromDisposition('attachment; filename="rapport.xlsx"')).toBe('rapport.xlsx')
    expect(fileNameFromDisposition('attachment; filename=rapport.xlsx')).toBe('rapport.xlsx')
    expect(fileNameFromDisposition("attachment; filename*=UTF-8''ontvangst%20%C3%A9.pdf")).toBe('ontvangst é.pdf')
    // Both forms present: the encoded one wins (it carries the full Unicode name).
    expect(fileNameFromDisposition("attachment; filename=\"fallback.pdf\"; filename*=UTF-8''echt%20bestand.pdf")).toBe('echt bestand.pdf')
  })

  it('returns null without a header or a name', () => {
    expect(fileNameFromDisposition(null)).toBeNull()
    expect(fileNameFromDisposition('inline')).toBeNull()
  })
})
