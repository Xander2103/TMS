import { ApiError, apiClient } from '../../api/apiClient'
import type {
  EmployeeIssuedItem,
  IssuedItemStatus,
  IssuedItemTemplate,
  ReturnDisposition,
  ReturnIssuedItemInput,
} from './issuedItemsApi'
import type { InventoryStatus } from './inventoryStatus'

export interface IssuedItemAttributeOption {
  id: string
  value: string
  sortOrder: number
  isActive: boolean
}

export interface IssuedItemAttributeDefinition {
  id: string
  name: string
  allowCustomValues: boolean
  isShared: boolean
  sortOrder: number
  isActive: boolean
  options: IssuedItemAttributeOption[]
}

export interface AttributeDefinitionInput {
  name: string
  allowCustomValues: boolean
  isShared: boolean
  sortOrder: number
  isActive: boolean
}

export interface AttributeOptionInput {
  value: string
  sortOrder: number
  isActive: boolean
}

export interface IssuedItemVariantValue {
  attributeDefinitionId: string
  attributeName: string
  attributeOptionId: string | null
  value: string
}

export interface IssuedItemVariant {
  id: string
  label: string
  currentStock: number
  isActive: boolean
  sortOrder: number
  values: IssuedItemVariantValue[]
  lowStockThreshold: number | null
}

export interface VariantValueInput {
  attributeDefinitionId: string
  attributeOptionId: string | null
  customValue: string | null
}

export interface VariantInput {
  values: VariantValueInput[]
  isActive: boolean
  sortOrder: number
  initialStock: number | null
  /** Free variant label for templates without linked attributes ("Small", "maat 43"). */
  label?: string | null
  lowStockThreshold?: number | null
}

export type StockMovementType =
  | 'InitialStock'
  | 'Purchase'
  | 'Correction'
  | 'Issue'
  | 'Return'
  | 'Damaged'
  | 'Lost'
  | 'Disposed'
  | 'Transfer'
  | 'Consumption'

/** Vertaalsleutels — renderen als t(STOCK_MOVEMENT_LABELS[type]). */
export const STOCK_MOVEMENT_LABELS: Record<StockMovementType, string> = {
  InitialStock: 'issuedItems.movement.InitialStock',
  Purchase: 'issuedItems.movement.Purchase',
  Correction: 'issuedItems.movement.Correction',
  Issue: 'issuedItems.movement.Issue',
  Return: 'issuedItems.movement.Return',
  Damaged: 'issuedItems.movement.Damaged',
  Lost: 'issuedItems.movement.Lost',
  Disposed: 'issuedItems.movement.Disposed',
  Transfer: 'issuedItems.movement.Transfer',
  Consumption: 'issuedItems.movement.Consumption',
}

export interface StockMovement {
  id: string
  templateId: string
  variantId: string | null
  variantLabel: string | null
  movementType: StockMovementType
  quantity: number
  resultingStock: number
  reason: string | null
  notes: string | null
  employeeId: string | null
  employeeName: string | null
  performedByUserId: string | null
  timestamp: string
  /** WH-M4: who booked it, the employee number, the serial and the issuance it belongs to. */
  performedByName?: string | null
  employeeNumber?: string | null
  serialNumber?: string | null
  issuedItemId?: string | null
}

export interface CurrentHolder {
  employeeId: string
  employeeName: string
  employeeNumber: string
  itemId: string
  variantLabel: string | null
  quantity: number
  issuedDate: string | null
  serialNumber: string | null
  expectedReturnDate?: string | null
  employeeActive?: boolean
}

export interface IssuedItemTemplateDetail {
  template: IssuedItemTemplate
  attributes: IssuedItemAttributeDefinition[]
  variants: IssuedItemVariant[]
}

// ---- Attribute definitions (reusable master data) ----

export function listAttributeDefinitions(includeInactive = false): Promise<IssuedItemAttributeDefinition[]> {
  const query = includeInactive ? '?includeInactive=true' : ''
  return apiClient.getJson<IssuedItemAttributeDefinition[]>(`/api/issued-item-attributes${query}`)
}

