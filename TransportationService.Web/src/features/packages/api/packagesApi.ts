import { apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type {
  BulkCreateInput,
  CreatePackageInput,
  ImportCommit,
  ImportPreview,
  Package,
  PackageBarcodeRow,
  PackageIncidentAction,
  PackageLabelVersion,
  PackageTimelineEvent,
  TripPackageChecklist,
  TripPackageReadiness,
} from '../types'

export function getPackageTimeline(id: string): Promise<PackageTimelineEvent[]> {
  return apiClient.getJson<PackageTimelineEvent[]>(`/api/packages/${id}/events`)
}

export function getPackageBarcodes(id: string): Promise<PackageBarcodeRow[]> {
  return apiClient.getJson<PackageBarcodeRow[]>(`/api/packages/${id}/barcodes`)
}

export function listLabelVersions(id: string): Promise<PackageLabelVersion[]> {
  return apiClient.getJson<PackageLabelVersion[]>(`/api/packages/${id}/labels`)
}

export async function downloadLabelVersion(id: string, version: number): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/packages/${id}/labels/${version}/pdf`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    // Geen user-facing tekst hier: de aanroepende component toont zijn eigen vertaalde fallback.
    throw new Error('label download failed')
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `etiket-v${version}.pdf`
  anchor.click()
  URL.revokeObjectURL(url)
}

export function getWarehouseTrips(date?: string): Promise<import('../types').WarehouseTrip[]> {
  const suffix = date ? `?date=${date}` : ''
  return apiClient.getJson(`/api/warehouse/trips${suffix}`)
}

export function searchWarehousePackages(search: string): Promise<import('../types').WarehousePackageRow[]> {
  return apiClient.getJson(`/api/warehouse/packages?search=${encodeURIComponent(search)}`)
}

export function getTripPackageChecklist(tripId: string, stopId?: string): Promise<TripPackageChecklist> {
  const suffix = stopId ? `?stopId=${stopId}` : ''
  return apiClient.getJson<TripPackageChecklist>(`/api/trips/${tripId}/packages${suffix}`)
}

export function getTripPackageReadiness(tripId: string): Promise<TripPackageReadiness> {
  return apiClient.getJson<TripPackageReadiness>(`/api/trips/${tripId}/package-readiness`)
}

export function markPackageMissing(tripId: string, packageId: string, stopId: string, note: string | null): Promise<void> {
  return apiClient.postJson(`/api/trips/${tripId}/packages/${packageId}/missing`, { stopId, note })
}

export function resolvePackageIncident(packageId: string, action: PackageIncidentAction, note: string): Promise<void> {
  return apiClient.postJson(`/api/packages/${packageId}/resolve-incident`, { action, note })
}

export function listOrderPackages(orderId: string): Promise<Package[]> {
  return apiClient.getJson<Package[]>(`/api/transport-orders/${orderId}/packages`)
}

export function getPackage(id: string): Promise<Package> {
  return apiClient.getJson<Package>(`/api/packages/${id}`)
}

export function createPackage(orderId: string, input: CreatePackageInput): Promise<Package> {
  return apiClient.postJson<Package, CreatePackageInput>(`/api/transport-orders/${orderId}/packages`, input)
}

export function bulkCreatePackages(
  orderId: string,
  input: BulkCreateInput,
): Promise<{ packages: Package[]; pallet: Package | null }> {
  return apiClient.postJson<{ packages: Package[]; pallet: Package | null }, BulkCreateInput>(
    `/api/transport-orders/${orderId}/packages/bulk`,
    input,
  )
}

export interface PackageGenerationResult {
  created: number
  cancelled: number
  unchanged: number
  requiresAttention: number
  createdPackages: Package[]
  message: string | null
}

/** Idempotent cargo-driven generation (also runs automatically when the order is confirmed). */
export function generatePackages(orderId: string): Promise<PackageGenerationResult> {
  return apiClient.postJson<PackageGenerationResult, Record<string, never>>(
    `/api/transport-orders/${orderId}/packages/generate`,
    {},
  )
}

export function cancelPackage(id: string, reason: string): Promise<Package> {
  return apiClient.postJson<Package, { reason: string }>(`/api/packages/${id}/cancel`, { reason })
}

export function relabelPackage(id: string, reason: string): Promise<Package> {
  return apiClient.postJson<Package, { reason: string }>(`/api/packages/${id}/relabel`, { reason })
}

export async function downloadImportTemplate(): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/package-import/template`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new Error('template download failed')
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'colli-import-sjabloon.xlsx'
  anchor.click()
  URL.revokeObjectURL(url)
}

async function postWorkbook<T>(path: string, file: File): Promise<T> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body: form,
  })
  const data = (await response.json().catch(() => null)) as T | { message?: string } | null
  if (!response.ok) {
    // Servermessage wint; zonder message vertaalt de component zijn eigen fallback (lege
    // message → describeApiError kiest de fallback).
    throw new Error((data as { message?: string } | null)?.message ?? '')
  }

  return data as T
}

export function previewImport(orderId: string, file: File): Promise<ImportPreview> {
  return postWorkbook<ImportPreview>(`/api/transport-orders/${orderId}/packages/import/preview`, file)
}

export function commitImport(
  orderId: string,
  file: File,
  allOrNothing: boolean,
  allowUpdates: boolean,
): Promise<ImportCommit> {
  return postWorkbook<ImportCommit>(
    `/api/transport-orders/${orderId}/packages/import?allOrNothing=${allOrNothing}&allowUpdates=${allowUpdates}`,
    file,
  )
}

export async function printLabels(
  packageIds: string[],
  format: 'Thermal100x150' | 'A4',
  reprintReason: string | null,
): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/packages/labels`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${getAccessToken() ?? ''}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ packageIds, format, reprintReason }),
  })
  if (!response.ok) {
    const data = (await response.json().catch(() => null)) as { message?: string } | null
    // Servermessage wint; zonder message vertaalt de component zijn eigen fallback.
    throw new Error(data?.message ?? '')
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'etiketten.pdf'
  anchor.click()
  URL.revokeObjectURL(url)
}

export function downloadErrorWorkbook(base64: string): void {
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
  const blob = new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'colli-import-fouten.xlsx'
  anchor.click()
  URL.revokeObjectURL(url)
}
