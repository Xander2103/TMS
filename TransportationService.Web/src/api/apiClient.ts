import { apiBaseUrl } from '../config/env'
import { getAccessToken, setAccessToken, clearTokens } from '../features/auth/authStorage'
import { refresh as refreshSession, type RefreshOutcome } from '../features/auth/authApi'
import { extractFieldErrors, type FieldErrors } from './problemDetails'

export class ApiError extends Error {
  readonly status?: number
  /** Parsed JSON error body, when the server sent one (e.g. 409 conflict payloads). */
  readonly body?: unknown
  /** Per-field validation messages (normalised paths), from the ProblemDetails "errors" dictionary. */
  readonly fieldErrors: FieldErrors
  /**
   * Stabiele machineleesbare foutcode (i18n-wave): `code`-veld in anonieme bodies of
   * ProblemDetails-extensions. Frontendlogica en vertaling branchen HIEROP — nooit op
   * de (Nederlandse) message-tekst.
   */
  readonly code?: string

  constructor(message: string, status?: number, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
    this.fieldErrors = extractFieldErrors(body)
    this.code =
      typeof body === 'object' && body !== null && 'code' in body && typeof (body as { code: unknown }).code === 'string'
        ? (body as { code: string }).code
        : undefined
  }
}

export interface RequestOptions {
  signal?: AbortSignal
  /**
   * PERF-H7: deadline for one attempt at the server (a 401 refresh-and-retry gets a fresh one).
   * Defaults to {@link DEFAULT_TIMEOUT_MS}; known long-running endpoints (exports, PDF/ZIP
   * downloads) pass {@link LONG_RUNNING_TIMEOUT_MS}. On expiry the call rejects with an
   * {@link ApiError} 408 / code `timeout` (retryable), never with a bare AbortError.
   */
  timeoutMs?: number
}

