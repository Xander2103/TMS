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

/**
 * Why a session refresh did not produce tokens. The distinction matters: only a REJECTED
 * credential (401/403, or a 400 for a malformed cookie) means "not signed in". A rate limit
 * (429), a server error or a network interruption says nothing about the session, so the app
 * keeps it and lets the user retry instead of dropping them on the login page (B01).
 */
export type RefreshOutcome =
  | { status: 'ok'; tokens: AuthTokens }
  | { status: 'unauthenticated' }
  | { status: 'unavailable'; reason: 'network' | 'rate_limited' | 'server'; httpStatus?: number }

/** Exchange the HttpOnly refresh cookie for a fresh token pair, classifying every failure. */
export async function refresh(): Promise<RefreshOutcome> {
  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
      credentials: 'include',
    })
  } catch {
    return { status: 'unavailable', reason: 'network' }
  }
  if (response.ok) {
    return { status: 'ok', tokens: (await response.json()) as AuthTokens }
  }
  if (response.status === 401 || response.status === 403 || response.status === 400) {
    return { status: 'unauthenticated' }
  }
  if (response.status === 429) {
    return { status: 'unavailable', reason: 'rate_limited', httpStatus: 429 }
  }
  return { status: 'unavailable', reason: 'server', httpStatus: response.status }
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
