import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { PagedResult } from '../../../api/types'
import type { BadgeTone } from '../../../components/ui/Badge'
import type { TranslateFn } from '../../../i18n/localeContext'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'

// --- Statussen & labels ---

export type PeppolTransmissionStatus =
  | 'Draft'
  | 'Validated'
  | 'Queued'
  | 'SubmittedToProvider'
  | 'AcceptedByProvider'
  | 'Delivered'
  | 'Failed'
  | 'Rejected'
  | 'Cancelled'

/** Vertaalsleutels — renderen als t(PEPPOL_STATUS_LABEL_KEYS[status]). Gedeeld met het klantportaal. */
export const PEPPOL_STATUS_LABEL_KEYS: Record<PeppolTransmissionStatus, string> = {
  Draft: 'invoices.peppolStatus.Draft',
  Validated: 'invoices.peppolStatus.Validated',
  Queued: 'invoices.peppolStatus.Queued',
  SubmittedToProvider: 'invoices.peppolStatus.SubmittedToProvider',
  AcceptedByProvider: 'invoices.peppolStatus.AcceptedByProvider',
  Delivered: 'invoices.peppolStatus.Delivered',
  Failed: 'invoices.peppolStatus.Failed',
  Rejected: 'invoices.peppolStatus.Rejected',
  Cancelled: 'invoices.peppolStatus.Cancelled',
}

export const PEPPOL_STATUS_TONE: Record<PeppolTransmissionStatus, BadgeTone> = {
  Draft: 'neutral',
  Validated: 'info',
  Queued: 'info',
  SubmittedToProvider: 'info',
  AcceptedByProvider: 'info',
  Delivered: 'success',
  Failed: 'danger',
  Rejected: 'danger',
  Cancelled: 'neutral',
}

/** Label for a raw transmission status string (unknown values fall through unchanged). */
export function peppolStatusLabel(t: TranslateFn, status: string): string {
  const key = PEPPOL_STATUS_LABEL_KEYS[status as PeppolTransmissionStatus]
  return key ? t(key) : status
}

export function peppolStatusTone(status: string): BadgeTone {
  return PEPPOL_STATUS_TONE[status as PeppolTransmissionStatus] ?? 'neutral'
}

export type PeppolDocumentKind = 'Invoice' | 'CreditNote'

/** Vertaalsleutels — renderen als t(PEPPOL_KIND_LABEL_KEYS[kind]). */
export const PEPPOL_KIND_LABEL_KEYS: Record<PeppolDocumentKind, string> = {
  Invoice: 'peppol.kind.Invoice',
  CreditNote: 'peppol.kind.CreditNote',
}

export type PeppolIncomingStatus = 'Received' | 'NeedsReview' | 'Linked' | 'Rejected'

/** Vertaalsleutels — renderen als t(PEPPOL_INCOMING_STATUS_LABEL_KEYS[status]). */
export const PEPPOL_INCOMING_STATUS_LABEL_KEYS: Record<PeppolIncomingStatus, string> = {
  Received: 'peppol.incomingStatus.Received',
  NeedsReview: 'peppol.incomingStatus.NeedsReview',
  Linked: 'peppol.incomingStatus.Linked',
  Rejected: 'peppol.incomingStatus.Rejected',
}

export const PEPPOL_INCOMING_STATUS_TONE: Record<PeppolIncomingStatus, BadgeTone> = {
  Received: 'info',
  NeedsReview: 'warning',
  Linked: 'success',
  Rejected: 'danger',
}

// --- Overzicht & instellingen ---

/** Mirrors PeppolChecklistItemDto. */
export interface PeppolChecklistItem {
  legalEntityId: string
  legalEntityName: string
  hasPeppolIdentity: boolean
  hasVatNumber: boolean
  hasIban: boolean
  enabled: boolean
  environment: string
  providerKey: string
  isComplete: boolean
}

