export type InvoiceStatus = 'Draft' | 'Sent' | 'Paid' | 'Cancelled'

export const INVOICE_STATUS_LABELS: Record<InvoiceStatus, string> = {
  Draft: 'Concept',
  Sent: 'Verzonden',
  Paid: 'Betaald',
  Cancelled: 'Geannuleerd',
}

export const INVOICE_STATUSES: InvoiceStatus[] = ['Draft', 'Sent', 'Paid', 'Cancelled']

export const INVOICE_STATUS_TONE: Record<InvoiceStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Draft: 'neutral',
  Sent: 'info',
  Paid: 'success',
  Cancelled: 'danger',
}

export const INVOICE_TRANSITION_LABELS: Record<InvoiceStatus, string> = {
  Draft: 'Terug naar concept',
  Sent: 'Verzenden',
  Paid: 'Markeer als betaald',
  Cancelled: 'Annuleren',
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
}

export function euro(value: number, currency = 'EUR'): string {
  return value.toLocaleString('nl-BE', { style: 'currency', currency })
}
