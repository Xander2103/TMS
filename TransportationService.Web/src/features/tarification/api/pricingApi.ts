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

// --- Pricing agreements (tarievenkaarten) ---

export interface PricingAgreementSurcharge {
  id: string
  name: string
  kind: SurchargeKind
  value: number
}

export interface PricingAgreement {
  id: string
  customerId: string | null
  customerName: string | null
  name: string
  currency: string
  effectiveFrom: string
  effectiveUntil: string | null
  isActive: boolean
  minimumAmount: number | null
  notes: string | null
  surcharges: PricingAgreementSurcharge[]
}

export interface PricingAgreementInput {
  customerId: string | null
  name: string
  effectiveFrom: string
  effectiveUntil: string | null
  isActive: boolean
  minimumAmount: number | null
  notes: string | null
  surcharges: { name: string; kind: SurchargeKind; value: number }[] | null
}

export const listPricingAgreements = (customerId?: string): Promise<PricingAgreement[]> =>
  apiClient.getJson(`/api/pricing/agreements${customerId ? `?customerId=${customerId}` : ''}`)
export const createPricingAgreement = (input: PricingAgreementInput): Promise<PricingAgreement> =>
  apiClient.postJson('/api/pricing/agreements', input)
export const updatePricingAgreement = (id: string, input: PricingAgreementInput): Promise<PricingAgreement> =>
  apiClient.putJson(`/api/pricing/agreements/${id}`, input)
