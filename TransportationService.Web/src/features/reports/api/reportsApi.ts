import { apiClient } from '../../../api/apiClient'
import type { ReportCatalogEntry } from '../types'

export function getReportCatalog(): Promise<ReportCatalogEntry[]> {
  return apiClient.getJson<ReportCatalogEntry[]>('/api/reports/catalog')
}