/** Response payload plus the headers, for callers that read server hints (X-Result-Truncated, …). */
export interface JsonWithMeta<T> {
  data: T
  headers: Headers
  status: number
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/**
 * PERF-H7 timeout policy — ONE place. Every request (reads and mutations) gives up after 30 s:
 * a hung API used to leave "… laden…" on screen forever. Exports and document downloads are
 * legitimately slower and get 120 s.
 */
export const DEFAULT_TIMEOUT_MS = 30_000
export const LONG_RUNNING_TIMEOUT_MS = 120_000
export const TIMEOUT_ERROR_CODE = 'timeout'
/** User-facing message of a timeout (Dutch source text; `errors.codes.timeout` localises it). */
export const TIMEOUT_MESSAGE = 'De server reageert niet. Probeer het opnieuw.'

/** True when a call ended because OUR deadline expired (not because the caller aborted it). */
export function isTimeoutError(error: unknown): boolean {
  return error instanceof ApiError && error.code === TIMEOUT_ERROR_CODE
}

function timeoutError(): ApiError {
  // 408 + transient: isRetryableError() is true, so ErrorState/DataTable offer "Opnieuw proberen".
  return new ApiError(TIMEOUT_MESSAGE, 408, { code: TIMEOUT_ERROR_CODE, transient: true })
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

interface Deadline {
  /** Aborts when the caller's signal aborts OR the deadline expires. */
  signal: AbortSignal
  /** Rejects with the timeout error when the deadline expires (never resolves). */
  expired: Promise<never>
  /** True once the deadline fired — distinguishes our timeout from a caller-initiated abort. */
  readonly timedOut: boolean
  /** Stops the timer and detaches listeners; safe to call more than once. */
  dispose: () => void
}

function combineSignals(caller: AbortSignal, own: AbortSignal): { signal: AbortSignal; detach: () => void } {
  const any = (AbortSignal as unknown as { any?: (signals: AbortSignal[]) => AbortSignal }).any
  if (typeof any === 'function') {
    return { signal: any.call(AbortSignal, [caller, own]), detach: () => undefined }
  }
  // Manual fallback for runtimes without AbortSignal.any.
  const combined = new AbortController()
  const onCaller = () => combined.abort(caller.reason)
  const onOwn = () => combined.abort(own.reason)
  if (caller.aborted) {
    combined.abort(caller.reason)
  } else {
    caller.addEventListener('abort', onCaller, { once: true })
    own.addEventListener('abort', onOwn, { once: true })
  }
  return {
    signal: combined.signal,
    detach: () => {
      caller.removeEventListener('abort', onCaller)
      own.removeEventListener('abort', onOwn)
    },
  }
}

function startDeadline(callerSignal: AbortSignal | undefined, timeoutMs: number): Deadline {
  const own = new AbortController()
  let timedOut = false
  let expire: () => void = () => undefined
  const expired = new Promise<never>((_, reject) => {
    expire = () => reject(timeoutError())
  })
  // Nothing may be racing it when it fires (e.g. the body is already being read): never unhandled.
  expired.catch(() => undefined)
  const timer = setTimeout(() => {
    timedOut = true
    own.abort()
    expire()
  }, timeoutMs)
  const combined = callerSignal ? combineSignals(callerSignal, own.signal) : { signal: own.signal, detach: () => undefined }
  return {
    signal: combined.signal,
    expired,
    get timedOut() {
      return timedOut
    },
    dispose: () => {
      clearTimeout(timer)
      combined.detach()
    },
  }
}

interface Attempt {
  response: Response
  /** True when the deadline expired (also while the body is still being read). */
  readonly timedOut: boolean
  dispose: () => void
}

/**
 * One round trip under the timeout policy. Transport failures become `ApiError` ("Unable to
 * reach"), our own deadline becomes the timeout error, and an abort by the CALLER stays the
 * AbortError it always was (callers test for it to ignore superseded requests).
 */
async function send(path: string, init: RequestInit, options?: RequestOptions): Promise<Attempt> {
  const deadline = startDeadline(options?.signal, options?.timeoutMs ?? DEFAULT_TIMEOUT_MS)
  try {
    const response = await Promise.race([fetch(`${apiBaseUrl}${path}`, { ...init, signal: deadline.signal }), deadline.expired])
    return {
      response,
      get timedOut() {
        return deadline.timedOut
      },
      dispose: deadline.dispose,
    }
  } catch (err) {
    deadline.dispose()
    if (deadline.timedOut) throw timeoutError()
    if (isAbortError(err) || options?.signal?.aborted) throw err
    throw new ApiError(`Unable to reach ${path}`)
  }
}

/** Reads the body under the same deadline: an abort by the expired timer is a timeout, not a parse error. */
async function readBody<T>(attempt: Attempt, read: () => Promise<T>): Promise<T> {
  try {
    return await read()
  } catch (err) {
    if (attempt.timedOut) throw timeoutError()
    throw err
  }
}

// Called when authentication cannot be recovered (refresh failed). The AuthProvider registers a
// handler that clears state and redirects to the login page.
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler
}

// Single-flight refresh: concurrent 401s share one refresh attempt instead of stampeding.
let refreshInFlight: Promise<RefreshOutcome> | null = null

function attemptRefresh(): Promise<RefreshOutcome> {
  refreshInFlight ??= (async () => {
    // H5: the refresh credential is the HttpOnly cookie — nothing to read locally.
    const outcome = await refreshSession()
    if (outcome.status === 'ok') setAccessToken(outcome.tokens.accessToken)
    return outcome
  })().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

/** User-facing message when the server cannot be reached for a refresh (session is kept). */
export const SERVICE_UNAVAILABLE_MESSAGE = 'De server is tijdelijk niet bereikbaar. Probeer het zo meteen opnieuw.'

/**
 * B01: a 401 whose refresh could not even be judged (429 rate limit, 5xx, network) is NOT an
 * authentication failure. The caller gets a transient error to show with a retry; the access
 * token, the user and the page all stay. Only a rejected credential signs the user out.
 */
function unavailableError(outcome: Extract<RefreshOutcome, { status: 'unavailable' }>): ApiError {
  return new ApiError(SERVICE_UNAVAILABLE_MESSAGE, outcome.httpStatus ?? 503, {
    code: 'service_unavailable',
    transient: true,
    reason: outcome.reason,
  })
}

function buildHeaders(hasBody: boolean): Record<string, string> {
  const headers: Record<string, string> = {}
  if (hasBody) headers['Content-Type'] = 'application/json'
  const token = getAccessToken()
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

/** Surface the backend's user-facing message (ProblemDetails `detail` or `{ message }`). */
async function failureFrom(attempt: Attempt, path: string, keepBody: boolean): Promise<ApiError> {
  const { response } = attempt
  let serverMessage: string | null = null
  let errorBody: unknown
  try {
    const data = (await response.json()) as { detail?: unknown; message?: unknown }
    errorBody = data
    if (typeof data.detail === 'string') serverMessage = data.detail
    else if (typeof data.message === 'string') serverMessage = data.message
  } catch {
    // Body was empty or not JSON — fall back to the generic message.
  }
  return new ApiError(
    serverMessage ?? `Request to ${path} failed with status ${response.status}`,
    response.status,
    keepBody ? errorBody : undefined,
  )
}

/**
 * Sends the request with a bearer token and one refresh-and-retry on 401. The returned attempt
 * still owns its deadline: the caller reads the body through {@link readBody} and disposes it.
 */
async function sendAuthenticated(path: string, init: () => RequestInit, options?: RequestOptions): Promise<Attempt> {
  let attempt = await send(path, init(), options)
  if (attempt.response.status === 401) {
    attempt.dispose()
    const outcome = await attemptRefresh()
    if (outcome.status === 'unavailable') throw unavailableError(outcome)
    if (outcome.status === 'ok') attempt = await send(path, init(), options)
    if (attempt.response.status === 401) {
      attempt.dispose()
      clearTokens()
      onUnauthorized?.()
      throw new ApiError('Authentication required', 401)
    }
  }
  return attempt
}

async function requestWithMeta<T>(method: Method, path: string, body?: unknown, options?: RequestOptions): Promise<JsonWithMeta<T>> {
  const hasBody = body !== undefined
  const serialized = hasBody ? JSON.stringify(body) : undefined
  const init = (): RequestInit => ({ method, headers: buildHeaders(hasBody), body: serialized })

  const attempt = await sendAuthenticated(path, init, options)
  try {
    const { response } = attempt
    if (!response.ok) throw await failureFrom(attempt, path, true)
    if (response.status === 204) return { data: undefined as T, headers: response.headers, status: response.status }
    const data = await readBody(attempt, () => response.json() as Promise<T>)
    return { data, headers: response.headers, status: response.status }
  } finally {
    attempt.dispose()
  }
}

async function request<T>(method: Method, path: string, body?: unknown, options?: RequestOptions): Promise<T> {
  return (await requestWithMeta<T>(method, path, body, options)).data
}

async function getJson<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('GET', path, undefined, options)
}

/**
 * GET that also hands back the response headers, for list endpoints that signal a server-side
 * cap (`X-Result-Truncated: true`, `X-Result-Limit: 500`) so the screen can say so instead of
 * silently showing a partial list.
 */
async function getJsonWithMeta<T>(path: string, options?: RequestOptions): Promise<JsonWithMeta<T>> {
  return requestWithMeta<T>('GET', path, undefined, options)
}

async function postJson<TResponse, TBody>(path: string, body: TBody, options?: RequestOptions): Promise<TResponse> {
  return request<TResponse>('POST', path, body, options)
}

async function putJson<TResponse, TBody>(path: string, body: TBody, options?: RequestOptions): Promise<TResponse> {
  return request<TResponse>('PUT', path, body, options)
}

async function patchJson<TResponse, TBody>(path: string, body: TBody, options?: RequestOptions): Promise<TResponse> {
  return request<TResponse>('PATCH', path, body, options)
}

async function deleteRequest(path: string, options?: RequestOptions): Promise<void> {
  await request<void>('DELETE', path, undefined, options)
}

/** DELETE that returns a response body (e.g. the refreshed aggregate after removing a child). */
async function deleteJson<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('DELETE', path, undefined, options)
}

/** Reads the file name from a Content-Disposition header (quoted, plain or RFC 5987 `filename*`). */
export function fileNameFromDisposition(header: string | null): string | null {
  if (!header) return null
  const encoded = /filename\*=(?:UTF-8|utf-8)''([^;]+)/.exec(header)
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1].trim())
    } catch {
      // fall through to the plain form
    }
  }
  const plain = /filename="?([^";]+)"?/.exec(header)
  return plain ? plain[1].trim() : null
}

