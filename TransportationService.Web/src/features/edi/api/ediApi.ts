import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'

export type EdiDirection = 'Inbound' | 'Outbound'
export type EdiStatus = 'Received' | 'Processed' | 'Failed' | 'DeadLettered' | 'Duplicate'

export interface EdiMessageRow {
  id: string
  direction: EdiDirection
  partnerCode: string
  messageType: string
  externalReference: string | null
  status: EdiStatus
  attemptCount: number
  processedAt: string | null
  errorDetail: string | null
  mappingIssue: boolean
  resultEntityType: string | null
  resultEntityId: string | null
  createdAt: string
}

export interface EdiMessageDetail extends EdiMessageRow {
  validationErrors: string[] | null
  payloadJson: string
}

export interface EdiPartnerLocationMapping {
  id: string
  externalLocationCode: string
  locationId: string
  locationName: string
}

export interface EdiPartner {
  id: string
  code: string
  name: string
  customerId: string | null
  customerName: string | null
  externalCustomerIdentifier: string | null
  mappingProfile: string
  isActive: boolean
  notes: string | null
  locations: EdiPartnerLocationMapping[]
}

export interface CreatePartnerInput {
  code: string
  name: string
  customerId: string | null
  externalCustomerIdentifier: string | null
  isActive: boolean
}

export interface UpdatePartnerInput {
  name: string
  customerId: string | null
  externalCustomerIdentifier: string | null
  mappingProfile: string
  isActive: boolean
  notes: string | null
}

export interface EdiStats {
  perStatus: Record<string, number>
  failed: number
  deadLettered: number
  mappingIssues: number
  processedLast7Days: number
  totalPartners: number
  partnersWithoutCustomer: number
}

export interface EdiValidationSummary {
  externalOrderId: string
  customerReference: string | null
  goodsDescription: string
  stopCount: number
  cargoLineCount: number
  resolvedLocationCodes: string[]
  resolvedUnitCodes: string[]
}

export interface EdiValidationResult {
  valid: boolean
  errors: string[]
  wouldCreate: EdiValidationSummary | null
}

export interface ListMessagesParams {
  direction?: EdiDirection
  status?: EdiStatus
  partnerId?: string
  mappingIssues?: boolean
  search?: string
  page?: number
  pageSize?: number
}

export function listMessages(params: ListMessagesParams): Promise<PagedResult<EdiMessageRow>> {
  const query = new URLSearchParams()
  if (params.direction) query.set('direction', params.direction)
  if (params.status) query.set('status', params.status)
  if (params.partnerId) query.set('partnerId', params.partnerId)
  if (params.mappingIssues) query.set('mappingIssues', 'true')
  if (params.search) query.set('search', params.search)
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 25))
  return apiClient.getJson(`/api/edi/messages?${query.toString()}`)
}

export function getMessage(id: string): Promise<EdiMessageDetail> {
  return apiClient.getJson(`/api/edi/messages/${id}`)
}

export function replayMessage(id: string): Promise<{ id: string; status: EdiStatus; errorDetail: string | null }> {
  return apiClient.postJson(`/api/edi/messages/${id}/replay`, {})
}

export function listPartners(): Promise<EdiPartner[]> {
  return apiClient.getJson('/api/edi/partners')
}

export function createPartner(input: CreatePartnerInput): Promise<{ id: string }> {
  return apiClient.postJson('/api/edi/partners', input)
}

export function updatePartner(id: string, input: UpdatePartnerInput): Promise<{ id: string }> {
  return apiClient.putJson(`/api/edi/partners/${id}`, input)
}

export function addLocationMapping(
  partnerId: string,
  input: { externalLocationCode: string; locationId: string },
): Promise<void> {
  return apiClient.postJson(`/api/edi/partners/${partnerId}/locations`, input)
}

export function deleteLocationMapping(partnerId: string, mappingId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/edi/partners/${partnerId}/locations/${mappingId}`)
}

export function getStats(): Promise<EdiStats> {
  return apiClient.getJson('/api/edi/stats')
}

export function validatePayload(input: {
  partnerCode: string
  messageType: string
  payload: string
}): Promise<EdiValidationResult> {
  return apiClient.postJson('/api/edi/validate', input)
}

export function simulate(partnerCode: string): Promise<EdiMessageRow> {
  return apiClient.postJson('/api/edi/simulate', { partnerCode })
}

/** Vertaalsleutels — renderen als t(STATUS_LABELS[status]). */
export const STATUS_LABELS: Record<EdiStatus, string> = {
  Received: 'edi.status.Received',
  Processed: 'edi.status.Processed',
  Failed: 'edi.status.Failed',
  DeadLettered: 'edi.status.DeadLettered',
  Duplicate: 'edi.status.Duplicate',
}

export const STATUS_TONE: Record<EdiStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Received: 'info',
  Processed: 'success',
  Failed: 'danger',
  DeadLettered: 'danger',
  Duplicate: 'neutral',
}

/** The generic-json-v1 sample payload shape (mirrors EdiController.SimulatedPayload), for the Testen tab's prefilled textarea. */
export const SAMPLE_PAYLOAD = JSON.stringify(
  {
    externalOrderId: 'TEST-0001',
    customerReference: 'SIM-REF',
    goodsDescription: 'Simulatie-opdracht via EDI',
    stops: [
      { type: 'Loading', city: 'Antwerpen' },
      { type: 'Unloading', city: 'Gent' },
    ],
    cargoItems: [{ description: 'Simulatiepallet', quantity: 4 }],
  },
  null,
  2,
)
