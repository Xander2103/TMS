import { apiBaseUrl } from '../../config/env'
import type { AuthTokens, CurrentUser } from './authTypes'

/**
 * Raw authentication calls. These deliberately do NOT go through the shared apiClient: the
 * apiClient adds the bearer token and performs refresh-on-401, and routing login/refresh through
 * it would create recursion. Everything else in the app uses apiClient.
 *
 * H5: the refresh token never reaches JavaScript — the server keeps it in an HttpOnly cookie
 * scoped to /api/auth, so every call here sends `credentials: 'include'`.
 */

export class LoginError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'LoginError'
  }
}

export async function login(email: string, password: string, signal?: AbortSignal): Promise<AuthTokens> {
  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
      credentials: 'include',
      signal,
    })
  } catch {
    throw new LoginError('De server is niet bereikbaar. Probeer het later opnieuw.')
  }

  if (response.status === 401) {
    throw new LoginError('E-mailadres of wachtwoord is onjuist.')
  }
  if (!response.ok) {
    throw new LoginError('Inloggen is mislukt. Probeer het later opnieuw.')
  }
  return (await response.json()) as AuthTokens
}

/** Exchange the HttpOnly refresh cookie for a fresh token pair. Null when refresh is not possible. */
export async function refresh(): Promise<AuthTokens | null> {
  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
      credentials: 'include',
    })
  } catch {
    return null
  }
  if (!response.ok) {
    return null
  }
  return (await response.json()) as AuthTokens
}

export async function fetchCurrentUser(accessToken: string): Promise<CurrentUser | null> {
  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}/api/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    })
  } catch {
    return null
  }
  if (!response.ok) {
    return null
  }
  return (await response.json()) as CurrentUser
}

export async function logout(accessToken: string): Promise<void> {
  try {
    await fetch(`${apiBaseUrl}/api/auth/logout`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify({}),
      credentials: 'include',
    })
  } catch {
    // Best-effort: local state is cleared regardless; the server cookie is deleted on success.
  }
}
