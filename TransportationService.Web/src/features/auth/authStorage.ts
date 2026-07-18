import type { AuthTokens } from './authTypes'

/**
 * Token persistence for the current development architecture. Tokens live in localStorage so
 * the session survives a refresh. The access token is short-lived and the refresh token rotates
 * on every use (single-use, server-revocable), which bounds the blast radius.
 *
 * Production hardening (documented, out of scope for this milestone): move the refresh token to
 * an httpOnly, Secure, SameSite cookie so it is never reachable from JavaScript.
 */
const ACCESS_KEY = 'ts.accessToken'
const REFRESH_KEY = 'ts.refreshToken'

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_KEY)
}

export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_KEY)
}

export function storeTokens(tokens: AuthTokens): void {
  localStorage.setItem(ACCESS_KEY, tokens.accessToken)
  localStorage.setItem(REFRESH_KEY, tokens.refreshToken)
}

export function clearTokens(): void {
  localStorage.removeItem(ACCESS_KEY)
  localStorage.removeItem(REFRESH_KEY)
}
