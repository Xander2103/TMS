import { apiClient } from '../../../api/apiClient'
import type { ScanEventEntry, ScanFeedback, ScanType, StopScanSummary } from '../types'

export interface SubmitScanInput {
  scanType: ScanType
  barcode: string
  quantity: number
  damaged: boolean
  damageNote: string | null
  deviceInfo: string | null
  /** Idempotency key: retrying with the same id can never double-register the scan. */
  clientEventId: string
  refused?: boolean
  partial?: boolean
  note?: string | null
}

/**
 * Single entry point for scan submission. This is also the future offline-queue seam: a queued
 * implementation would buffer these calls and replay them; real offline sync is deliberately
 * not implemented yet — the server stays the only source of truth.
 */
export function submitScan(tripId: string, stopId: string, input: SubmitScanInput): Promise<ScanFeedback> {
  return apiClient.postJson<ScanFeedback, SubmitScanInput>(`/api/trips/${tripId}/stops/${stopId}/scans`, input)
}

export interface ScanCorrectionInput {
  cargoItemId: string
  scanType: ScanType
  quantity: number
  reason: string
}

export function correctScan(tripId: string, stopId: string, input: ScanCorrectionInput): Promise<ScanFeedback> {
  return apiClient.postJson<ScanFeedback, ScanCorrectionInput>(
    `/api/trips/${tripId}/stops/${stopId}/scan-corrections`,
    input,
  )
}

export function getStopScanSummary(tripId: string, stopId: string): Promise<StopScanSummary> {
  return apiClient.getJson<StopScanSummary>(`/api/trips/${tripId}/stops/${stopId}/scan-summary`)
}

export function listScans(tripId: string, stopId?: string): Promise<ScanEventEntry[]> {
  const suffix = stopId ? `?stopId=${stopId}` : ''
  return apiClient.getJson<ScanEventEntry[]>(`/api/trips/${tripId}/scans${suffix}`)
}
