/**
 * Access-token holder (H5). The short-lived access token lives ONLY in memory: nothing
 * long-lived is ever readable from JavaScript, so XSS cannot steal a credential that outlives
 * the tab. The session itself survives a page refresh through the HttpOnly refresh cookie the
 * server sets on /api/auth — on boot the AuthProvider exchanges that cookie for a fresh access
 * token. The legacy localStorage entries from the previous architecture are actively removed.
 */
const LEGACY_ACCESS_KEY = 'ts.accessToken'
const LEGACY_REFRESH_KEY = 'ts.refreshToken'

let accessToken: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function clearTokens(): void {
  accessToken = null
  clearLegacyTokens()
}

/** One-time migration sweep: tokens must never linger in localStorage on upgraded clients. */
export function clearLegacyTokens(): void {
  localStorage.removeItem(LEGACY_ACCESS_KEY)
  localStorage.removeItem(LEGACY_REFRESH_KEY)
}
