import { ApiError, apiClient } from '../../api/apiClient'
import { apiBaseUrl } from '../../config/env'
import { getAccessToken } from '../auth/authStorage'

export type TachographStatus = 'Valid' | 'ExpiringSoon' | 'Overdue'

export const TACHOGRAPH_STATUS_LABELS: Record<TachographStatus, string> = {
  Valid: 'Geldig',
  ExpiringSoon: 'Verloopt binnenkort',
  Overdue: 'Vervallen',
}

export interface TachographCalibration {
  id: string
  vehicleId: string
  tachographType: string | null
  manufacturer: string | null
  model: string | null
  serialNumber: string | null
  calibrationDate: string
  nextCalibrationDue: string
  workshop: string | null
  certificateNumber: string | null
  sealReference: string | null
  odometerKm: number | null
  tyreCircumferenceMm: number | null
  notes: string | null
  hasAttachment: boolean
  fileName: string | null
  status: TachographStatus
}

export interface TachographInput {
  tachographType: string | null
  manufacturer: string | null
  model: string | null
  serialNumber: string | null
  calibrationDate: string
  nextCalibrationDue: string
  workshop: string | null
  certificateNumber: string | null
  sealReference: string | null
  odometerKm: number | null
  tyreCircumferenceMm: number | null
  notes: string | null
}

const base = (vehicleId: string) => `/api/vehicles/${vehicleId}/tachograph-calibrations`

export function listTachographCalibrations(vehicleId: string): Promise<TachographCalibration[]> {
  return apiClient.getJson<TachographCalibration[]>(base(vehicleId))
}

export function createTachographCalibration(vehicleId: string, input: TachographInput): Promise<TachographCalibration> {
  return apiClient.postJson<TachographCalibration, TachographInput>(base(vehicleId), input)
}

export function updateTachographCalibration(vehicleId: string, id: string, input: TachographInput): Promise<TachographCalibration> {
  return apiClient.putJson<TachographCalibration, TachographInput>(`${base(vehicleId)}/${id}`, input)
}

export function deleteTachographCalibration(vehicleId: string, id: string): Promise<void> {
  return apiClient.deleteRequest(`${base(vehicleId)}/${id}`)
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

export async function uploadTachographFile(vehicleId: string, id: string, file: File): Promise<TachographCalibration> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}${base(vehicleId)}/${id}/document`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) return readError(response, 'Uploaden is mislukt.')
  return (await response.json()) as TachographCalibration
}

export async function downloadTachographFile(vehicleId: string, id: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}${base(vehicleId)}/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new ApiError('Bestand kon niet worden gedownload.', response.status)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
