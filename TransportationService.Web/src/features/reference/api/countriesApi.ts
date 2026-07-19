import { apiClient } from '../../../api/apiClient'

export interface CountryOption {
  code: string
  alpha3: string
  name: string
  isEuMember: boolean
}

// The ISO country list is global and immutable during a session — cache the promise so
// every combobox on the page shares one request.
let cache: Promise<CountryOption[]> | null = null

export function getCountryOptions(): Promise<CountryOption[]> {
  cache ??= apiClient.getJson<CountryOption[]>('/api/countries/options').catch((error) => {
    cache = null
    throw error
  })
  return cache
}
