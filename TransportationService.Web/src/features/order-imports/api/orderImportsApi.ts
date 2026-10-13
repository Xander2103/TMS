import { ApiError, apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import { apiBaseUrl } from '../../../config/env'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
import { getAccessToken } from '../../auth/authStorage'

export interface OrderImportProfile {
  id: string
  name: string
  description: string | null
  mappingJson: string
  isActive: boolean
  /** Optional customer binding (null = generic profile, usable for every customer). */
  customerId: string | null
  customerName: string | null
  /** Parsed mapping: field key → column letter ("A"), for the editor. */
  mapping: Record<string, string> | null
  /** Sample-file headers in column order; lets the editor open without a new upload. */
  sourceHeaders: string[] | null
  mappedFieldCount: number
  updatedAt: string
}

/** One importable TMS target field (stable key + picker group; labels are client-side i18n). */
export interface OrderImportField {
  key: string
  group: string
}

export interface OrderImportColumnAnalysis {
  columnIndex: number
  header: string
  sampleValues: string[]
  suggestedField: string | null
  confidence: number | null
}

/** How well a SAVED profile's stored headers match the uploaded file's headers. */
export interface OrderImportProfileMatch {
  profileId: string
  name: string
  customerId: string | null
  matchPercent: number
}

export interface OrderImportAnalysis {
  columns: OrderImportColumnAnalysis[]
  profileMatches: OrderImportProfileMatch[]
}

export interface SaveOrderImportProfileInput {
  name: string
  description: string | null
  customerId: string | null
  isActive: boolean
  headerRows: number
  /** field key → column reference (letter or 1-based index as string). */
  mapping: Record<string, string>
  sourceHeaders: string[] | null
}

export type OrderImportBatchStatus = 'Validated' | 'Processed' | 'Failed'
export type OrderImportRowStatus = 'Created' | 'Skipped' | 'Error'

export interface OrderImportBatch {
  id: string
  profileId: string
  profileName: string
  customerId: string
  customerName: string
  fileName: string
  status: OrderImportBatchStatus
  rowCount: number
  successCount: number
  failureCount: number
  dryRun: boolean
  createdAt: string
}

export interface OrderImportRow {
  rowNumber: number
  status: OrderImportRowStatus
  error: string | null
  createdTransportOrderId: string | null
  externalReference: string | null
}

export interface OrderImportBatchDetail {
  batch: OrderImportBatch
  rows: OrderImportRow[]
}

export function listOrderImportProfiles(includeInactive = false): Promise<OrderImportProfile[]> {
  return apiClient.getJson<OrderImportProfile[]>(
    `/api/order-imports/profiles${includeInactive ? '?includeInactive=true' : ''}`,
  )
}

export function listOrderImportFields(): Promise<OrderImportField[]> {
  return apiClient.getJson<OrderImportField[]>('/api/order-imports/fields')
}

export function createOrderImportProfile(input: SaveOrderImportProfileInput): Promise<OrderImportProfile> {
  return apiClient.postJson<OrderImportProfile, SaveOrderImportProfileInput>('/api/order-imports/profiles', input)
}

export function updateOrderImportProfile(id: string, input: SaveOrderImportProfileInput): Promise<OrderImportProfile> {
  return apiClient.putJson<OrderImportProfile, SaveOrderImportProfileInput>(`/api/order-imports/profiles/${id}`, input)
}

export function deleteOrderImportProfile(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/order-imports/profiles/${id}`)
}

/** Reads a sample workbook's headers + example values; never persists anything (multipart, see upload note). */
export async function analyzeOrderImportFile(file: File): Promise<OrderImportAnalysis> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/order-imports/analyze`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    let message = translate(getActiveLocale(), 'orderImports.profileEditor.analyzeFailed')
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as OrderImportAnalysis
}

export function listOrderImportBatches(page: number, pageSize: number): Promise<PagedResult<OrderImportBatch>> {
  return apiClient.getJson<PagedResult<OrderImportBatch>>(`/api/order-imports?page=${page}&pageSize=${pageSize}`)
}

export function getOrderImportBatch(id: string): Promise<OrderImportBatchDetail> {
  return apiClient.getJson<OrderImportBatchDetail>(`/api/order-imports/${id}`)
}

/** Multipart upload falls outside the JSON apiClient; attaches the bearer token manually. */
export async function uploadOrderImport(input: {
  file: File
  profileId: string
  customerId: string
  dryRun: boolean
}): Promise<OrderImportBatchDetail> {
  const body = new FormData()
  body.append('file', input.file)
  body.append('profileId', input.profileId)
  body.append('customerId', input.customerId)
  body.append('dryRun', String(input.dryRun))
  const response = await fetch(`${apiBaseUrl}/api/order-imports`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    let message = translate(getActiveLocale(), 'orderImports.form.uploadFailed')
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as OrderImportBatchDetail
}
