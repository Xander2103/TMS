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

/** One stacking step of a derived agreement (spec §9: "NL = BE +30%"). */
export interface PricingAgreementModifier {
  id: string
  sequence: number
  name: string
  countryCode: string | null
  zoneId: string | null
  zoneName: string | null
  percent: number | null
  fixedAmount: number | null
}

export interface PricingAgreementModifierInput {
  sequence: number
  name: string
  countryCode: string | null
  zoneId: string | null
  percent: number | null
  fixedAmount: number | null
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
  /** True = reusable rate table; never applies directly, only through assignments. */
  isShared: boolean
  /** Cap on the agreement subtotal per order, applied after the minimum. */
  maximumAmount: number | null
  /** Count of customer assignments active today (0 for non-shared agreements). */
  customerCount: number
  /** Names of the customers currently assigned; populated on the list endpoint. */
  customerNames: string[] | null
  /** Set => this is a derived table; it reuses the base-chain root's rules. */
  baseAgreementId: string | null
  baseAgreementName: string | null
  modifiers: PricingAgreementModifier[]
  /** Included loading/unloading time (Phase 6). Mutually exclusive with includedCombinedMinutes. */
  includedLoadingMinutes: number | null
  includedUnloadingMinutes: number | null
  /** Included loading+unloading minutes combined. Mutually exclusive with the per-activity fields. */
  includedCombinedMinutes: number | null
  /** Hourly rate charged for time beyond the included allowance (proposal until confirmed). */
  extraHourlyRate: number | null
  /** Wave 2: default sales code for this agreement's rules (a rule's own code wins). */
  salesCategoryId?: string | null
  salesCategoryName?: string | null
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
  isShared?: boolean
  maximumAmount?: number | null
  baseAgreementId?: string | null
  modifiers?: PricingAgreementModifierInput[] | null
  includedLoadingMinutes?: number | null
  includedUnloadingMinutes?: number | null
  includedCombinedMinutes?: number | null
  extraHourlyRate?: number | null
  salesCategoryId?: string | null
}

export const listPricingAgreements = (customerId?: string): Promise<PricingAgreement[]> =>
  apiClient.getJson(`/api/pricing/agreements${customerId ? `?customerId=${customerId}` : ''}`)
/** Every agreement of the tenant regardless of customer — the "Tarieventabellen" overview. */
export const listAllPricingAgreements = (): Promise<PricingAgreement[]> =>
  apiClient.getJson('/api/pricing/agreements?all=true')
export const getPricingAgreement = (id: string): Promise<PricingAgreement> =>
  apiClient.getJson(`/api/pricing/agreements/${id}`)
export const createPricingAgreement = (input: PricingAgreementInput): Promise<PricingAgreement> =>
  apiClient.postJson('/api/pricing/agreements', input)
export const updatePricingAgreement = (id: string, input: PricingAgreementInput): Promise<PricingAgreement> =>
  apiClient.putJson(`/api/pricing/agreements/${id}`, input)