export interface PeppolCounts {
  queued: number
  delivered: number
  failed: number
  receivedIncoming: number
}

/** Mirrors PeppolOverviewDto. */
export interface PeppolOverview {
  legalEntities: PeppolChecklistItem[]
  counts: PeppolCounts
  customersEnabledWithoutPeppolId: number
  activeCustomersMissingPeppolData: number
}

export type PeppolEnvironment = 'Sandbox' | 'Live'

/** Mirrors PeppolSettingsDto. */
export interface PeppolSettings {
  legalEntityId: string
  legalEntityName: string
  enabled: boolean
  environment: PeppolEnvironment
  providerKey: string
  invoiceEmailFallback: string | null
  defaultInvoiceNote: string | null
}

export interface SavePeppolSettingsInput {
  enabled: boolean
  environment: PeppolEnvironment
  providerKey: string
  invoiceEmailFallback: string | null
  defaultInvoiceNote: string | null
}

/** Mirrors PeppolTestConnectionResultDto. */
export interface PeppolTestConnectionResult {
  found: boolean
  supportedDocumentTypes: string[]
  providerReference: string | null
  error: string | null
}

export function getPeppolOverview(): Promise<PeppolOverview> {
  return apiClient.getJson<PeppolOverview>('/api/peppol/overview')
}

export function getPeppolSettings(legalEntityId: string): Promise<PeppolSettings> {
  return apiClient.getJson<PeppolSettings>(`/api/peppol/settings/${legalEntityId}`)
}

export function savePeppolSettings(legalEntityId: string, input: SavePeppolSettingsInput): Promise<PeppolSettings> {
  return apiClient.putJson<PeppolSettings, SavePeppolSettingsInput>(`/api/peppol/settings/${legalEntityId}`, input)
}

export function testPeppolConnection(legalEntityId: string): Promise<PeppolTestConnectionResult> {
  return apiClient.postJson<PeppolTestConnectionResult, Record<string, never>>(
    `/api/peppol/settings/${legalEntityId}/test-connection`,
    {},
  )
}

// --- Uitgaande transmissies ---

/** Mirrors PeppolTransmissionListItemDto. */
export interface PeppolTransmissionRow {
  id: string
  invoiceId: string
  invoiceNumber: string
  documentKind: PeppolDocumentKind
  customerName: string
  total: number
  currency: string
  invoiceDate: string
  status: string
  environment: string
  providerMessageId: string | null
  errorDetail: string | null
  retryCount: number
  payloadVersion: number
  createdAt: string
}

export interface ListTransmissionsParams {
  status?: string
  search?: string
  page?: number
  pageSize?: number
}

export function listPeppolTransmissions(params: ListTransmissionsParams): Promise<PagedResult<PeppolTransmissionRow>> {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  if (params.search) query.set('search', params.search)
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 25))
  return apiClient.getJson(`/api/peppol/transmissions?${query.toString()}`)
}

export function retryPeppolTransmission(id: string): Promise<PeppolTransmissionRow> {
  return apiClient.postJson(`/api/peppol/transmissions/${id}/retry`, {})
}

export function cancelPeppolTransmission(id: string): Promise<PeppolTransmissionRow> {
  return apiClient.postJson(`/api/peppol/transmissions/${id}/cancel`, {})
}

// --- Inkomende documenten ---

/** Mirrors PeppolIncomingDocumentDto. */
export interface PeppolIncomingDocument {
  id: string
  documentKind: string
  supplierParticipant: string
  supplierName: string | null
  documentNumber: string
  documentDate: string | null
  amount: number | null
  currency: string | null
  status: PeppolIncomingStatus
  reviewNote: string | null
  receivedAt: string
}

export interface ListIncomingParams {
  status?: string
  page?: number
  pageSize?: number
}

export function listPeppolIncoming(params: ListIncomingParams): Promise<PagedResult<PeppolIncomingDocument>> {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 25))
  return apiClient.getJson(`/api/peppol/incoming?${query.toString()}`)
}

