const LOCAL_DEV_API_BASE_URL = 'http://localhost:5019'

export const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? LOCAL_DEV_API_BASE_URL

export const devTenantId = import.meta.env.VITE_DEV_TENANT_ID ?? ''
export const devUserId = import.meta.env.VITE_DEV_USER_ID ?? ''