export const deletePricingAgreement = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/pricing/agreements/${id}`)

/** Duplicates an agreement as a new version; assignments are deliberately NOT copied. */
export interface DuplicateAgreementInput {
  name: string
  effectiveFrom: string
  closeSource: boolean
  percent?: number | null
  amountDelta?: number | null
  roundingStep?: number | null
}

export const duplicateAgreement = (agreementId: string, input: DuplicateAgreementInput): Promise<PricingAgreement> =>
  apiClient.postJson(`/api/pricing/agreements/${agreementId}/duplicate`, input)

// --- Agreement configuration validation ("Controle") ---

export interface PricingConfigCheck {
  severity: 'error' | 'warning'
  message: string
}

export const validateAgreementConfiguration = (agreementId: string): Promise<PricingConfigCheck[]> =>
  apiClient.getJson(`/api/pricing/agreements/${agreementId}/validate`)

// --- Pricing agreement assignments (shared tables → customers) ---

export interface PricingAssignment {
  id: string
  customerId: string
  customerName: string
  percentAdjustment: number | null
  fixedAdjustment: number | null
  effectiveFrom: string | null
  effectiveUntil: string | null
  notes: string | null
}

export interface PricingAssignmentInput {
  customerId: string
  percentAdjustment: number | null
  fixedAdjustment: number | null
  effectiveFrom: string | null
  effectiveUntil: string | null
  notes: string | null
}

export const getAgreementAssignments = (agreementId: string): Promise<PricingAssignment[]> =>
  apiClient.getJson(`/api/pricing/agreements/${agreementId}/assignments`)
export const saveAgreementAssignments = (
  agreementId: string,
  rows: PricingAssignmentInput[],
): Promise<PricingAssignment[]> => apiClient.putJson(`/api/pricing/agreements/${agreementId}/assignments`, rows)

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
  | 'PerLoadingMeter'
  | 'PerVolume'
  | 'PerStop'

export const PRICE_RULE_BASIS_LABELS: Record<PriceRuleBasis, string> = {
  PerUnit: 'Per eenheid (prijs per stuk)',
  QuantityBracket: 'Per eenheid (staffel op aantal)',
  WeightBracket: 'Volgens gewicht',
  Hourly: 'Per uur',
  PerKm: 'Per kilometer',
  PerLoadingMeter: 'Per laadmeter',
  PerVolume: 'Per volume',
  PerStop: 'Per stop',
  Fixed: 'Forfait / vaste prijs',
  PerPallet: 'Per pallet (order)',
  PerTon: 'Per ton (order)',
}

/** The primary pricing bases offered in the rule editor (spec §10) — an OR choice. */
export type PrimaryPricingBasis =
  | 'unit'
  | 'WeightBracket'
  | 'Hourly'
  | 'PerKm'
  | 'PerLoadingMeter'
  | 'PerVolume'
  | 'PerStop'
  | 'Fixed'

export const PRIMARY_BASIS_LABELS: Record<PrimaryPricingBasis, string> = {
  unit: 'Per eenheid',
  WeightBracket: 'Volgens gewicht',
  Hourly: 'Per uur',
  PerKm: 'Per kilometer',
  PerLoadingMeter: 'Per laadmeter',
  PerVolume: 'Per volume',
  PerStop: 'Per stop',
  Fixed: 'Forfait / vaste prijs',
}

export interface PriceRuleBracket {
  id: string
  fromQuantity: number
  toQuantity: number | null
  price: number
  pricePerExtraUnit: number | null
  /** Row matches only when the order's own weight/volume/loading-meters is known and within the cap. */
  weightToKg: number | null
  volumeToM3: number | null
  loadingMetersTo: number | null
}

/** QuantityBracket only: Absolute (single bracket price) or PerNextUnit (sum per piece). */
export type BracketSelectionMode = 'Absolute' | 'PerNextUnit'

export const BRACKET_SELECTION_MODE_LABELS: Record<BracketSelectionMode, string> = {
  Absolute: 'Absoluut (prijs van de staffel)',
  PerNextUnit: 'Per volgende eenheid (som per stuk)',
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
  minimumQuantity: number | null
  quantityRoundingStep: number | null
  /** Cap on the rule amount, applied after minimumAmount. */
  maximumAmount: number | null
  bracketMode: BracketSelectionMode
  /** Wave 2: sales code for lines priced by this rule (wins over the agreement's). */
  salesCategoryId?: string | null
  salesCategoryName?: string | null
}

export interface PriceRuleBracketInput {
  fromQuantity: number
  toQuantity: number | null
  price: number
  pricePerExtraUnit: number | null
  weightToKg: number | null
  volumeToM3: number | null
  loadingMetersTo: number | null
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
  minimumQuantity?: number | null
  quantityRoundingStep?: number | null
  maximumAmount?: number | null
  bracketMode?: BracketSelectionMode
  salesCategoryId?: string | null
}

// --- Bracket-row customer overrides ("klantafwijkingen") ---

export interface PriceRuleBracketOverride {
  id: string
  priceRuleId: string
  customerId: string
  customerName: string
  fromQuantity: number
  toQuantity: number | null
  weightToKg: number | null
  volumeToM3: number | null
  loadingMetersTo: number | null
  price: number
  pricePerExtraUnit: number | null
  effectiveFrom: string | null
  effectiveUntil: string | null
  notes: string | null
  /** True when the rule no longer has a bracket row with this exact identity. */
  orphaned: boolean
}

export interface PriceRuleBracketOverrideInput {
  customerId: string
  fromQuantity: number
  toQuantity: number | null
  weightToKg?: number | null
  volumeToM3?: number | null
  loadingMetersTo?: number | null
  price: number
  pricePerExtraUnit?: number | null
  effectiveFrom?: string | null
  effectiveUntil?: string | null
  notes?: string | null
}

export const listBracketOverrides = (ruleId: string): Promise<PriceRuleBracketOverride[]> =>
  apiClient.getJson(`/api/pricing/rules/${ruleId}/bracket-overrides`)
export const createBracketOverride = (
  ruleId: string,
  input: PriceRuleBracketOverrideInput,
): Promise<PriceRuleBracketOverride> => apiClient.postJson(`/api/pricing/rules/${ruleId}/bracket-overrides`, input)
export const deleteBracketOverride = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/pricing/bracket-overrides/${id}`)