export function reviewPeppolIncoming(id: string, note: string | null): Promise<PeppolIncomingDocument> {
  return apiClient.postJson(`/api/peppol/incoming/${id}/review`, { note })
}

export function rejectPeppolIncoming(id: string, note: string | null): Promise<PeppolIncomingDocument> {
  return apiClient.postJson(`/api/peppol/incoming/${id}/reject`, { note })
}

// --- Factuurniveau ---

export interface PeppolValidationIssue {
  code: string
  message: string
}

export interface PeppolInvoiceValidationResult {
  isValid: boolean
  issues: PeppolValidationIssue[]
}

export interface PeppolPreviewParty {
  name: string
  vatNumber: string | null
  participant: string | null
}

export interface PeppolPreviewLine {
  sequence: number
  description: string
  quantity: number
  unitCode: string
  unitPrice: number
  lineTotal: number
  vatCategoryCode: string
  vatRatePercent: number
}

export interface PeppolPreviewVatGroup {
  vatCategoryCode: string
  vatRatePercent: number
  taxableAmount: number
  vatAmount: number
}

/** Mirrors PeppolInvoicePreviewDto — structured UBL summary, no XML parsing in de UI. */
export interface PeppolInvoicePreview {
  invoiceNumber: string
  kind: string
  invoiceDate: string
  currency: string
  seller: PeppolPreviewParty
  buyer: PeppolPreviewParty
  lines: PeppolPreviewLine[]
  vatGroups: PeppolPreviewVatGroup[]
  subtotal: number
  vatAmount: number
  total: number
  buyerReference: string | null
  purchaseOrderNumber: string | null
  creditedInvoiceNumber: string | null
}

export interface PeppolTransmissionEvent {
  status: string
  timestamp: string
  detail: string | null
}

/** Mirrors PeppolTransmissionDto (detail-shape met events). */
export interface PeppolTransmission {
  id: string
  invoiceId: string
  documentKind: PeppolDocumentKind
  status: string
  environment: string
  providerKey: string
  providerMessageId: string | null
  payloadVersion: number
  payloadHash: string | null
  errorDetail: string | null
  retryCount: number
  createdAt: string
  events: PeppolTransmissionEvent[]
}

export function validateInvoicePeppol(invoiceId: string): Promise<PeppolInvoiceValidationResult> {
  return apiClient.postJson(`/api/invoices/${invoiceId}/peppol/validate`, {})
}

export function getInvoicePeppolPreview(invoiceId: string): Promise<PeppolInvoicePreview> {
  return apiClient.getJson(`/api/invoices/${invoiceId}/peppol/preview`)
}

export function sendInvoicePeppol(invoiceId: string): Promise<PeppolTransmission> {
  return apiClient.postJson(`/api/invoices/${invoiceId}/peppol/send`, {})
}

export function listInvoicePeppolTransmissions(invoiceId: string): Promise<PeppolTransmission[]> {
  return apiClient.getJson(`/api/invoices/${invoiceId}/peppol/transmissions`)
}

// Blob-download valt buiten de JSON-apiClient; de bearer token wordt handmatig meegegeven
// (zelfde idioom als de factuurbijlage-download). Een 400 bevat `{ isValid: false, issues }`.
export async function downloadInvoicePeppolXml(invoiceId: string, invoiceNumber: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/invoices/${invoiceId}/peppol/xml`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    let message = translate(getActiveLocale(), 'peppol.xml.downloadFailed')
    try {
      const data = (await response.json()) as { detail?: string; issues?: { message?: string }[] }
      const firstIssue = data.issues?.find((issue) => typeof issue.message === 'string')
      message = firstIssue?.message ?? data.detail ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `peppol-${invoiceNumber}.xml`
  anchor.click()
  URL.revokeObjectURL(url)
}
