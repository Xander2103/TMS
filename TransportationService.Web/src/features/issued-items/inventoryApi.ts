import { apiClient } from '../../api/apiClient'
import type { IssuedItemTemplate } from './issuedItemsApi'

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

export const STOCK_MOVEMENT_LABELS: Record<StockMovementType, string> = {
  InitialStock: 'Beginvoorraad',
  Purchase: 'Aankoop',
  Correction: 'Correctie',
  Issue: 'Uitgifte',
  Return: 'Retour',
  Damaged: 'Beschadigd',
  Lost: 'Verloren',
  Disposed: 'Afgevoerd',
  Transfer: 'Overdracht',
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

// ---- Stock ----

export function receiveStock(templateId: string, input: { variantId: string | null; quantity: number; notes: string | null }): Promise<StockMovement> {
  return apiClient.postJson<StockMovement, typeof input>(`/api/issued-item-templates/${templateId}/stock/receipts`, input)
}

export function correctStock(templateId: string, input: { variantId: string | null; newQuantity: number; reason: string }): Promise<StockMovement> {
  return apiClient.postJson<StockMovement, typeof input>(`/api/issued-item-templates/${templateId}/stock/corrections`, input)
}

export function listStockMovements(templateId: string): Promise<StockMovement[]> {
  return apiClient.getJson<StockMovement[]>(`/api/issued-item-templates/${templateId}/stock/movements`)
}

export function listCurrentHolders(templateId: string): Promise<CurrentHolder[]> {
  return apiClient.getJson<CurrentHolder[]>(`/api/issued-item-templates/${templateId}/holders`)
}
