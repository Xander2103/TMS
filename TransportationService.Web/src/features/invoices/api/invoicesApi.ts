import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { PagedResult } from '../../../api/types'
import type {
  InvoiceDetail,
  InvoiceHistoryEvent,
  InvoiceIssuePreflight,
  InvoiceListItem,
  InvoiceNumberPreview,
  InvoicePaidFilter,
  InvoiceStatus,
  ManualLineInput,
  UninvoicedActivity,
  UninvoicedOrder,
  UpdateLineInput,
} from '../types'

export interface SearchInvoicesParams {
  search?: string
  status?: InvoiceStatus
  customerId?: string
  page: number
  pageSize: number
  /** B26: whitelisted server sort key and direction ('' = the list's natural order). */
  sort?: string
  dir?: 'asc' | 'desc' | ''
  /** ACC-M10: invoice-date window (ISO dates, inclusive), paid/unpaid and the latest Peppol status. */
  dateFrom?: string
  dateTo?: string
  paid?: InvoicePaidFilter | ''
  peppolStatus?: string
}

export function searchInvoices(params: SearchInvoicesParams): Promise<PagedResult<InvoiceListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.sort) query.set('sort', params.sort)
  if (params.dir) query.set('dir', params.dir)
  if (params.dateFrom) query.set('dateFrom', params.dateFrom)
  if (params.dateTo) query.set('dateTo', params.dateTo)
  if (params.paid) query.set('paid', params.paid)
  if (params.peppolStatus) query.set('peppolStatus', params.peppolStatus)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<InvoiceListItem>>(`/api/invoices?${query.toString()}`)
}

export function getInvoice(id: string): Promise<InvoiceDetail> {
  return apiClient.getJson<InvoiceDetail>(`/api/invoices/${id}`)
}

export function listUninvoicedOrders(customerId: string): Promise<UninvoicedOrder[]> {
  return apiClient.getJson<UninvoicedOrder[]>(`/api/invoices/uninvoiced-orders?customerId=${customerId}`)
}

/** Priced dossier activities (Opslag/Kraan) of the customer that have no invoice line yet. */
export function listUninvoicedActivities(customerId: string): Promise<UninvoicedActivity[]> {
  return apiClient.getJson<UninvoicedActivity[]>(`/api/invoices/uninvoiced-activities?customerId=${customerId}`)
}

export interface NextInvoiceNumberParams {
  legalEntityId?: string
  year?: number
  month?: number
}

/** Informative preview of the next number for an entity + period; 404s when no active entity exists. */
export function getNextInvoiceNumber(
  params: NextInvoiceNumberParams,
  options?: { signal?: AbortSignal },
): Promise<InvoiceNumberPreview> {
  const query = new URLSearchParams()
  if (params.legalEntityId) query.set('legalEntityId', params.legalEntityId)
  if (params.year !== undefined) query.set('year', String(params.year))
  if (params.month !== undefined) query.set('month', String(params.month))
  const suffix = query.toString()
  return apiClient.getJson<InvoiceNumberPreview>(`/api/invoices/next-number${suffix ? `?${suffix}` : ''}`, options)
}

export interface OverrideInvoiceNumberInput {
  invoiceNumber: string
  reason: string
}

/** Manual number correction (Draft only, permission invoices.override_number). */
export function overrideInvoiceNumber(id: string, input: OverrideInvoiceNumberInput): Promise<InvoiceDetail> {
  return apiClient.postJson<InvoiceDetail, OverrideInvoiceNumberInput>(`/api/invoices/${id}/number-override`, input)
}

export interface CreateInvoiceInput {
  customerId: string
  invoiceDate: string | null
  orderIds: string[]
  /** Dossier activities to invoice next to the orders; omitted = none. */
  dossierActivityIds?: string[]
  manualLines: ManualLineInput[]
  notes: string | null
  purchaseOrderNumber?: string | null
  legalEntityId?: string | null
  invoicePeriodYear?: number | null
  invoicePeriodMonth?: number | null
}

// --- Wave 10: facturatiecontrole ---

export interface ControlOrder {
  transportOrderId: string
  orderNumber: string
  orderDate: string
  agreedPrice: number | null
  dossierNumber: string | null
  customerReference: string | null
  invoiceReadiness: string
  reasons: string[]
  /** P12: uitgesteld tot deze datum (buiten de voorstellen, apart getoond). */
  snoozedUntil: string | null
  snoozeReason: string | null
}

/** T-H8: a billable standalone dossier activity (crane work, plateau, storage) on the control page. */
export interface ControlActivity {
  dossierActivityId: string
  dossierId: string
  dossierNumber: string
  activityTypeName: string
  label: string | null
  /** Completion date, else the planned date. */
  date: string | null
  agreedPrice: number | null
  isFree: boolean
  executionStatus: string
  customerId: string
  customerName: string
  /** Review only: `activity.price_missing` | `activity.price_zero_unconfirmed`. */
  reasons: string[]
}