export const deletePricingAgreement = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/pricing/agreements/${id}`)

// --- Price rules ---

export type PriceRuleBasis =
  | 'PerUnit'
  | 'QuantityBracket'
  | 'WeightBracket'
  | 'Hourly'
  | 'Fixed'
  | 'PerKm'
  | 'PerPallet'
  | 'PerTon'

export const PRICE_RULE_BASIS_LABELS: Record<PriceRuleBasis, string> = {
  PerUnit: 'Prijs per eenheid',
  QuantityBracket: 'Staffel op aantal',
  WeightBracket: 'Staffel op gewicht (kg)',
  Hourly: 'Uurtarief',
  Fixed: 'Vaste prijs',
  PerKm: 'Kilometertarief',
  PerPallet: 'Prijs per pallet (order)',
  PerTon: 'Prijs per ton (order)',
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
  agreementId: string | null
  agreementName: string | null
  priority: number
  baseAmount: number | null
  oversizeLengthCm: number | null
  oversizeWidthCm: number | null
  oversizeBillableFactor: number | null
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
  agreementId?: string | null
  priority?: number
  baseAmount?: number | null
  oversizeLengthCm?: number | null
  oversizeWidthCm?: number | null
  oversizeBillableFactor?: number | null
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
  description: string | null
  invoiceDescription: string | null
  selectableInOrders: boolean
}

export interface ServiceOptionInput {
  code: string
  name: string
  kind: SurchargeKind
  defaultValue: number
  isActive: boolean
  sortOrder: number
  description?: string | null
  invoiceDescription?: string | null
  selectableInOrders?: boolean
}

export const listServiceOptions = (includeInactive = false, forOrderEntry = false): Promise<ServiceOption[]> => {
  const params = new URLSearchParams()
  if (includeInactive) params.set('includeInactive', 'true')
  if (forOrderEntry) params.set('forOrderEntry', 'true')
  const query = params.toString()
  return apiClient.getJson(`/api/service-options${query ? `?${query}` : ''}`)
}
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
  customerLabel: string | null
  ediCode: string | null
  excelCode: string | null
  isFavourite: boolean
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

export interface CustomerUnitInput {
  unitTypeId: string
  sortOrder: number
  customerLabel: string | null
  ediCode: string | null
  excelCode: string | null
  isFavourite: boolean
}

export interface CustomerPricingConfigInput {
  units: CustomerUnitInput[]
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

export type UnitCategory =
  | 'Other'
  | 'Packaging'
  | 'Weight'
  | 'Volume'
  | 'Capacity'
  | 'Time'
  | 'Distance'
  | 'Commercial'
  | 'Inventory'

export type UnitDimensionBehavior = 'Variable' | 'DefaultButOverridable' | 'Fixed'

export const UNIT_CATEGORY_LABELS: Record<UnitCategory, string> = {
  Packaging: 'Verpakking',
  Weight: 'Gewicht',
  Volume: 'Volume',
  Capacity: 'Capaciteit',
  Time: 'Tijd',
  Distance: 'Afstand',
  Commercial: 'Commercieel',
  Inventory: 'Voorraad',
  Other: 'Overig',
}

export const DIMENSION_BEHAVIOR_LABELS: Record<UnitDimensionBehavior, string> = {
  Variable: 'Variabel',
  DefaultButOverridable: 'Standaard, aanpasbaar',
  Fixed: 'Vast',
}

export interface UnitTypeMaster {
  id: string
  code: string
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
  allowForOrderEntry: boolean
  allowForPricing: boolean
  allowForInventory: boolean
  category: UnitCategory
  decimals: number
  symbol: string | null
  dimensionBehavior: UnitDimensionBehavior
  defaultLengthCm: number | null
  defaultWidthCm: number | null
  defaultHeightCm: number | null
  defaultWeightKg: number | null
  maxWeightKg: number | null
  defaultVolumeM3: number | null
  defaultLoadingMeters: number | null
  defaultPalletPlaces: number | null
}

export type UnitTypeMasterInput = Omit<UnitTypeMaster, 'id'>

export const listUnitTypeMaster = (): Promise<UnitTypeMaster[]> => apiClient.getJson('/api/unit-types/master')
export const createUnitTypeMaster = (input: UnitTypeMasterInput): Promise<UnitTypeMaster> =>
  apiClient.postJson('/api/unit-types/master', input)
export const updateUnitTypeMaster = (id: string, input: UnitTypeMasterInput): Promise<UnitTypeMaster> =>
  apiClient.putJson(`/api/unit-types/${id}/master`, input)

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
  ruleName: string | null
  agreementName: string | null
  actualQuantity: number | null
  billableQuantity: number | null
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
  tariffDate: string | null
  configurationError: string | null
  diagnostics: string[] | null
}

export const previewPrice = (input: PricePreviewInput): Promise<PriceCalculationResult> =>
  apiClient.postJson('/api/pricing/preview', input)

// --- Scheduled price adjustments ---

export interface PriceAdjustmentValueChange {
  field: string
  oldValue: number
  newValue: number
}

export interface PriceAdjustmentRulePreview {
  priceRuleId: string
  ruleName: string
  effectiveFrom: string
  effectiveUntil: string | null
  changes: PriceAdjustmentValueChange[]
}

export interface ScheduledPriceAdjustment {
  id: string
  customerId: string
  effectiveDate: string
  percent: number
  status: 'Gepland' | 'Actief' | 'Geannuleerd'
  reason: string | null
  ruleCount: number
  createdAt: string
}

export interface PriceAdjustmentInput {
  effectiveDate: string
  percent: number
  ruleIds: string[] | null
  reason?: string | null
}

export const listPriceAdjustments = (customerId: string): Promise<ScheduledPriceAdjustment[]> =>
  apiClient.getJson(`/api/customers/${customerId}/price-adjustments`)
export const previewPriceAdjustment = (
  customerId: string,
  input: Omit<PriceAdjustmentInput, 'reason'>,
): Promise<PriceAdjustmentRulePreview[]> =>
  apiClient.postJson(`/api/customers/${customerId}/price-adjustments/preview`, input)
export const createPriceAdjustment = (
  customerId: string,
  input: PriceAdjustmentInput,
): Promise<ScheduledPriceAdjustment> =>
  apiClient.postJson(`/api/customers/${customerId}/price-adjustments`, input)
export const cancelPriceAdjustment = (customerId: string, id: string): Promise<ScheduledPriceAdjustment> =>
  apiClient.postJson(`/api/customers/${customerId}/price-adjustments/${id}/cancel`, {})
