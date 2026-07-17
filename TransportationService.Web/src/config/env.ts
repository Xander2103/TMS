const LOCAL_DEV_API_BASE_URL = 'http://localhost:5019'

export const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? LOCAL_DEV_API_BASE_URL
