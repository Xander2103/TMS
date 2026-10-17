import { formatCurrency, formatQuantity } from '../../utils/numbers'

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
  /** ACC-H3: the legal number is assigned at issue — null/empty while the document is a draft. */
  invoiceNumber: string
  /** "FAC-2026100006", or the draft reference ("Concept …") while no legal number exists. Absent on older payloads. */
  displayNumber?: string
  kind?: 'Invoice' | 'CreditNote'
  invoiceDate: string
  dueDate: string
  customerId: string
  customerName: string
  /** ACC-M10: shown next to the customer so a number search reads back. Absent on older payloads. */
  customerNumber?: string | null
  status: InvoiceStatus
  currency: string
  subtotal: number
  vatAmount: number
  total: number
  lineCount: number
  paidAt?: string | null
  /** Latest Peppol transmission status of the document; null = never sent through Peppol. */
  peppolStatus?: string | null
}

/** ACC-M10: paid/unpaid list filter (the URL value and the query parameter). */
export const INVOICE_PAID_FILTERS = ['paid', 'unpaid'] as const
export type InvoicePaidFilter = (typeof INVOICE_PAID_FILTERS)[number]

export interface InvoiceLine {
  id: string
  sequence: number
  transportOrderId: string | null
  orderNumber: string | null
  /** Activity-backed line (Opslag/Kraan uit een dossier): transportOrderId is null, this is set. */
  dossierActivityId: string | null
  dossierNumber: string | null
  description: string
  /** Up to 3 decimals (ACC-M2). */
  quantity: number
  /** Up to 4 decimals (ACC-M2): never rounded on the client. */
  unitPrice: number
  /** Human unit ("uur", "Europallet"); the structured UN/ECE code lives on the e-invoice only. */
  unitLabel?: string | null
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
  /** What the customer reads in the invoice language: what Send will freeze while Draft, frozen after. */
  customerDescription?: string | null
}

/** ACC-M4: one blocking/advisory issue the issue preflight found; `lineIds` points at the affected lines. */
export interface InvoiceIssuePreflightIssue {
  code: string
  message: string
  lineIds: string[]
}

export interface InvoiceIssuePreflight {
  canIssue: boolean
  issues: InvoiceIssuePreflightIssue[]
}

/** ACC-M7: one readable step of the invoice's financial story (GET /api/invoices/{id}/history). */
export interface InvoiceHistoryEvent {
  id: string
  timestamp: string
  /** System = automatic (dispatcher, provider), never the person who happened to queue it. */
  actorKind: 'User' | 'System' | 'Unknown'
  actorName: string | null
  /** Stable machine code ("invoice.issued", "peppol.delivered", …). */
  type: string
  /** The Dutch sentence the user reads. */
  title: string
  detail: string | null
}

export interface InvoiceDetail {
  id: string
  /** ACC-H3: null while Draft — the legal number is assigned atomically at issue. */
  invoiceNumber: string | null
  /** ACC-H3: internal reference of a draft ("CONCEPT-A3FC"); absent on older payloads. */
  draftReference?: string | null
  /** Number when issued, draft reference before; absent on older payloads. */
  displayNumber?: string | null
  /** ACC-H1: the VAT % a manual line defaults to, derived from the invoice's VAT treatment. */
  defaultVatRatePercent?: number | null
  /** ACC-H5: effective payment term (customer → legal entity → tenant) in days. */
  paymentTermDays?: number | null
  paidAt?: string | null
  paidByName?: string | null
  /** ACC-M11: why the credit note was made (credit notes only). */
  creditReason?: string | null
  customerLegalName?: string | null
  customerAddressLine?: string | null
  sellerLegalName?: string | null
  sellerCompanyNumber?: string | null
  paymentTermsText?: string | null
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
  /** Invoice or CreditNote; a credit note points at the invoice it credits, an invoice lists its credit notes. */
  kind?: 'Invoice' | 'CreditNote'
  creditedInvoiceId?: string | null
  creditedInvoiceNumber?: string | null
  creditNotes?: { id: string; invoiceNumber: string; status: InvoiceStatus }[] | null
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

/** Mirrors UninvoicedActivityDto (GET /api/invoices/uninvoiced-activities): a priced dossier activity without an invoice line. */
export interface UninvoicedActivity {
  id: string
  dossierId: string
  dossierNumber: string
  dossierTitle: string | null
  activityTypeName: string
  label: string | null
  /** Kalenderdatum (YYYY-MM-DD) zonder tijd. */
  plannedDate: string | null
  agreedPrice: number
  /** Explicitly agreed at no charge: still invoiceable as a € 0,00 line. */
  isFree: boolean
  pricingSource: 'OneOff' | 'Lines'
  priceLineCount: number
  legalEntityId: string | null
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
  // Central tenant-aware notation ("€ 1.234,56"); non-EUR currencies show their ISO code as symbol.
  return formatCurrency(value, currency === 'EUR' ? '€' : currency)
}

/**
 * ACC-M2: a unit price keeps the decimals it was entered with (up to 4: km, kg, litre and hour
 * fractions), while amounts stay at currency precision. "€ 0,335" — never silently "€ 0,34".
 */
export function unitPrice(value: number, currency = 'EUR'): string {
  const decimals = Math.min(4, Math.max(2, countDecimals(value)))
  const body = formatQuantity(value, decimals)
  // formatQuantity drops trailing zeros; pad back to at least two so prices read as money.
  const [integer, fraction = ''] = body.split(/[.,]/)
  const separator = body.includes(',') ? ',' : '.'
  const padded = fraction.length >= 2 ? body : `${integer}${separator}${fraction.padEnd(2, '0')}`
  return currency === 'EUR' ? `€ ${padded}` : `${currency} ${padded}`
}

function countDecimals(value: number): number {
  if (!Number.isFinite(value)) return 0
  const text = value.toString()
  if (text.includes('e-')) return Number(text.split('e-')[1]) + (text.split('.')[1]?.replace(/e.*/, '').length ?? 0)
  return text.split('.')[1]?.length ?? 0
}

/** ACC-M11: a credit note is a credit document — its totals read with a minus sign. */
export function creditAmount(value: number, currency = 'EUR'): string {
  return `− ${euro(Math.abs(value), currency)}`
}

/** ISO date + days → ISO date, in UTC so no DST shift touches the calendar day. */
export function addDaysIso(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split('-').map(Number)
  if (!y || !m || !d) return isoDate
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}

/** Whole days between two ISO dates (due − invoice); the effective term of an existing draft. */
export function daysBetweenIso(from: string, to: string): number | null {
  const a = Date.parse(`${from}T00:00:00Z`)
  const b = Date.parse(`${to}T00:00:00Z`)
  if (Number.isNaN(a) || Number.isNaN(b)) return null
  return Math.round((b - a) / 86_400_000)
}

/** ACC-H3: what a document is called — its legal number once issued, its draft reference before. */
export function invoiceDisplayNumber(
  invoice: Pick<InvoiceDetail, 'invoiceNumber' | 'displayNumber' | 'draftReference'>,
  draftLabel: string,
): string {
  return invoice.invoiceNumber ?? invoice.displayNumber ?? invoice.draftReference ?? draftLabel
}
