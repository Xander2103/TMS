import { ApiError, apiClient } from '../../api/apiClient'
import { apiBaseUrl } from '../../config/env'
import { getAccessToken } from '../auth/authStorage'

export type IssuedItemStatus = 'NotIssued' | 'Issued' | 'Returned' | 'Missing' | 'Damaged'

export const ISSUED_ITEM_STATUS_LABELS: Record<IssuedItemStatus, string> = {
  NotIssued: 'Niet uitgereikt',
  Issued: 'Uitgereikt',
  Returned: 'Teruggebracht',
  Missing: 'Ontbreekt',
  Damaged: 'Beschadigd',
}

export const ISSUED_ITEM_STATUSES: IssuedItemStatus[] = ['NotIssued', 'Issued', 'Returned', 'Missing', 'Damaged']

/** Structured return outcome; only "good" may restore usable stock. */
export type ReturnDisposition = 'good' | 'damaged' | 'lost' | 'disposed'

export const RETURN_DISPOSITION_LABELS: Record<ReturnDisposition, string> = {
  good: 'Goede staat',
  damaged: 'Beschadigd',
  lost: 'Verloren',
  disposed: 'Afgevoerd',
}

export interface IssuedItemTemplate {
  id: string
  name: string
  category: string
  categoryId: string | null
  applicableJobFunctionCodes: string | null
  defaultQuantity: number
  requiresSerialNumber: boolean
  requiresReceivedDate: boolean
  returnRequired: boolean
  isActive: boolean
  sortOrder: number
  description: string | null
  unit: string | null
  notes: string | null
  stockTrackingEnabled: boolean
  variantsEnabled: boolean
  allowNegativeStock: boolean
  lowStockThreshold: number | null
  minimumStock: number | null
  storageLocation: string | null
  currentStock: number
  totalAvailable: number
  lowStock: boolean
  variantCount: number
}

export interface IssuedItemTemplateInput {
  name: string
  category: string
  categoryId: string | null
  applicableJobFunctionCodes: string | null
  defaultQuantity: number
  requiresSerialNumber: boolean
  requiresReceivedDate: boolean
  returnRequired: boolean
  isActive: boolean
  sortOrder: number
  description: string | null
  unit: string | null
  notes: string | null
  stockTrackingEnabled: boolean
  variantsEnabled: boolean
  allowNegativeStock: boolean
  lowStockThreshold: number | null
  minimumStock: number | null
  storageLocation: string | null
  /** Target physical stock; applied through the movement ledger server-side. */
  stock?: number | null
  /** Required when changing existing stock (ledger correction reason). */
  stockCorrectionReason?: string | null
}

export interface EmployeeIssuedItem {
  id: string
  templateId: string | null
  name: string
  category: string
  status: IssuedItemStatus
  issuedDate: string | null
  quantity: number
  serialNumber: string | null
  notes: string | null
  issuedByUserId: string | null
  returnedDate: string | null
  returnCondition: string | null
  receivedBackByUserId: string | null
  variantId: string | null
  variantLabel: string | null
}

export interface EmployeeIssuedItemInput {
  templateId: string | null
  name: string | null
  category: string | null
  status: IssuedItemStatus
  issuedDate: string | null
  quantity: number
  serialNumber: string | null
  notes: string | null
  returnedDate: string | null
  returnCondition: string | null
  variantId?: string | null
  returnDisposition?: ReturnDisposition | null
  restoreStock?: boolean | null
  overrideInsufficientStock?: boolean
  overrideReason?: string | null
}

// ---- Templates ----
export function listIssuedItemTemplates(includeInactive = false): Promise<IssuedItemTemplate[]> {
  const query = includeInactive ? '?includeInactive=true' : ''
  return apiClient.getJson<IssuedItemTemplate[]>(`/api/issued-item-templates${query}`)
}

export function createIssuedItemTemplate(input: IssuedItemTemplateInput): Promise<IssuedItemTemplate> {
  return apiClient.postJson<IssuedItemTemplate, IssuedItemTemplateInput>('/api/issued-item-templates', input)
}

export function updateIssuedItemTemplate(id: string, input: IssuedItemTemplateInput): Promise<IssuedItemTemplate> {
  return apiClient.putJson<IssuedItemTemplate, IssuedItemTemplateInput>(`/api/issued-item-templates/${id}`, input)
}

export function deleteIssuedItemTemplate(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/issued-item-templates/${id}`)
}

// ---- Employee items ----
export function listEmployeeIssuedItems(employeeId: string): Promise<EmployeeIssuedItem[]> {
  return apiClient.getJson<EmployeeIssuedItem[]>(`/api/employees/${employeeId}/issued-items`)
}

export function saveEmployeeIssuedItem(
  employeeId: string,
  itemId: string | null,
  input: EmployeeIssuedItemInput,
): Promise<EmployeeIssuedItem> {
  return itemId
    ? apiClient.putJson<EmployeeIssuedItem, EmployeeIssuedItemInput>(`/api/employees/${employeeId}/issued-items/${itemId}`, input)
    : apiClient.postJson<EmployeeIssuedItem, EmployeeIssuedItemInput>(`/api/employees/${employeeId}/issued-items`, input)
}

export function deleteEmployeeIssuedItem(employeeId: string, itemId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/employees/${employeeId}/issued-items/${itemId}`)
}

export async function downloadIssuedItemsAcknowledgement(employeeId: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/employees/${employeeId}/issued-items/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new ApiError('Het ontvangstbewijs kon niet worden gedownload.', response.status)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'ontvangstbewijs-bedrijfsmiddelen.pdf'
  anchor.click()
  URL.revokeObjectURL(url)
}
