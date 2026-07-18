import { apiClient } from '../../../api/apiClient'
import type { CompanySettings, UpdateCompanySettings } from '../types'

export function getCompanySettings(): Promise<CompanySettings> {
  return apiClient.getJson<CompanySettings>('/api/company-settings')
}

export function updateCompanySettings(input: UpdateCompanySettings): Promise<CompanySettings> {
  return apiClient.putJson<CompanySettings, UpdateCompanySettings>('/api/company-settings', input)
}
