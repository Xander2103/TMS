export interface CurrentUser {
  id: string
  tenantId: string
  tenantName: string
  email: string
  firstName: string
  lastName: string
  employeeId: string | null
  roles: string[]
  permissions: string[]
  /** Temporary/admin-set credential: the UI forces a password change before anything else. */
  mustChangePassword: boolean
  /** Set for customer-portal users only; drives post-login routing into /klantportaal. */
  customerId: string | null
}

export interface AuthTokens {
  accessToken: string
  accessTokenExpiresAt: string
  /** Always empty for browser clients (H5): the refresh token lives in an HttpOnly cookie. */
  refreshToken: string
  refreshTokenExpiresAt: string
  user: CurrentUser
}