export function createAttributeDefinition(input: AttributeDefinitionInput): Promise<IssuedItemAttributeDefinition> {
  return apiClient.postJson<IssuedItemAttributeDefinition, AttributeDefinitionInput>('/api/issued-item-attributes', input)
}

export function updateAttributeDefinition(id: string, input: AttributeDefinitionInput): Promise<IssuedItemAttributeDefinition> {
  return apiClient.putJson<IssuedItemAttributeDefinition, AttributeDefinitionInput>(`/api/issued-item-attributes/${id}`, input)
}

export function deleteAttributeDefinition(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/issued-item-attributes/${id}`)
}

export function addAttributeOption(definitionId: string, input: AttributeOptionInput): Promise<IssuedItemAttributeOption> {
  return apiClient.postJson<IssuedItemAttributeOption, AttributeOptionInput>(`/api/issued-item-attributes/${definitionId}/options`, input)
}

export function deleteAttributeOption(definitionId: string, optionId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/issued-item-attributes/${definitionId}/options/${optionId}`)
}

// ---- Template detail, attributes, variants ----

export function getTemplateDetail(templateId: string): Promise<IssuedItemTemplateDetail> {
  return apiClient.getJson<IssuedItemTemplateDetail>(`/api/issued-item-templates/${templateId}/detail`)
}

export function setTemplateAttributes(templateId: string, attributeDefinitionIds: string[]): Promise<IssuedItemTemplateDetail> {
  return apiClient.putJson<IssuedItemTemplateDetail, { attributeDefinitionIds: string[] }>(
    `/api/issued-item-templates/${templateId}/attributes`,
    { attributeDefinitionIds },
  )
}

export function createVariant(templateId: string, input: VariantInput): Promise<IssuedItemVariant> {
  return apiClient.postJson<IssuedItemVariant, VariantInput>(`/api/issued-item-templates/${templateId}/variants`, input)
}

export function updateVariant(templateId: string, variantId: string, input: VariantInput): Promise<IssuedItemVariant> {
  return apiClient.putJson<IssuedItemVariant, VariantInput>(`/api/issued-item-templates/${templateId}/variants/${variantId}`, input)
}

