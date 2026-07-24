import { apiClient } from '../../../api/apiClient'
import type { SurchargeKind } from '../types'

// --- Zones ---

export interface PricingZoneArea {
  id: string
  countryCode: string
  postalCodeFrom: string
  postalCodeTo: string
}

export interface PricingZone {
  id: string
  code: string
  name: string
  isActive: boolean
  sortOrder: number
  areas: PricingZoneArea[]
}

export interface PricingZoneAreaInput {
  countryCode: string
  postalCodeFrom: string
  postalCodeTo: string
}

export interface PricingZoneInput {
  code: string
  name: string
  isActive: boolean
  sortOrder: number
  areas: PricingZoneAreaInput[]
}

export const listPricingZones = (): Promise<PricingZone[]> => apiClient.getJson('/api/pricing/zones')
export const createPricingZone = (input: PricingZoneInput): Promise<PricingZone> =>
  apiClient.postJson('/api/pricing/zones', input)
export const updatePricingZone = (id: string, input: PricingZoneInput): Promise<PricingZone> =>
  apiClient.putJson(`/api/pricing/zones/${id}`, input)
export const deletePricingZone = (id: string): Promise<void> => apiClient.deleteRequest(`/api/pricing/zones/${id}`)

// --- Price rules ---

export type PriceRuleBasis = 'PerUnit' | 'QuantityBracket' | 'WeightBracket' | 'Hourly' | 'Fixed'

export const PRICE_RULE_BASIS_LABELS: Record<PriceRuleBasis, string> = {
  PerUnit: 'Prijs per eenheid',
  QuantityBracket: 'Staffel op aantal',
  WeightBracket: 'Staffel op gewicht (kg)',
  Hourly: 'Uurtarief',
  Fixed: 'Vaste prijs',
}

export interface PriceRuleBracket {
  id: string
  fromQuantity: number
  toQuantity: number | null
  price: number
  pricePerExtraUnit: number | null
}

export interface PriceRule {
  id: string
  customerId: string | null
  customerName: string | null
  unitTypeId: string | null
  unitTypeName: string | null
  basis: PriceRuleBasis
  zoneId: string | null
  zoneName: string | null
  name: string
  currency: string
  effectiveFrom: string
  effectiveUntil: string | null
  isActive: boolean
  unitPrice: number | null
  minimumAmount: number | null
  brackets: PriceRuleBracket[]
}

export interface PriceRuleBracketInput {
  fromQuantity: number
  toQuantity: number | null
  price: number
  pricePerExtraUnit: number | null
}

export interface PriceRuleInput {
  customerId: string | null
  unitTypeId: string | null
  basis: PriceRuleBasis
  zoneId: string | null
  name: string
  effectiveFrom: string
  effectiveUntil: string | null
  isActive: boolean
  unitPrice: number | null
  minimumAmount: number | null
  brackets: PriceRuleBracketInput[] | null
}

export const listPriceRules = (customerId?: string): Promise<PriceRule[]> =>
  apiClient.getJson(`/api/pricing/rules${customerId ? `?customerId=${customerId}` : ''}`)
export const createPriceRule = (input: PriceRuleInput): Promise<PriceRule> =>
  apiClient.postJson('/api/pricing/rules', input)
export const updatePriceRule = (id: string, input: PriceRuleInput): Promise<PriceRule> =>
  apiClient.putJson(`/api/pricing/rules/${id}`, input)
export const deletePriceRule = (id: string): Promise<void> => apiClient.deleteRequest(`/api/pricing/rules/${id}`)

// --- Service options ---

export interface ServiceOption {
  id: string
  code: string
  name: string
  kind: SurchargeKind
  defaultValue: number
  isActive: boolean
  sortOrder: number
}

export interface ServiceOptionInput {
  code: string
  name: string
  kind: SurchargeKind
  defaultValue: number
  isActive: boolean
  sortOrder: number
}

export const listServiceOptions = (includeInactive = false): Promise<ServiceOption[]> =>
  apiClient.getJson(`/api/service-options${includeInactive ? '?includeInactive=true' : ''}`)
export const createServiceOption = (input: ServiceOptionInput): Promise<ServiceOption> =>
  apiClient.postJson('/api/service-options', input)
export const updateServiceOption = (id: string, input: ServiceOptionInput): Promise<ServiceOption> =>
  apiClient.putJson(`/api/service-options/${id}`, input)
export const deleteServiceOption = (id: string): Promise<void> => apiClient.deleteRequest(`/api/service-options/${id}`)

// --- Customer pricing config ---

export interface CustomerPreferredUnit {
  unitTypeId: string
  code: string
  name: string
  sortOrder: number
}

export interface CustomerServiceOptionPrice {
  serviceOptionId: string
  name: string
  kind: SurchargeKind
  defaultValue: number
  customerValue: number | null
}

export interface CustomerPricingConfig {
  preferredUnits: CustomerPreferredUnit[]
  serviceOptions: CustomerServiceOptionPrice[]
}

export interface CustomerPricingConfigInput {
  preferredUnitTypeIds: string[]
  optionPrices: { serviceOptionId: string; value: number | null }[]
}

export const getCustomerPricingConfig = (customerId: string): Promise<CustomerPricingConfig> =>
  apiClient.getJson(`/api/customers/${customerId}/pricing-config`)
export const saveCustomerPricingConfig = (
  customerId: string,
  input: CustomerPricingConfigInput,
): Promise<CustomerPricingConfig> => apiClient.putJson(`/api/customers/${customerId}/pricing-config`, input)

// --- Unit type settings ---

export interface UnitTypeSettings {
  id: string
  code: string
  name: string
  isActive: boolean
  sortOrder: number
  allowForOrderEntry: boolean
  allowForPricing: boolean
}

export const listUnitTypeSettings = (): Promise<UnitTypeSettings[]> => apiClient.getJson('/api/unit-types/settings')
export const saveUnitTypeSettings = (
  id: string,
  input: { allowForOrderEntry: boolean; allowForPricing: boolean },
): Promise<UnitTypeSettings> => apiClient.putJson(`/api/unit-types/${id}/settings`, input)

// --- Price preview ---

export interface PriceCalculationLineInput {
  unitTypeId: string
  quantity: number
}

export interface PricePreviewInput {
  customerId: string
  date: string
  lines: PriceCalculationLineInput[]
  deliveryCountryCode: string | null
  deliveryPostalCode: string | null
  weightKg: number | null
  distanceKm: number | null
  palletCount: number | null
  serviceOptionIds: string[]
}

export interface PriceBreakdownLine {
  label: string
  amount: number
  source: string
  informational: boolean
}

export interface PriceCalculationResult {
  lines: PriceBreakdownLine[]
  total: number
  totalWithInformational: number
  currency: string
  zoneCode: string | null
  zoneName: string | null
  requiresManualPrice: boolean
  serviceLines: { serviceOptionId: string; name: string; kind: SurchargeKind; value: number; amount: number }[]
}

export const previewPrice = (input: PricePreviewInput): Promise<PriceCalculationResult> =>
  apiClient.postJson('/api/pricing/preview', input)
