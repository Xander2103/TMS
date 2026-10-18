import { apiClient } from '../../api/apiClient'
import type { InventoryStatus } from './inventoryStatus'
import { issuedItemUnitForForm } from './issuedItemUnits'

/** `Consumed` (WH-M5): a consumable handed out for good — never an open loan, nothing to return. */
export type IssuedItemStatus = 'NotIssued' | 'Issued' | 'Returned' | 'Missing' | 'Damaged' | 'Consumed'

/** Vertaalsleutels — renderen als t(ISSUED_ITEM_STATUS_LABELS[status]). */
export const ISSUED_ITEM_STATUS_LABELS: Record<IssuedItemStatus, string> = {
  NotIssued: 'issuedItems.status.NotIssued',
  Issued: 'issuedItems.status.Issued',
  Returned: 'issuedItems.status.Returned',
  Missing: 'issuedItems.status.Missing',
  Damaged: 'issuedItems.status.Damaged',
  Consumed: 'issuedItems.status.Consumed',
}

/** Statuses a person may pick by hand in the employee form; Consumed is set by the server only. */
export const ISSUED_ITEM_STATUSES: IssuedItemStatus[] = ['NotIssued', 'Issued', 'Returned', 'Missing', 'Damaged']

/** Structured return outcome; only "good" may restore usable stock. */
export type ReturnDisposition = 'good' | 'damaged' | 'lost' | 'disposed'

/** Vertaalsleutels — renderen als t(RETURN_DISPOSITION_LABELS[disposition]). */
export const RETURN_DISPOSITION_LABELS: Record<ReturnDisposition, string> = {
  good: 'issuedItems.disposition.good',
  damaged: 'issuedItems.disposition.damaged',
  lost: 'issuedItems.disposition.lost',
  disposed: 'issuedItems.disposition.disposed',
}

/**
 * WH-M4: the label of an issuance's OUTCOME. A returned row is only "Teruggebracht" when it came
 * back in good condition; damaged / lost / disposed say so instead of hiding behind "Teruggebracht".
 */
export function issuanceOutcomeKey(status: IssuedItemStatus, disposition?: ReturnDisposition | null): string {
  if (status === 'Returned') {
    switch (disposition) {
      case 'damaged':
        return 'issuedItems.outcome.returnedDamaged'
      case 'lost':
        return 'issuedItems.outcome.lost'
      case 'disposed':
        return 'issuedItems.outcome.disposed'
      default:
        return ISSUED_ITEM_STATUS_LABELS.Returned
    }
  }
  return ISSUED_ITEM_STATUS_LABELS[status]
}

export interface IssuedItemTemplate {
  id: string
  /** WH-M1: short visible warehouse code ("BM-0007"), unique per tenant. */
  code?: string | null
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
  targetStockLevel: number | null
  reorderQuantity: number | null
  negativeStockRequiresReason: boolean
  stockStatus: InventoryStatus
  storageLocation: string | null
  currentStock: number
  totalAvailable: number
  lowStock: boolean
  variantCount: number
  /** WH-M5: consumable — issuing it is final (status Consumed), no loan, no return. */
  isConsumable?: boolean
}

/** "BM-0007 · Scanner" — the code makes two items with the same name distinguishable (WH-M1). */
export function itemDisplayName(template: Pick<IssuedItemTemplate, 'code' | 'name'>): string {
  return template.code ? `${template.code} · ${template.name}` : template.name
}

export interface IssuedItemTemplateInput {
  /** Empty/omitted = the server assigns the next BM-number (create) or keeps the current one (edit). */
  code?: string | null
  isConsumable?: boolean
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
  /** WH-M3: the stock the form was opened on; a stock that moved meanwhile is refused (409). */
  expectedCurrentStock?: number | null
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
  issuedByName: string | null
  returnedDate: string | null
  returnCondition: string | null
  receivedBackByUserId: string | null
  /** Display name of the user who booked the return (null for active or legacy rows). */
  receivedBackByName: string | null
  variantId: string | null
  variantLabel: string | null
  /** Code of the item (template) this row was issued from. */
  itemCode?: string | null
  expectedReturnDate?: string | null
  returnDisposition?: ReturnDisposition | null
  isReturnOverdue?: boolean
}

