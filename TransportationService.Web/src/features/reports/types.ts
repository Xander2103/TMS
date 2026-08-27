export type ReportKind = 'Export' | 'Page' | 'ComingSoon'

export type ReportFilterKey = 'dateRange' | 'search' | 'orderStatus'

export interface ReportCatalogEntry {
  id: string
  category: string
  title: string
  description: string
  kind: ReportKind
  endpoint: string | null
  route: string | null
  filters: string[]
  fileType: string | null
}

export interface ReportCategoryMeta {
  icon: string
  /** Vertaalsleutel — renderen als t(descriptionKey). */
  descriptionKey: string
}

/**
 * Presentation metadata per category; unknown categories fall back to a neutral card.
 * Gekeyd op de stabiele categoriestring die de backendcatalogus levert.
 */
export const REPORT_CATEGORY_META: Record<string, ReportCategoryMeta> = {
  Operationeel: { icon: '🚚', descriptionKey: 'kpiReports.reports.categories.Operationeel' },
  Planning: { icon: '🗓️', descriptionKey: 'kpiReports.reports.categories.Planning' },
  Financieel: { icon: '💶', descriptionKey: 'kpiReports.reports.categories.Financieel' },
  Vloot: { icon: '🚛', descriptionKey: 'kpiReports.reports.categories.Vloot' },
  HR: { icon: '👥', descriptionKey: 'kpiReports.reports.categories.HR' },
  Klanten: { icon: '🤝', descriptionKey: 'kpiReports.reports.categories.Klanten' },
}

export const REPORT_CATEGORY_ORDER = ['Operationeel', 'Planning', 'Financieel', 'Vloot', 'HR', 'Klanten']