export function deleteVariant(templateId: string, variantId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/issued-item-templates/${templateId}/variants/${variantId}`)
}

export interface GenerateVariantsDimension {
  attributeDefinitionId: string
  optionIds: string[]
}

/** Bulk-creates the cartesian product of the selected options; existing combinations are kept. */
export function generateVariants(templateId: string, dimensions: GenerateVariantsDimension[]): Promise<IssuedItemTemplateDetail> {
  return apiClient.postJson<IssuedItemTemplateDetail, { dimensions: GenerateVariantsDimension[] }>(
    `/api/issued-item-templates/${templateId}/variants/generate`,
    { dimensions },
  )
}

// ---- Stock ----

export function receiveStock(templateId: string, input: { variantId: string | null; quantity: number; notes: string | null }): Promise<StockMovement> {
  return apiClient.postJson<StockMovement, typeof input>(`/api/issued-item-templates/${templateId}/stock/receipts`, input)
}

export interface StockCorrectionInput {
  variantId: string | null
  newQuantity: number
  reason: string
  /** Resend flag after a negative_stock_confirmation_required 409. */
  confirmNegativeStock?: boolean
  /** Stock version (guid) uit de 409-payload; server weigert bij tussentijdse wijziging. */
  expectedVersion?: string
  /** WH-M3: the stock the dialog was opened on — mandatory; a moved stock answers 409. */
  expectedCurrentStock: number
}

export function correctStock(templateId: string, input: StockCorrectionInput): Promise<StockMovement> {
  return apiClient.postJson<StockMovement, StockCorrectionInput>(`/api/issued-item-templates/${templateId}/stock/corrections`, input)
}

export function listStockMovements(templateId: string): Promise<StockMovement[]> {
  return apiClient.getJson<StockMovement[]>(`/api/issued-item-templates/${templateId}/stock/movements`)
}

export function listCurrentHolders(templateId: string): Promise<CurrentHolder[]> {
  return apiClient.getJson<CurrentHolder[]>(`/api/issued-item-templates/${templateId}/holders`)
}

// ---- Thresholds (voorraadregels) ----

export interface StockThresholdsInput {
  lowStockThreshold: number | null
  minimumStock: number | null
  targetStockLevel: number | null
  reorderQuantity: number | null
  allowNegativeStock: boolean
  negativeStockRequiresReason: boolean
}

/** Requires `inventory.manage_thresholds` or `issued_items.manage_templates`. */
export function updateStockThresholds(templateId: string, input: StockThresholdsInput): Promise<IssuedItemTemplate> {
  return apiClient.putJson<IssuedItemTemplate, StockThresholdsInput>(`/api/issued-item-templates/${templateId}/thresholds`, input)
}

// ---- Preflight (optioneel; de 409-flow is leidend) ----

export interface StockPreflightInput {
  variantId?: string | null
  quantityDelta: number
}

export interface StockPreflightResult {
  currentStock: number
  projectedStock: number
  allowNegativeStock: boolean
  blocked: boolean
  requiresConfirmation: boolean
  requiresReason: boolean
  version: string
  currentStatus: InventoryStatus
  projectedStatus: InventoryStatus
  warningLevel: number | null
  minimumLevel: number | null
}

export function preflightStock(templateId: string, input: StockPreflightInput): Promise<StockPreflightResult> {
  return apiClient.postJson<StockPreflightResult, StockPreflightInput>(`/api/issued-item-templates/${templateId}/stock/preflight`, input)
}

// ---- Inventory read model (permissie inventory.view of inventory.manage) ----

export interface InventoryOverviewRow {
  templateId: string
  variantId: string | null
  name: string
  variantLabel: string | null
  category: string
  storageLocation: string | null
  unit: string | null
  currentStock: number
  warningLevel: number | null
  minimumLevel: number | null
  targetLevel: number | null
  reorderQuantity: number | null
  status: InventoryStatus
  lastMovementAt: string | null
  allowNegativeStock: boolean
  returnRequired: boolean
  code?: string | null
  isConsumable?: boolean
}

export function getInventoryOverview(status?: InventoryStatus): Promise<InventoryOverviewRow[]> {
  const query = status ? `?status=${status}` : ''
  return apiClient.getJson<InventoryOverviewRow[]>(`/api/inventory/overview${query}`)
}

export interface InventoryAlert {
  id: string
  templateId: string
  variantId: string | null
  name: string
  variantLabel: string | null
  kind: InventoryStatus
  stockSnapshot: number
  warningSnapshot: number | null
  minimumSnapshot: number | null
  lastSeenAt: string
  createdAt: string
}

export function listInventoryAlerts(): Promise<InventoryAlert[]> {
  return apiClient.getJson<InventoryAlert[]>('/api/inventory/alerts')
}

// ---- Negatieve-voorraadbevestiging (409-payload) ----

export const NEGATIVE_STOCK_CODE = 'negative_stock_confirmation_required'

/** ProblemDetails-extensions van de 409 die om expliciete bevestiging vraagt. */
export interface NegativeStockPayload {
  code: typeof NEGATIVE_STOCK_CODE
  templateId: string
  variantId: string | null
  itemName: string
  variantLabel: string | null
  currentStock: number
  requestedDelta: number
  projectedStock: number
  /** Stock version (guid); moet als expectedVersion terug bij de bevestiging. */
  version: string
  requiresReason: boolean
  /** True wanneer de voorraad intussen wijzigde; toon de nieuwe cijfers en laat opnieuw bevestigen. */
  versionMismatch: boolean
}

/** Haalt de bevestigingspayload uit een 409 ApiError, of null voor elke andere fout. */
export function parseNegativeStockPayload(error: unknown): NegativeStockPayload | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null
  const body = error.body
  if (typeof body !== 'object' || body === null) return null
  const b = body as Record<string, unknown>
  if (b.code !== NEGATIVE_STOCK_CODE) return null
  return {
    code: NEGATIVE_STOCK_CODE,
    templateId: typeof b.templateId === 'string' ? b.templateId : '',
    variantId: typeof b.variantId === 'string' ? b.variantId : null,
    itemName: typeof b.itemName === 'string' ? b.itemName : '',
    variantLabel: typeof b.variantLabel === 'string' ? b.variantLabel : null,
    currentStock: typeof b.currentStock === 'number' ? b.currentStock : 0,
    requestedDelta: typeof b.requestedDelta === 'number' ? b.requestedDelta : 0,
    projectedStock: typeof b.projectedStock === 'number' ? b.projectedStock : 0,
    version: typeof b.version === 'string' ? b.version : '',
    requiresReason: b.requiresReason === true,
    versionMismatch: b.versionMismatch === true,
  }
}

// ---- Warehouse issue/return desk (WH-H3/H4/H5) ----

/** The safe identity the desk may show: number, name, active — never HR fields. */
export interface InventoryEmployeeOption {
  id: string
  employeeNumber: string
  name: string
  isActive: boolean
}

export function searchInventoryEmployees(search: string, includeInactive = false): Promise<InventoryEmployeeOption[]> {
  const params = new URLSearchParams()
  if (search.trim()) params.set('search', search.trim())
  if (includeInactive) params.set('includeInactive', 'true')
  const query = params.toString()
  return apiClient.getJson<InventoryEmployeeOption[]>(`/api/inventory/employee-options${query ? `?${query}` : ''}`)
}

export interface InventoryIssueRow {
  itemId: string
  employeeId: string
  employeeNumber: string
  employeeName: string
  employeeActive: boolean
  templateId: string | null
  itemCode: string | null
  itemName: string
  variantLabel: string | null
  serialNumber: string | null
  quantity: number
  status: IssuedItemStatus
  returnDisposition: ReturnDisposition | null
  issuedDate: string | null
  expectedReturnDate: string | null
  returnedDate: string | null
  isOverdue: boolean
  returnRequired: boolean
  isConsumable: boolean
  issuedByName: string | null
  receivedBackByName: string | null
  /** Legacy data: the same serial has more than one open holder (from before the serial rule). */
  duplicateOpenSerial: boolean
}

export interface InventoryIssueFilters {
  scope: 'open' | 'history'
  search?: string
  employeeId?: string
  templateId?: string
  serial?: string
  overdueOnly?: boolean
  inactiveEmployeesOnly?: boolean
  returnRequiredOnly?: boolean
  dueBefore?: string
}

export function listInventoryIssues(filters: InventoryIssueFilters): Promise<InventoryIssueRow[]> {
  const params = new URLSearchParams({ scope: filters.scope })
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.employeeId) params.set('employeeId', filters.employeeId)
  if (filters.templateId) params.set('templateId', filters.templateId)
  if (filters.serial?.trim()) params.set('serial', filters.serial.trim())
  if (filters.overdueOnly) params.set('overdueOnly', 'true')
  if (filters.inactiveEmployeesOnly) params.set('inactiveEmployeesOnly', 'true')
  if (filters.returnRequiredOnly) params.set('returnRequiredOnly', 'true')
  if (filters.dueBefore) params.set('dueBefore', filters.dueBefore)
  return apiClient.getJson<InventoryIssueRow[]>(`/api/inventory/issues?${params.toString()}`)
}

export interface WarehouseIssueInput {
  employeeId: string
  templateId: string
  variantId: string | null
  quantity: number
  serialNumber: string | null
  issuedDate: string | null
  expectedReturnDate: string | null
  conditionAtIssue: string | null
  notes: string | null
  confirmNegativeStock?: boolean
  expectedVersion?: string | null
  overrideReason?: string | null
}

export function issueFromWarehouse(input: WarehouseIssueInput): Promise<EmployeeIssuedItem> {
  return apiClient.postJson<EmployeeIssuedItem, WarehouseIssueInput>('/api/inventory/issues', input)
}

export function returnToWarehouse(itemId: string, input: ReturnIssuedItemInput): Promise<EmployeeIssuedItem> {
  return apiClient.postJson<EmployeeIssuedItem, ReturnIssuedItemInput>(`/api/inventory/issues/${itemId}/return`, input)
}

export interface OutstandingEquipment {
  count: number
  items: InventoryIssueRow[]
}

/** WH-H5: open return-required items of one employee (the offboarding warning). */
export function getOutstandingEquipment(employeeId: string): Promise<OutstandingEquipment> {
  return apiClient.getJson<OutstandingEquipment>(`/api/inventory/employees/${employeeId}/outstanding`)
}
