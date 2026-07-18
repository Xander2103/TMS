export type FleetDocumentType =
  | 'Registration'
  | 'Insurance'
  | 'TechnicalInspection'
  | 'Conformity'
  | 'CraneInspection'
  | 'RefrigerationCertificate'
  | 'AdrCertificate'
  | 'Other'

export type FleetDocumentStatus = 'NoExpiry' | 'Valid' | 'ExpiringSoon' | 'Expired'

export const FLEET_DOCUMENT_TYPE_LABELS: Record<FleetDocumentType, string> = {
  Registration: 'Inschrijvingsbewijs',
  Insurance: 'Verzekering',
  TechnicalInspection: 'Technische keuring',
  Conformity: 'Conformiteitsattest',
  CraneInspection: 'Kraankeuring',
  RefrigerationCertificate: 'Koelcertificaat',
  AdrCertificate: 'ADR-certificaat',
  Other: 'Overig',
}

export const FLEET_DOCUMENT_TYPES = Object.keys(FLEET_DOCUMENT_TYPE_LABELS) as FleetDocumentType[]

export const FLEET_DOCUMENT_STATUS_LABELS: Record<FleetDocumentStatus, string> = {
  NoExpiry: 'Geen vervaldatum',
  Valid: 'Geldig',
  ExpiringSoon: 'Verloopt binnenkort',
  Expired: 'Verlopen',
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
  notes: string | null
}

export interface FleetDocumentInput {
  documentType: FleetDocumentType
  customTypeName: string | null
  documentNumber: string | null
  issueDate: string | null
  expiryDate: string | null
  warningDays: number | null
  notes: string | null
}

export function fleetDocumentDisplayName(doc: Pick<FleetDocument, 'documentType' | 'customTypeName'>): string {
  return doc.documentType === 'Other' && doc.customTypeName
    ? doc.customTypeName
    : FLEET_DOCUMENT_TYPE_LABELS[doc.documentType]
}
