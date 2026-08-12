import { ApiError, apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

export interface OrderImportProfile {
  id: string
  name: string
  description: string | null
  mappingJson: string
  isActive: boolean
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

export function listOrderImportProfiles(): Promise<OrderImportProfile[]> {
  return apiClient.getJson<OrderImportProfile[]>('/api/order-imports/profiles')
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
    let message = 'De import is mislukt.'
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
