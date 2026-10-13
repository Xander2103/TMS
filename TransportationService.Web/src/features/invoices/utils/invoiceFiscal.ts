import { VAT_TREATMENT_LABEL_KEYS, type VatTreatment } from '../../customers/types'
import type { InvoiceLine } from '../types'

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Human label for a VatTreatment name; unknown names are shown verbatim rather than hidden. */
export function fiscalTreatmentLabel(t: Translate, treatment: string | null | undefined): string | null {
  if (!treatment) return null
  const key = VAT_TREATMENT_LABEL_KEYS[treatment as VatTreatment]
  return key ? t(key) : treatment
}

const SOURCES = new Set(['LineOverride', 'SalesCode', 'Customer', 'TenantDefault'])

export function sourceLabel(t: Translate, source: string): string {
  return SOURCES.has(source) ? t(`invoices.fiscal.source.${source}`) : source
}

/**
 * A line deviates from the invoice's own (customer) treatment when its treatment came from a
 * sales code or a line override — those are the exceptions the operator must be able to see.
 */
export function isLineFiscalException(line: InvoiceLine): boolean {
  return line.vatTreatmentSource === 'SalesCode' || line.vatTreatmentSource === 'LineOverride'
}