/** Body of POST …/issued-items/{id}/return — only valid for an Issued row. */
export interface ReturnIssuedItemInput {
  /** YYYY-MM-DD; server defaults to today. */
  returnedDate?: string | null
  returnCondition?: string | null
  /** Server default "good"; only "good" may restore stock. */
  returnDisposition?: ReturnDisposition | null
  restoreStock?: boolean | null
}

/** Body of POST …/issued-items/{id}/reactivate — only valid for a Returned row. */
export interface ReactivateIssuedItemInput {
  /** YYYY-MM-DD; server defaults to today. */
  issuedDate?: string | null
  reason?: string | null
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
  overrideReason?: string | null
  /** Resend flag after a negative_stock_confirmation_required 409. */
  confirmNegativeStock?: boolean
  /** Stock version (guid) uit de 409-payload; server weigert bij tussentijdse wijziging. */
  expectedVersion?: string | null
}

/**
 * The save request that reproduces a stored item unchanged — the edit form's starting point and
 * the one-click "Deactiveren" after a refused delete (WH-B1).
 */
export function templateToInput(template: IssuedItemTemplate): IssuedItemTemplateInput {
  return {
    code: template.code ?? null,
    isConsumable: template.isConsumable ?? false,
    name: template.name,
    category: template.category,
    categoryId: template.categoryId,
    applicableJobFunctionCodes: template.applicableJobFunctionCodes,
    defaultQuantity: template.defaultQuantity,
    requiresSerialNumber: template.requiresSerialNumber,
    requiresReceivedDate: template.requiresReceivedDate,
    returnRequired: template.returnRequired,
    isActive: template.isActive,
    sortOrder: template.sortOrder,
    description: template.description,
    // Legacy free-text units (pre-catalogue) land on "other" so the user re-picks consciously.
    unit: issuedItemUnitForForm(template.unit),
    notes: template.notes,
    stockTrackingEnabled: template.stockTrackingEnabled,
    variantsEnabled: template.variantsEnabled,
    allowNegativeStock: template.allowNegativeStock,
    lowStockThreshold: template.lowStockThreshold,
    // Round-tripped untouched: the detail page's "Voorraadregels" card owns this level.
    minimumStock: template.minimumStock,
    storageLocation: template.storageLocation,
    stock: template.stockTrackingEnabled && !template.variantsEnabled ? template.currentStock : null,
    stockCorrectionReason: null,
  }
}

// ---- Templates ----
// The template `unit` is a fixed catalogue code (see issuedItemUnits.ts), not master data.
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

/** Verwijderen blijft voorbehouden aan actieve/afwijkende rijen: een teruggebrachte uitgifte is historiek (server: 400). */
export function deleteEmployeeIssuedItem(employeeId: string, itemId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/employees/${employeeId}/issued-items/${itemId}`)
}

/** "Teruggebracht": closes an active issuance (status Issued → Returned) and returns the updated row. */
export function returnEmployeeIssuedItem(
  employeeId: string,
  itemId: string,
  input: ReturnIssuedItemInput,
): Promise<EmployeeIssuedItem> {
  return apiClient.postJson<EmployeeIssuedItem, ReturnIssuedItemInput>(
    `/api/employees/${employeeId}/issued-items/${itemId}/return`,
    input,
  )
}

/** "Heractiveren": re-opens a returned issuance (status Returned → Issued) and returns the updated row. */
export function reactivateEmployeeIssuedItem(
  employeeId: string,
  itemId: string,
  input: ReactivateIssuedItemInput,
): Promise<EmployeeIssuedItem> {
  return apiClient.postJson<EmployeeIssuedItem, ReactivateIssuedItemInput>(
    `/api/employees/${employeeId}/issued-items/${itemId}/reactivate`,
    input,
  )
}

/** Ontvangstbewijs (PDF) via the shared download path: token refresh on 401, server error message on failure. */
export function downloadIssuedItemsAcknowledgement(employeeId: string): Promise<void> {
  return apiClient.downloadFile(`/api/employees/${employeeId}/issued-items/document`, 'ontvangstbewijs-bedrijfsmiddelen.pdf')
}
