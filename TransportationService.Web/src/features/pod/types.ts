export type PodOutcome = 'Complete' | 'Partial' | 'Refused'

export type PodPhotoCategory = 'Delivery' | 'Package' | 'Document'

export const POD_OUTCOME_LABELS: Record<PodOutcome, string> = {
  Complete: 'Volledig geleverd',
  Partial: 'Gedeeltelijk geleverd',
  Refused: 'Geweigerd',
}

export const POD_OUTCOME_TONE: Record<PodOutcome, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Complete: 'success',
  Partial: 'warning',
  Refused: 'danger',
}

export const POD_OUTCOME_ICONS: Record<PodOutcome, string> = {
  Complete: '✓',
  Partial: '◐',
  Refused: '✕',
}

export const POD_PHOTO_CATEGORY_LABELS: Record<PodPhotoCategory, string> = {
  Delivery: 'Levering',
  Package: 'Colli',
  Document: 'Documenten',
}

export interface PodScanLine {
  description: string
  barcode: string | null
  expectedQuantity: number
  scannedQuantity: number
  damagedQuantity: number
  state: string
}

export interface PodPhoto {
  id: string
  category: PodPhotoCategory
  fileName: string
  contentType: string
  createdAt: string
}

export interface PodVersion {
  id: string
  version: number
  isCurrent: boolean
  deliveredAt: string
  outcome: PodOutcome
  correctionReason: string | null
}

export interface PodDetail {
  id: string
  version: number
  isCurrent: boolean
  tripId: string
  tripNumber: string
  transportOrderStopId: string
  stopLabel: string | null
  transportOrderId: string
  orderNumber: string | null
  customerName: string | null
  recipientName: string
  recipientRole: string | null
  outcome: PodOutcome
  damageReported: boolean
  missingReported: boolean
  notes: string | null
  deliveredAt: string
  latitude: number | null
  longitude: number | null
  hasSignature: boolean
  scannedSummary: PodScanLine[]
  finalisedByName: string | null
  driverName: string | null
  correctionReason: string | null
  correctedFromPodId: string | null
  customerVisible: boolean
  photos: PodPhoto[]
  versions: PodVersion[]
}

export interface FinalizePodInput {
  recipientName: string
  recipientRole: string | null
  outcome: PodOutcome
  damageReported: boolean
  missingReported: boolean
  notes: string | null
  signatureBase64: string | null
  latitude: number | null
  longitude: number | null
}

export interface CorrectPodInput extends FinalizePodInput {
  reason: string
}
