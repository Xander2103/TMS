export type InvoiceStatus = 'Draft' | 'Sent' | 'Paid' | 'Cancelled'

/** Vertaalsleutels — renderen als t(INVOICE_STATUS_LABELS[status]). */
export const INVOICE_STATUS_LABELS: Record<InvoiceStatus, string> = {
  Draft: 'invoices.status.Draft',
  Sent: 'invoices.status.Sent',
  Paid: 'invoices.status.Paid',
  Cancelled: 'invoices.status.Cancelled',
}

export const INVOICE_STATUSES: InvoiceStatus[] = ['Draft', 'Sent', 'Paid', 'Cancelled']

export const INVOICE_STATUS_TONE: Record<InvoiceStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Draft: 'neutral',
  Sent: 'info',
  Paid: 'success',
  Cancelled: 'danger',
}

/** Vertaalsleutels — renderen als t(INVOICE_TRANSITION_LABELS[target]). */
export const INVOICE_TRANSITION_LABELS: Record<InvoiceStatus, string> = {
  Draft: 'invoices.transition.Draft',
  Sent: 'invoices.transition.Sent',
  Paid: 'invoices.transition.Paid',
  Cancelled: 'invoices.transition.Cancelled',
}

export interface InvoiceListItem {
  id: string
  invoiceNumber: string
  invoiceDate: string
  dueDate: string
  customerId: string
  customerName: string
  status: InvoiceStatus
  currency: string
  subtotal: number
  vatAmount: number
  total: number
  lineCount: number
}

export interface InvoiceLine {
  id: string
  sequence: number
  transportOrderId: string | null
  orderNumber: string | null
  description: string
  quantity: number
  unitPrice: number
  vatRatePercent: number
  lineTotal: number
  /** Verkoopcategorie: live while Draft, frozen name/account after Send. */
  salesCategoryId: string | null
  salesCategoryName: string | null
  ledgerAccountNumber: string | null
  ledgerAccountName: string | null
  /** Draft-stage warning when the category is missing or unmapped; null = ok. */
  ledgerWarning: string | null
  /** Sprint 5: fiscal treatment of the line (VatTreatment name) — snapshot after Send, live while Draft. */
  vatTreatment?: string | null
  /** Where the treatment came from: LineOverride | SalesCode | Customer | TenantDefault. */
  vatTreatmentSource?: string | null
  vatLegalText?: string | null
  salesCode?: string | null
}

export interface InvoiceDetail {
  id: string
  invoiceNumber: string
  invoiceDate: string
  dueDate: string
  customerId: string
  customerName: string
  customerVatNumber: string | null
  status: InvoiceStatus
  currency: string
  notes: string | null
  purchaseOrderNumber: string | null
  lines: InvoiceLine[]
  subtotal: number
  vatAmount: number
  total: number
  allowedTransitions: InvoiceStatus[]
  legalEntityId: string | null
  legalEntityName: string | null
  invoicePeriodYear: number
  invoicePeriodMonth: number
  numberIsManual: boolean
  /** Sprint 5: the customer treatment frozen on the header, the document language and its statutory text. */
  customerVatTreatment?: string | null
  languageCode?: string | null
  vatLegalText?: string | null
}

/** Mirrors InvoiceNumberPreviewDto (GET /api/invoices/next-number). */
export interface InvoiceNumberPreview {
  invoiceNumber: string
  legalEntityId: string
  year: number
  month: number
}

export interface UninvoicedOrder {
  id: string
  orderNumber: string
  orderDate: string
  goodsDescription: string
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
  agreedPrice: number | null
  /** Wave 2 §6: NotReady | ReadyForInvoice | ReviewRequired — informatief, nooit blokkerend. */
  invoiceReadiness?: string
  /** Puntkomma-gescheiden redencodes bij ReviewRequired (bv. pricing.stale;pod.missing). */
  invoiceReadinessReasons?: string | null
}

export interface ManualLineInput {
  description: string
  quantity: number
  unitPrice: number
  vatRatePercent: number | null
}

export interface UpdateLineInput {
  id: string | null
  description: string
  quantity: number
  unitPrice: number
  vatRatePercent: number
  /** Null keeps the line's current category. */
  salesCategoryId?: string | null
}

export function euro(value: number, currency = 'EUR'): string {
  return value.toLocaleString('nl-BE', { style: 'currency', currency })
}
