import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

export type EmployeeDocumentCategory =
  | 'IdentityCardFront'
  | 'IdentityCardBack'
  | 'DrivingLicenceFront'
  | 'DrivingLicenceBack'
  | 'EmploymentDocument'
  | 'MedicalDocument'
  | 'Certificate'
  | 'Contract'
  | 'AdditionalDocument'
  | 'Other'

export const EMPLOYEE_DOCUMENT_CATEGORY_LABELS: Record<EmployeeDocumentCategory, string> = {
  IdentityCardFront: 'Identiteitskaart (voorzijde)',
  IdentityCardBack: 'Identiteitskaart (achterzijde)',
  DrivingLicenceFront: 'Rijbewijs (voorzijde)',
  DrivingLicenceBack: 'Rijbewijs (achterzijde)',
  EmploymentDocument: 'Tewerkstellingsdocument',
  MedicalDocument: 'Medisch document',
  Certificate: 'Attest',
  Contract: 'Contract',
  AdditionalDocument: 'Bijkomend document',
  Other: 'Overige',
}

/** Order used for grouping the document grid. */
export const EMPLOYEE_DOCUMENT_CATEGORY_ORDER: EmployeeDocumentCategory[] = [
  'IdentityCardFront',
  'IdentityCardBack',
  'DrivingLicenceFront',
  'DrivingLicenceBack',
  'EmploymentDocument',
  'Contract',
  'MedicalDocument',
  'Certificate',
  'AdditionalDocument',
  'Other',
]

/** Allowed upload types, mirrored on the file inputs. */
export const EMPLOYEE_DOCUMENT_ACCEPT = '.pdf,.jpg,.jpeg,.png'

/** Mirrors EmployeeDocumentDto (camelCase JSON). */
export interface EmployeeDocument {
  id: string
  category: EmployeeDocumentCategory
  customLabel: string | null
  fileName: string
  contentType: string
  sizeBytes: number
  expiryDate: string | null
  notes: string | null
  isArchived: boolean
  isSensitive: boolean
  uploadedAt: string
  uploadedByUserId: string | null
}

export interface UpdateEmployeeDocumentInput {
  category: EmployeeDocumentCategory
  customLabel: string | null
  expiryDate: string | null
  notes: string | null
}

export interface UploadEmployeeDocumentInput {
  file: File
  category: EmployeeDocumentCategory
  customLabel?: string | null
  expiryDate?: string | null
  notes?: string | null
}

export function listEmployeeDocuments(employeeId: string, includeArchived = false): Promise<EmployeeDocument[]> {
  const query = includeArchived ? '?includeArchived=true' : ''
  return apiClient.getJson<EmployeeDocument[]>(`/api/employees/${employeeId}/documents${query}`)
}

export function updateEmployeeDocument(
  employeeId: string,
  documentId: string,
  input: UpdateEmployeeDocumentInput,
): Promise<EmployeeDocument> {
  return apiClient.putJson<EmployeeDocument, UpdateEmployeeDocumentInput>(
    `/api/employees/${employeeId}/documents/${documentId}`,
    input,
  )
}

export function setEmployeeDocumentArchived(
  employeeId: string,
  documentId: string,
  archived: boolean,
): Promise<EmployeeDocument> {
  return apiClient.postJson<EmployeeDocument, { archived: boolean }>(
    `/api/employees/${employeeId}/documents/${documentId}/archived`,
    { archived },
  )
}

export function deleteEmployeeDocument(employeeId: string, documentId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/employees/${employeeId}/documents/${documentId}`)
}

// Multipart upload/replace and blob download fall outside the JSON apiClient; each attaches
// the bearer token manually (same idiom as the qualification-document upload).
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

export async function uploadEmployeeDocument(
  employeeId: string,
  input: UploadEmployeeDocumentInput,
): Promise<EmployeeDocument> {
  const body = new FormData()
  body.append('file', input.file)
  body.append('category', input.category)
  if (input.customLabel) body.append('customLabel', input.customLabel)
  if (input.expiryDate) body.append('expiryDate', input.expiryDate)
  if (input.notes) body.append('notes', input.notes)
  const response = await fetch(`${apiBaseUrl}/api/employees/${employeeId}/documents`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) return readError(response, 'Uploaden is mislukt.')
  return (await response.json()) as EmployeeDocument
}

export async function replaceEmployeeDocumentFile(
  employeeId: string,
  documentId: string,
  file: File,
): Promise<EmployeeDocument> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/employees/${employeeId}/documents/${documentId}/file`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) return readError(response, 'Vervangen is mislukt.')
  return (await response.json()) as EmployeeDocument
}

export async function downloadEmployeeDocument(
  employeeId: string,
  documentId: string,
  fileName: string,
): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/employees/${employeeId}/documents/${documentId}/content`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('Document kon niet worden gedownload.', response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
