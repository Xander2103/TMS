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
  description: string
}

/** Presentation metadata per category; unknown categories fall back to a neutral card. */
export const REPORT_CATEGORY_META: Record<string, ReportCategoryMeta> = {
  Operationeel: { icon: '🚚', description: 'Opdrachten, colli, scans en incidenten.' },
  Planning: { icon: '🗓️', description: 'Betrouwbaarheid en bezetting van de planning.' },
  Financieel: { icon: '💶', description: 'Winstgevendheid, KPI’s en facturatie.' },
  Vloot: { icon: '🚛', description: 'Voertuigen, verbruik en uitstoot.' },
  HR: { icon: '👥', description: 'Uren, kwalificaties en beschikbaarheid.' },
  Klanten: { icon: '🤝', description: 'Klantoverzichten en orderboeken.' },
}

export const REPORT_CATEGORY_ORDER = ['Operationeel', 'Planning', 'Financieel', 'Vloot', 'HR', 'Klanten']
