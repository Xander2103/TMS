export type FleetDocumentType =
  | 'Registration'
  | 'Insurance'
  | 'TechnicalInspection'
  | 'Conformity'
  | 'CraneInspection'
  | 'RefrigerationCertificate'
  | 'AdrCertificate'
  | 'LeasingContract'
  | 'TachographCalibration'
  | 'Other'

export type FleetDocumentStatus = 'NoExpiry' | 'Valid' | 'ExpiringSoon' | 'Expired'

/** i18n-keys (fleet.docs.type.*) — render via t(FLEET_DOCUMENT_TYPE_LABELS[x]). */
export const FLEET_DOCUMENT_TYPE_LABELS: Record<FleetDocumentType, string> = {
  Registration: 'fleet.docs.type.Registration',
  Insurance: 'fleet.docs.type.Insurance',
  TechnicalInspection: 'fleet.docs.type.TechnicalInspection',
  Conformity: 'fleet.docs.type.Conformity',
  CraneInspection: 'fleet.docs.type.CraneInspection',
  RefrigerationCertificate: 'fleet.docs.type.RefrigerationCertificate',
  AdrCertificate: 'fleet.docs.type.AdrCertificate',
  LeasingContract: 'fleet.docs.type.LeasingContract',
  TachographCalibration: 'fleet.docs.type.TachographCalibration',
  Other: 'fleet.docs.type.Other',
}

export const FLEET_DOCUMENT_TYPES = Object.keys(FLEET_DOCUMENT_TYPE_LABELS) as FleetDocumentType[]

/** i18n-keys (fleet.docs.status.*) — render via t(FLEET_DOCUMENT_STATUS_LABELS[x]). */
export const FLEET_DOCUMENT_STATUS_LABELS: Record<FleetDocumentStatus, string> = {
  NoExpiry: 'fleet.docs.status.NoExpiry',
  Valid: 'fleet.docs.status.Valid',
  ExpiringSoon: 'fleet.docs.status.ExpiringSoon',
  Expired: 'fleet.docs.status.Expired',
}

export interface FleetDocument {
  id: string
  vehicleId: string | null
  trailerId: string | null
  documentType: FleetDocumentType
  customTypeName: string | null
  documentNumber: string | null
  issueDate: string | null
  expiryDate: string | null
  warningDays: number | null
  status: FleetDocumentStatus
  hasAttachment: boolean
  fileName: string | null
  issuingAuthority: string | null
  notes: string | null
}

export interface FleetDocumentInput {
  documentType: FleetDocumentType
  customTypeName: string | null
  documentNumber: string | null
  issueDate: string | null
  expiryDate: string | null
  warningDays: number | null
  issuingAuthority: string | null
  notes: string | null
}

/**
 * Display name: either the custom name (data, rendered as-is) or a translation KEY from
 * FLEET_DOCUMENT_TYPE_LABELS. Callers render via t(fleetDocumentDisplayName(doc)) — t()
 * echoes unknown keys, so custom names pass through unchanged.
 */
export function fleetDocumentDisplayName(doc: Pick<FleetDocument, 'documentType' | 'customTypeName'>): string {
  return doc.documentType === 'Other' && doc.customTypeName
    ? doc.customTypeName
    : FLEET_DOCUMENT_TYPE_LABELS[doc.documentType]
}