/**
 * Downloads a binary endpoint (PDF, export) through the same auth path as JSON requests:
 * bearer token, one refresh-and-retry on 401, and the backend's ProblemDetails message on
 * failure. Raw `fetch` callers used to skip the refresh, so a download after the access
 * token expired failed until some other request had refreshed it. Documents and exports are
 * slow by nature: the deadline is {@link LONG_RUNNING_TIMEOUT_MS} unless the caller says otherwise.
 */
async function fetchBlob(path: string, options?: RequestOptions): Promise<{ blob: Blob; fileName: string | null }> {
  const effective: RequestOptions = { ...options, timeoutMs: options?.timeoutMs ?? LONG_RUNNING_TIMEOUT_MS }
  const attempt = await sendAuthenticated(path, () => ({ method: 'GET', headers: buildHeaders(false) }), effective)
  try {
    const { response } = attempt
    if (!response.ok) throw await failureFrom(attempt, path, false)
    const blob = await readBody(attempt, () => response.blob())
    return { blob, fileName: fileNameFromDisposition(response.headers.get('Content-Disposition')) }
  } finally {
    attempt.dispose()
  }
}

/** Fetches a file and hands it to the browser as a download (`fallbackName` when the server sends no name). */
async function downloadFile(path: string, fallbackName: string, options?: RequestOptions): Promise<void> {
  const { blob, fileName } = await fetchBlob(path, options)
  const objectUrl = URL.createObjectURL(blob)
  try {
    const anchor = document.createElement('a')
    anchor.href = objectUrl
    anchor.download = fileName ?? fallbackName
    anchor.rel = 'noopener'
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    // Revoke after the click has been dispatched; some browsers cancel an immediately revoked URL.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 1000)
  }
}

export const apiClient = {
  getJson,
  getJsonWithMeta,
  postJson,
  putJson,
  patchJson,
  deleteRequest,
  deleteJson,
  fetchBlob,
  downloadFile,
}