export interface InvoiceProposal {
  customerId: string
  customerName: string
  grouping: string
  groupLabel: string
  orders: ControlOrder[]
  totalAmount: number
  /** T-H8: ready standalone activities of the same group; absent on older payloads. */
  activities?: ControlActivity[] | null
}

export interface InvoiceControl {
  proposals: InvoiceProposal[]
  needsReview: ControlOrder[]
  pendingCharges: string[]
  /** P12: uitgestelde orders — buiten de voorstellen tot hun datum, nooit verborgen. */
  snoozed: ControlOrder[]
  /** T-H8: executed billable activities without a (confirmed) price — never invisible to accounting. */
  activitiesNeedingReview?: ControlActivity[] | null
}

export function getInvoiceControl(): Promise<InvoiceControl> {
  return apiClient.getJson('/api/invoice-control')
}

export interface SnoozeOrderInput {
  /** null = uitstel opheffen. */
  until: string | null
  reason: string | null
}

/** P12: stelt de facturatie van een order uit (of heft het uitstel op met until = null). */
export function snoozeInvoiceControlOrder(orderId: string, input: SnoozeOrderInput): Promise<void> {
  return apiClient.putJson<void, SnoozeOrderInput>(`/api/invoice-control/orders/${orderId}/snooze`, input)
}

export function createInvoice(input: CreateInvoiceInput): Promise<InvoiceDetail> {
  return apiClient.postJson<InvoiceDetail, CreateInvoiceInput>('/api/invoices', input)
}

export interface UpdateInvoiceInput {
  invoiceDate: string
  dueDate: string
  lines: UpdateLineInput[]
  notes: string | null
  purchaseOrderNumber?: string | null
  invoicePeriodYear?: number | null
  invoicePeriodMonth?: number | null
}

export function updateInvoice(id: string, input: UpdateInvoiceInput): Promise<InvoiceDetail> {
  return apiClient.putJson<InvoiceDetail, UpdateInvoiceInput>(`/api/invoices/${id}`, input)
}

export function changeInvoiceStatus(id: string, status: InvoiceStatus): Promise<InvoiceDetail> {
  return apiClient.postJson<InvoiceDetail, { status: InvoiceStatus }>(`/api/invoices/${id}/status`, { status })
}

/** Fills only the MISSING ledger snapshots of a Sent/Paid invoice from the current mapping (accounting.manage). */
export function completeInvoiceLedgerSnapshots(id: string): Promise<InvoiceDetail> {
  return apiClient.postJson<InvoiceDetail, Record<string, never>>(`/api/invoices/${id}/complete-ledger-snapshots`, {})
}

export function deleteInvoice(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/invoices/${id}`)
}

/**
 * Fetches the invoice PDF as rendered for the customer (draft = stamped preview, same
 * description/language rules as Send). Presentation only: never changes invoice state.
 * Returns an object URL the caller must revoke.
 */
export async function fetchInvoicePdfUrl(id: string): Promise<string> {
  const response = await fetch(`${apiBaseUrl}/api/invoices/${id}/pdf`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('', response.status)
  }
  const blob = await response.blob()
  return URL.createObjectURL(blob)
}

export interface CreateCreditNoteInput {
  /** ACC-M11: why the invoice is credited (required, max 500). */
  reason: string
}

/** Creates a DRAFT credit note against a Sent/Paid invoice; the original is never modified. */
export function createCreditNote(id: string, input: CreateCreditNoteInput): Promise<InvoiceDetail> {
  return apiClient.postJson<InvoiceDetail, CreateCreditNoteInput>(`/api/invoices/${id}/credit-note`, input)
}

/** ACC-M4: what still blocks issuing this draft (PO, categories, VAT, unpriced sources …). */
export function getInvoiceIssuePreflight(id: string, options?: { signal?: AbortSignal }): Promise<InvoiceIssuePreflight> {
  return apiClient.getJson<InvoiceIssuePreflight>(`/api/invoices/${id}/issue-preflight`, options)
}

/** ACC-M7: the invoice's financial story, oldest first; readable with invoices.view. */
export function getInvoiceHistory(id: string, options?: { signal?: AbortSignal }): Promise<InvoiceHistoryEvent[]> {
  return apiClient.getJson<InvoiceHistoryEvent[]>(`/api/invoices/${id}/history`, options)
}

/** ACC-H1: the VAT % manual lines default to for this customer (falls back to the server default). */
export function getDefaultVatRate(customerId: string, options?: { signal?: AbortSignal }): Promise<{ vatRatePercent: number }> {
  return apiClient.getJson<{ vatRatePercent: number }>(`/api/invoices/default-vat?customerId=${encodeURIComponent(customerId)}`, options)
}
