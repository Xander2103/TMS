import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type {
  InvoiceDetail,
  InvoiceListItem,
  InvoiceNumberPreview,
  InvoiceStatus,
  ManualLineInput,
  UninvoicedOrder,
  UpdateLineInput,
} from '../types'

export interface SearchInvoicesParams {
  search?: string
  status?: InvoiceStatus
  customerId?: string
  page: number
  pageSize: number
}

export function searchInvoices(params: SearchInvoicesParams): Promise<PagedResult<InvoiceListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.customerId) query.set('customerId', params.customerId)
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
  manualLines: ManualLineInput[]
  notes: string | null
  purchaseOrderNumber?: string | null
  legalEntityId?: string | null
  invoicePeriodYear?: number | null
  invoicePeriodMonth?: number | null
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

export function deleteInvoice(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/invoices/${id}`)
}
