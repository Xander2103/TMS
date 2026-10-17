import type { FleetDocumentType } from './types'

/** Legally relevant documents only make sense with the date they stop being valid. */
const EXPIRY_REQUIRED: ReadonlySet<FleetDocumentType> = new Set<FleetDocumentType>([
  'Insurance',
  'TechnicalInspection',
  'AdrCertificate',
  'CraneInspection',
  'RefrigerationCertificate',
  'TachographCalibration',
])

/** Documents without an expiry are identified by their number. */
const NUMBER_REQUIRED: ReadonlySet<FleetDocumentType> = new Set<FleetDocumentType>(['Registration', 'Conformity'])

interface DocumentFields {
  documentType: FleetDocumentType
  customTypeName: string | null
  documentNumber: string | null
  expiryDate: string | null
  issueDate: string | null
}

/**
 * FL-M2: the minimum a document needs to be more than an empty row — mirrors the server rule so
 * the user sees the reason before submitting. Returns a translation key, or null when valid.
 */
export function fleetDocumentValidationKey(doc: DocumentFields): string | null {
  const hasNumber = Boolean(doc.documentNumber?.trim())
  if (doc.documentType === 'Other' && !doc.customTypeName?.trim()) return 'fleet.docs.customNameRequired'
  if (EXPIRY_REQUIRED.has(doc.documentType) && !doc.expiryDate) return 'fleet.docs.expiryRequired'
  if (NUMBER_REQUIRED.has(doc.documentType) && !hasNumber) return 'fleet.docs.numberRequired'
  if (!hasNumber && !doc.expiryDate) return 'fleet.docs.numberOrExpiryRequired'
  if (doc.issueDate && doc.expiryDate && doc.expiryDate < doc.issueDate) return 'fleet.docs.expiryBeforeIssue'
  return null
}