export const listPriceRules = (customerId?: string): Promise<PriceRule[]> =>
  apiClient.getJson(`/api/pricing/rules${customerId ? `?customerId=${customerId}` : ''}`)
/** Rules belonging to exactly this agreement (used by the rate-table grid editor). */
export const listPriceRulesByAgreement = (agreementId: string): Promise<PriceRule[]> =>
  apiClient.getJson(`/api/pricing/rules?agreementId=${agreementId}`)
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
  /** Kind === 'PerUnit' only: which managed unit this service counts. */
  unitTypeId: string | null
  unitTypeName: string | null
  /** The engine adds this service automatically (contract service), quantified from the order. */
  autoApply: boolean
  /** Only charged/auto-applied when the order requires ADR. */
  onlyForAdr: boolean
  /** Warehouse condition: only applies when the order touches one of these warehouses. Empty = all orders. */
  warehouseIds?: string[] | null
  warehouseNames?: string[] | null
  /** Wave 2026-08-04 §16: time-based stop conditions. */
  timeConditions?: ServiceTimeCondition[] | null
  /** Wave 2: sales code for service lines of this option (wins over rule/agreement). */
  salesCategoryId?: string | null
  salesCategoryName?: string | null
}

/** One time-based condition row of a service option (wave 2026-08-04 §16/§17). */
export interface ServiceTimeCondition {
  kind: 'StopTimeBefore' | 'StopTimeAfter' | 'AppointmentRequired' | 'Weekend'
  stopScope: 'Any' | 'Loading' | 'Unloading'
  /** "HH:mm[:ss]" — StopTimeBefore/StopTimeAfter only. */
  timeOfDay: string | null
  /** Competition priority among matched Before (resp. After) conditions; higher wins. */
  priority: number
  /** Opt-in stacking: never competes, always applies when matched. */
  allowStacking: boolean
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
  unitTypeId?: string | null
  autoApply?: boolean
  onlyForAdr?: boolean
  /** Warehouse condition (OR within the list, AND with the ADR flag); empty/omitted = all orders. */
  warehouseIds?: string[] | null
  /** Wave 2026-08-04 §16: time-based stop conditions; the list replaces the stored rows. */
  timeConditions?: ServiceTimeCondition[] | null
  salesCategoryId?: string | null
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
  disabled: boolean
  minimumAmount: number | null
  invoiceDescription: string | null
  effectiveFrom: string | null
  effectiveUntil: string | null
  effectiveValue: number
  source: 'Klanttarief' | 'Algemene standaard'
  /** Override of the global auto-apply behaviour for this customer; null = inherit. */
  autoApplyOverride: boolean | null
  /** The effective auto-apply behaviour today, after applying the override. */
  effectiveAutoApply: boolean
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

export interface CustomerOptionPriceInput {
  serviceOptionId: string
  value: number | null
  disabled?: boolean
  minimumAmount?: number | null
  invoiceDescription?: string | null
  effectiveFrom?: string | null
  effectiveUntil?: string | null
  autoApplyOverride?: boolean | null
}

export interface CustomerPricingConfigInput {
  units: CustomerUnitInput[]
  optionPrices: CustomerOptionPriceInput[]
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
  /** Wave 3 §1: laadmeters — voedt PerLdm-tarieven en ldm-staffelgrenzen. */
  loadingMeters?: number | null
  serviceOptionIds: string[]
  services?: { serviceOptionId: string; quantity: number | null }[]
  /** Drives OnlyForAdr service options. */
  adrRequired?: boolean | null
  /** Drives PerOrderLine service options; null = unknown. */
  cargoLineCount?: number | null
  /** Set => this order carries its own one-off price agreement; skips all rule/agreement resolution. */
  oneOff?: OneOffPricingInput | null
  /** Measured loading/unloading minutes from stop executions, for included-time extra-time proposals. */
  actualLoadingMinutes?: number | null
  actualUnloadingMinutes?: number | null
  /** Warehouses the order touches (stop at a warehouse's master location) — drives warehouse-conditioned services. */
  warehouseIds?: string[] | null
  /** Wave 2026-08-04 §16: per-stop time facts driving time-based service conditions. */
  stopTimes?: StopTimePreviewInput[] | null
}

/** One stop's time facts for time-based service conditions (wave 2026-08-04 §16). */
export interface StopTimePreviewInput {
  isUnloading: boolean
  requirementKind: 'None' | 'Before' | 'After' | 'Window'
  /** "HH:mm". */
  requirementFrom: string | null
  requirementTo: string | null
  appointmentRequired: boolean
  /** "YYYY-MM-DD" — drives weekend conditions. */
  plannedDate: string | null
}

/** A one-off order's own price agreement: no contract is consulted (spec Phase 6). */
export interface OneOffPricingInput {
  fixedAmount: number
  includedLoadingMinutes: number | null
  includedUnloadingMinutes: number | null
  includedCombinedMinutes: number | null
  extraHourlyRate: number | null
  notes: string | null
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
  /** An unconfirmed extra-time charge (Phase 6): excluded from total, shown as a proposal ("VOORSTEL"). */
  proposed?: boolean
}

export interface PricePreviewServiceLine {
  serviceOptionId: string
  name: string
  kind: SurchargeKind
  value: number
  amount: number
  quantity?: number | null
  /** True when the engine added this line automatically (contract service) — not selected by the user. */
  autoApplied?: boolean
}

/**
 * Task 11: the effective included loading/unloading time and extra-time rate actually applied
 * (order override ?? winning agreement value), plus where it came from — drives the order form's
 * "Laad- en lostijd" section. "Contract" = agreement value, no order override; "Order" = any of
 * the 5 order overrides set; "Geen" = no engaged agreement with included time and no overrides.
 */
export interface IncludedTimeInfo {
  includedLoadingMinutes: number | null
  includedUnloadingMinutes: number | null
  includedCombinedMinutes: number | null
  extraHourlyRate: number | null
  source: 'Contract' | 'Order' | 'Geen'
}

export interface PriceCalculationResult {
  lines: PriceBreakdownLine[]
  total: number
  totalWithInformational: number
  currency: string
  zoneCode: string | null
  zoneName: string | null
  requiresManualPrice: boolean
  serviceLines: PricePreviewServiceLine[]
  tariffDate: string | null
  configurationError: string | null
  diagnostics: string[] | null
  /** Total + proposed (unconfirmed) extra-time charges — never silently invoiceable on its own. */
  totalWithProposed: number
  /** Null for a one-off order (see OneOffPricingInput, which has its own included-time fields). */
  includedTimeInfo?: IncludedTimeInfo | null
  /** Wave 2026-08-04 §7: per-unit-line pricing coverage of the previewed calculation. */
  coverage?: PricePreviewCoverageLine[] | null
}

/** Pricing coverage of one commercial unit line (wave 2026-08-04 §7). */
export interface PricePreviewCoverageLine {
  unitTypeId: string | null
  unitLabel: string
  quantity: number
  status: 'Full' | 'Partial' | 'None'
  baseAmount: number
  baseRuleName: string | null
  servicesAmount: number
  reason: string | null
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

/** Comma-joined basis names used to restrict a bulk adjustment (v2); null/undefined = every basis. */
export type AdjustmentBasisFilter = string | null

export interface ScheduledPriceAdjustment {
  id: string
  /** Customer-scoped adjustment; exactly one of customerId/agreementId is set. */
  customerId: string | null
  agreementId: string | null
  effectiveDate: string
  /** Signed percentage; XOR with amountDelta. */
  percent: number | null
  /** Fixed signed €-amount; XOR with percent. */
  amountDelta: number | null
  /** Optional post-adjustment rounding: null | 0.01 | 0.05 | 0.10. */
  roundingStep: number | null
  basisFilter: AdjustmentBasisFilter
  unitTypeIdFilter: string | null
  status: 'Gepland' | 'Actief' | 'Geannuleerd'
  reason: string | null
  ruleCount: number
  createdAt: string
}

export interface PriceAdjustmentInput {
  effectiveDate: string
  /** Exactly one of percent/amountDelta must be set. */
  percent: number | null
  ruleIds: string[] | null
  amountDelta?: number | null
  roundingStep?: number | null
  basisFilter?: AdjustmentBasisFilter
  unitTypeIdFilter?: string | null
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

// --- Combined-unit degression discounts (spec §29-31) ---

export type DegressionScope = 'Order' | 'DeliveryAddress' | 'Stop'

export const DEGRESSION_SCOPE_LABELS: Record<DegressionScope, string> = {
  Order: 'Hele order',
  DeliveryAddress: 'Per leveradres',
  Stop: 'Per stop',
}

export interface CombinedUnitDiscountUnit {
  id: string
  unitTypeId: string
  unitTypeName: string | null
  equivalentFactor: number
}

export interface CombinedUnitDiscountTier {
  id: string
  fromCount: number
  toCount: number | null
  percent: number
}

export interface CombinedUnitDiscount {
  id: string
  customerId: string | null
  customerName: string | null
  agreementId: string | null
  agreementName: string | null
  name: string
  scope: DegressionScope
  effectiveFrom: string
  effectiveUntil: string | null
  isActive: boolean
  units: CombinedUnitDiscountUnit[]
  tiers: CombinedUnitDiscountTier[]
}

export interface CombinedUnitDiscountUnitInput {
  unitTypeId: string
  equivalentFactor: number
}

export interface CombinedUnitDiscountTierInput {
  fromCount: number
  toCount: number | null
  percent: number
}

export interface CombinedUnitDiscountInput {
  customerId: string | null
  agreementId: string | null
  name: string
  scope: DegressionScope
  effectiveFrom: string
  effectiveUntil: string | null
  isActive: boolean
  units: CombinedUnitDiscountUnitInput[]
  tiers: CombinedUnitDiscountTierInput[]
}

export const listCombinedDiscounts = (customerId?: string, agreementId?: string): Promise<CombinedUnitDiscount[]> => {
  const params = new URLSearchParams()
  if (customerId) params.set('customerId', customerId)
  if (agreementId) params.set('agreementId', agreementId)
  const query = params.toString()
  return apiClient.getJson(`/api/pricing/combined-discounts${query ? `?${query}` : ''}`)
}
export const createCombinedDiscount = (input: CombinedUnitDiscountInput): Promise<CombinedUnitDiscount> =>
  apiClient.postJson('/api/pricing/combined-discounts', input)
export const updateCombinedDiscount = (id: string, input: CombinedUnitDiscountInput): Promise<CombinedUnitDiscount> =>
  apiClient.putJson(`/api/pricing/combined-discounts/${id}`, input)
export const deleteCombinedDiscount = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/pricing/combined-discounts/${id}`)

// --- Scheduled price adjustments (agreement-scoped, v2) ---

export const listAgreementAdjustments = (agreementId: string): Promise<ScheduledPriceAdjustment[]> =>
  apiClient.getJson(`/api/pricing/agreements/${agreementId}/price-adjustments`)
export const previewAgreementAdjustment = (
  agreementId: string,
  input: Omit<PriceAdjustmentInput, 'reason'>,
): Promise<PriceAdjustmentRulePreview[]> =>
  apiClient.postJson(`/api/pricing/agreements/${agreementId}/price-adjustments/preview`, input)
export const createAgreementAdjustment = (
  agreementId: string,
  input: PriceAdjustmentInput,
): Promise<ScheduledPriceAdjustment> =>
  apiClient.postJson(`/api/pricing/agreements/${agreementId}/price-adjustments`, input)
export const cancelAgreementAdjustment = (agreementId: string, id: string): Promise<ScheduledPriceAdjustment> =>
  apiClient.postJson(`/api/pricing/agreements/${agreementId}/price-adjustments/${id}/cancel`, {})
