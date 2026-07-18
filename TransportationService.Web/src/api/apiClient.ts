import { apiBaseUrl, devTenantId, devUserId } from '../config/env'

export class ApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

interface RequestOptions {
  signal?: AbortSignal
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function devHeaders(): Record<string, string> {
  const headers: Record<string, string> = {}
  if (devTenantId) headers['X-Dev-Tenant-Id'] = devTenantId
  if (devUserId) headers['X-Dev-User-Id'] = devUserId
  return headers
}

async function getJson<T>(path: string, options?: RequestOptions): Promise<T> {
  const url = `${apiBaseUrl}${path}`
  let response: Response

  try {
    response = await fetch(url, { headers: devHeaders(), signal: options?.signal })
  } catch (err) {
    if (isAbortError(err)) {
      throw err
    }
    throw new ApiError(`Unable to reach ${path}`)
  }

  if (!response.ok) {
    throw new ApiError(`Request to ${path} failed with status ${response.status}`, response.status)
  }

  return response.json() as Promise<T>
}

async function sendJson<TResponse, TBody>(
  method: 'POST' | 'PUT' | 'PATCH',
  path: string,
  body: TBody,
  options?: RequestOptions,
): Promise<TResponse> {
  const url = `${apiBaseUrl}${path}`
  let response: Response

  try {
    response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json',
        ...devHeaders(),
      },
      body: JSON.stringify(body),
      signal: options?.signal,
    })
  } catch (err) {
    if (isAbortError(err)) {
      throw err
    }
    throw new ApiError(`Unable to reach ${path}`)
  }

  if (!response.ok) {
    throw new ApiError(`Request to ${path} failed with status ${response.status}`, response.status)
  }

  if (response.status === 204) {
    return undefined as TResponse
  }

  return response.json() as Promise<TResponse>
}

async function postJson<TResponse, TBody>(
  path: string,
  body: TBody,
  options?: RequestOptions,
): Promise<TResponse> {
  return sendJson<TResponse, TBody>('POST', path, body, options)
}

async function putJson<TResponse, TBody>(
  path: string,
  body: TBody,
  options?: RequestOptions,
): Promise<TResponse> {
  return sendJson<TResponse, TBody>('PUT', path, body, options)
}

async function patchJson<TResponse, TBody>(
  path: string,
  body: TBody,
  options?: RequestOptions,
): Promise<TResponse> {
  return sendJson<TResponse, TBody>('PATCH', path, body, options)
}

async function deleteRequest(path: string, options?: RequestOptions): Promise<void> {
  const url = `${apiBaseUrl}${path}`
  let response: Response

  try {
    response = await fetch(url, {
      method: 'DELETE',
      headers: devHeaders(),
      signal: options?.signal,
    })
  } catch (err) {
    if (isAbortError(err)) {
      throw err
    }
    throw new ApiError(`Unable to reach ${path}`)
  }

  if (!response.ok) {
    throw new ApiError(`Request to ${path} failed with status ${response.status}`, response.status)
  }
}

export const apiClient = {
  getJson,
  postJson,
  putJson,
  patchJson,
  deleteRequest,
}
