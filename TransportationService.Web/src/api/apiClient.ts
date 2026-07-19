import { apiBaseUrl } from '../config/env'
import { getAccessToken, getRefreshToken, storeTokens, clearTokens } from '../features/auth/authStorage'
import { refresh as refreshTokens } from '../features/auth/authApi'

export class ApiError extends Error {
  readonly status?: number
  /** Parsed JSON error body, when the server sent one (e.g. 409 conflict payloads). */
  readonly body?: unknown

  constructor(message: string, status?: number, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

interface RequestOptions {
  signal?: AbortSignal
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

// Called when authentication cannot be recovered (refresh failed). The AuthProvider registers a
// handler that clears state and redirects to the login page.
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler
}

// Single-flight refresh: concurrent 401s share one refresh attempt instead of stampeding.
let refreshInFlight: Promise<boolean> | null = null

function attemptRefresh(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    const refreshToken = getRefreshToken()
    if (!refreshToken) return false
    const tokens = await refreshTokens(refreshToken)
    if (!tokens) return false
    storeTokens(tokens)
    return true
  })().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

function buildHeaders(hasBody: boolean): Record<string, string> {
  const headers: Record<string, string> = {}
  if (hasBody) headers['Content-Type'] = 'application/json'
  const token = getAccessToken()
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

async function request<T>(method: Method, path: string, body?: unknown, options?: RequestOptions): Promise<T> {
  const url = `${apiBaseUrl}${path}`
  const hasBody = body !== undefined
  const serialized = hasBody ? JSON.stringify(body) : undefined

  const send = (): Promise<Response> =>
    fetch(url, { method, headers: buildHeaders(hasBody), body: serialized, signal: options?.signal })

  let response: Response
  try {
    response = await send()
  } catch (err) {
    if (isAbortError(err)) throw err
    throw new ApiError(`Unable to reach ${path}`)
  }

  if (response.status === 401) {
    const refreshed = await attemptRefresh()
    if (refreshed) {
      try {
        response = await send()
      } catch (err) {
        if (isAbortError(err)) throw err
        throw new ApiError(`Unable to reach ${path}`)
      }
    }

    if (response.status === 401) {
      clearTokens()
      onUnauthorized?.()
      throw new ApiError('Authentication required', 401)
    }
  }

  if (!response.ok) {
    // Surface the backend's user-facing message (ProblemDetails `detail` or `{ message }`).
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
    throw new ApiError(
      serverMessage ?? `Request to ${path} failed with status ${response.status}`,
      response.status,
      errorBody,
    )
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

async function getJson<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('GET', path, undefined, options)
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

export const apiClient = {
  getJson,
  postJson,
  putJson,
  patchJson,
  deleteRequest,
}
