import { ApiError, apiClient } from '../../api/apiClient'
import { apiBaseUrl } from '../../config/env'
import { getActiveLocale } from '../../i18n/activeLocale'
import { translate } from '../../i18n/translations'
import { getAccessToken } from '../auth/authStorage'

export type LeasingOwnerType = 'vehicle' | 'trailer'

export interface LeasingContract {
  id: string
  vehicleId: string | null
  trailerId: string | null
  leasingCompany: string
  contractNumber: string | null
  startDate: string | null
  endDate: string | null
  /** Financial fields are null unless the caller has fleet_finance.view. */
  monthlyAmount: number | null
  currency: string
  kilometerAllowancePerYear: number | null
  endOfContractMileageKm: number | null
  contactPerson: string | null
  notes: string | null
  hasAttachment: boolean
  fileName: string | null
  isActive: boolean
}

export interface LeasingContractInput {
  leasingCompany: string
  contractNumber: string | null
  startDate: string | null
  endDate: string | null
  monthlyAmount: number | null
  currency: string | null
  kilometerAllowancePerYear: number | null
  endOfContractMileageKm: number | null
  contactPerson: string | null
  notes: string | null
  isActive: boolean
}

function ownerBase(ownerType: LeasingOwnerType, ownerId: string): string {
  return ownerType === 'vehicle'
    ? `/api/vehicles/${ownerId}/leasing-contracts`
    : `/api/trailers/${ownerId}/leasing-contracts`
}

export function listLeasingContracts(ownerType: LeasingOwnerType, ownerId: string): Promise<LeasingContract[]> {
  return apiClient.getJson<LeasingContract[]>(ownerBase(ownerType, ownerId))
}

export function createLeasingContract(
  ownerType: LeasingOwnerType,
  ownerId: string,
  input: LeasingContractInput,
): Promise<LeasingContract> {
  return apiClient.postJson<LeasingContract, LeasingContractInput>(ownerBase(ownerType, ownerId), input)
}

export function updateLeasingContract(id: string, input: LeasingContractInput): Promise<LeasingContract> {
  return apiClient.putJson<LeasingContract, LeasingContractInput>(`/api/leasing-contracts/${id}`, input)
}

export function deleteLeasingContract(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/leasing-contracts/${id}`)
}

async function readError(response: Response, fallback: string): Promise<never> {
  let message = fallback
  try {
    const data = (await response.json()) as { detail?: string; message?: string }
    message = data.detail ?? data.message ?? message
  } catch {
    // keep fallback
  }
  throw new ApiError(message, response.status)
}

export async function uploadLeasingFile(id: string, file: File): Promise<LeasingContract> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/leasing-contracts/${id}/document`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) return readError(response, translate(getActiveLocale(), 'fleet.api.uploadFailed'))
  return (await response.json()) as LeasingContract
}

export async function downloadLeasingFile(id: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/leasing-contracts/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new ApiError(translate(getActiveLocale(), 'fleet.api.downloadFailed'), response.status)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
