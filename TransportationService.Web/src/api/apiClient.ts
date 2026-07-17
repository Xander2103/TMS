import { apiBaseUrl } from '../config/env'

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

async function getJson<T>(path: string, options?: RequestOptions): Promise<T> {
  const url = `${apiBaseUrl}${path}`
  let response: Response

  try {
    response = await fetch(url, { signal: options?.signal })
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

async function postJson<TResponse, TBody>(
  path: string,
  body: TBody,
  options?: RequestOptions,
): Promise<TResponse> {
  const url = `${apiBaseUrl}${path}`
  let response: Response

  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
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

  return response.json() as Promise<TResponse>
}

export const apiClient = {
  getJson,
  postJson,
}
