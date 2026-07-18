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
}

export interface AuthTokens {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  user: CurrentUser
}
