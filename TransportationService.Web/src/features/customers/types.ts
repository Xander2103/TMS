export interface CustomerListItem {
  id: string
  customerNumber: string
  name: string
  city: string | null
  countryCode: string | null
  categoryName: string | null
  isActive: boolean
  isBlocked: boolean
}

/** Contact-person type; `isPrimary` means "primary within this type" (one per type). */
export type CustomerContactType =
  | 'Algemeen'
  | 'Planning'
  | 'Facturatie'
  | 'Magazijn'
  | 'Directie'
  | 'Operationeel'
  | 'Overig'

/**
 * Vertaalsleutels per contacttype — renderen als t(CUSTOMER_CONTACT_TYPE_LABEL_KEYS[type]).
 * De backend slaat de (Nederlandse) enumnaam als string op; de NL-vertaling is gelijk aan de
 * enumnaam, FR/EN vertalen bij render. Nooit logica op het vertaalde label baseren.
 */
export const CUSTOMER_CONTACT_TYPE_LABEL_KEYS: Record<CustomerContactType, string> = {
  Algemeen: 'customers.contactType.Algemeen',
  Planning: 'customers.contactType.Planning',
  Facturatie: 'customers.contactType.Facturatie',
  Magazijn: 'customers.contactType.Magazijn',
  Directie: 'customers.contactType.Directie',
  Operationeel: 'customers.contactType.Operationeel',
  Overig: 'customers.contactType.Overig',
}

export const CUSTOMER_CONTACT_TYPES = Object.keys(CUSTOMER_CONTACT_TYPE_LABEL_KEYS) as CustomerContactType[]

export interface CustomerContact {
  id: string
  firstName: string
  lastName: string
  role: string | null
  email: string | null
  phoneNumber: string | null
  isPrimary: boolean
  notes: string | null
  displayName: string | null
  nickname: string | null
  mobilePhone: string | null
  departmentId: string | null
  preferredLanguageCode: string | null
  isActive: boolean
  contactType: CustomerContactType
}

export type VatTreatment =
  | 'DomesticVat'
  | 'ReverseCharge'
  | 'IntraCommunitySupply'
  | 'ExportOutsideEu'
  | 'VatExempt'
  | 'Other'

/**
 * Vertaalsleutels als FALLBACK-labels — renderen als t(VAT_TREATMENT_LABEL_KEYS[treatment]).
 * De backendcatalogus (GET /api/customers/vat-treatments) blijft de bron voor labels zodra
 * die geladen is; deze keys dekken enkel de laad-/foutfase af.
 */
export const VAT_TREATMENT_LABEL_KEYS: Record<VatTreatment, string> = {
  DomesticVat: 'customers.vatTreatment.DomesticVat',
  ReverseCharge: 'customers.vatTreatment.ReverseCharge',
  IntraCommunitySupply: 'customers.vatTreatment.IntraCommunitySupply',
  ExportOutsideEu: 'customers.vatTreatment.ExportOutsideEu',
  VatExempt: 'customers.vatTreatment.VatExempt',
  Other: 'customers.vatTreatment.Other',
}

/** One VAT-treatment catalog entry from GET /api/customers/vat-treatments (backend is authoritative). */
export interface VatTreatmentInfo {
  treatment: VatTreatment
  label: string
  requiresVatNumber: boolean
  standardRates: number[]
  defaultRatePercent: number | null
  invoiceLegalText: string | null
  allowsCustomRate: boolean
}

/** Company data from the official registry (POST /api/customers/registry-lookup). */
export interface CompanyRegistryResult {
  legalName: string | null
  companyNumber: string | null
  vatNumber: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  peppolId: string | null
  peppolScheme: string | null
}

export interface RegistryLookupResponse {
  /** False when no registry provider is configured for this tenant. */
  configured: boolean
  result: CompanyRegistryResult | null
}

/** Documentstrategie: wie het transportdocument (leveringsbon/CMR) aanlevert. */
export type CustomerDocumentStrategy = 'GenerateOwn' | 'CustomerDocument' | 'PerOrder'

/** Vertaalsleutels — renderen als t(CUSTOMER_DOCUMENT_STRATEGY_LABEL_KEYS[strategy]). */
export const CUSTOMER_DOCUMENT_STRATEGY_LABEL_KEYS: Record<CustomerDocumentStrategy, string> = {
  GenerateOwn: 'customers.documentStrategy.GenerateOwn',
  CustomerDocument: 'customers.documentStrategy.CustomerDocument',
  PerOrder: 'customers.documentStrategy.PerOrder',
}

/** Provenance of the grouped Peppol control's current value, shown as a status chip. */
export type PeppolStatus = 'auto' | 'manual' | 'not-found' | 'not-validated'

/** Bezorgvoorkeur voor uitgaande facturen wanneer Peppol is ingeschakeld. */
export type PeppolDeliveryPreference = 'Peppol' | 'EmailFallback'

/** Vertaalsleutels — renderen als t(PEPPOL_DELIVERY_PREFERENCE_LABEL_KEYS[preference]). */
export const PEPPOL_DELIVERY_PREFERENCE_LABEL_KEYS: Record<PeppolDeliveryPreference, string> = {
  Peppol: 'customers.peppolDelivery.Peppol',
  EmailFallback: 'customers.peppolDelivery.EmailFallback',
}

/** Persisted outcome of the provider directory check (read-only; updated via verify). */
export type PeppolValidationStatus = 'Unknown' | 'Found' | 'NotFound'

/** Result of POST /api/customers/{id}/peppol/verify (peppol.validate). */
export interface CustomerPeppolVerifyResult {
  found: boolean
  supportedDocumentTypes: string[]
  lastCheckedAt: string
  reference: string | null
}

/** One Peppol scheme (EAS) option served by GET /api/customers/peppol-schemes. */
export interface PeppolScheme {
  code: string
  label: string
  countryCode: string | null
}

export interface CustomerVatProfile {
  vatTreatment: VatTreatment
  defaultVatRatePercent: number | null
  vatCountryCode: string | null
  vatNotes: string | null
  peppolId: string | null
  peppolScheme: string | null
  /** Facturen via Peppol versturen (vereist een Peppol-ID en -schema, fiscaal recht). */
  peppolEnabled: boolean
  peppolDeliveryPreference: PeppolDeliveryPreference
  /** Kopersreferentie (buyer reference) op uitgaande Peppol-facturen, bv. een kostenplaats. */
  buyerReference: string | null
  invoiceLanguageCode: string | null
  purchaseOrderRequired: boolean
  signedDeliveryNoteRequired: boolean
  customerReferenceRequired: boolean
}

// --- Communicatieregels (mirrors CustomerCommunicationRuleDto) ---

export type CustomerCommunicationType =
  | 'PlanningAlert'
  | 'DeliveryChange'
  | 'DelayNotification'
  | 'EtaUpdate'
  | 'OrderConfirmation'
  | 'Invoice'
  | 'InvoiceReminder'
  | 'GeneralReminder'
  | 'Claims'
  | 'Other'

/** Vertaalsleutels — renderen als t(COMMUNICATION_TYPE_LABEL_KEYS[type]). */
export const COMMUNICATION_TYPE_LABEL_KEYS: Record<CustomerCommunicationType, string> = {
  PlanningAlert: 'customers.communicationType.PlanningAlert',
  DeliveryChange: 'customers.communicationType.DeliveryChange',
  DelayNotification: 'customers.communicationType.DelayNotification',
  EtaUpdate: 'customers.communicationType.EtaUpdate',
  OrderConfirmation: 'customers.communicationType.OrderConfirmation',
  Invoice: 'customers.communicationType.Invoice',
  InvoiceReminder: 'customers.communicationType.InvoiceReminder',
  GeneralReminder: 'customers.communicationType.GeneralReminder',
  Claims: 'customers.communicationType.Claims',
  Other: 'customers.communicationType.Other',
}

export const COMMUNICATION_TYPES = Object.keys(COMMUNICATION_TYPE_LABEL_KEYS) as CustomerCommunicationType[]

/** Display label for a rule type; 'Other' shows the customer-specific label. */
export function communicationTypeLabel(
  t: (key: string, params?: Record<string, string | number>) => string,
  type: CustomerCommunicationType,
  customTypeLabel: string | null,
): string {
  if (type === 'Other' && customTypeLabel?.trim()) {
    return t('customers.communicationType.otherWithLabel', { label: customTypeLabel.trim() })
  }
  const key = COMMUNICATION_TYPE_LABEL_KEYS[type]
  return key ? t(key) : type
}

export interface CustomerCommunicationRule {
  id: string
  type: CustomerCommunicationType
  customTypeLabel: string | null
  channel: string
  ccEmail: string | null
  languageCode: string | null
  fallbackContactId: string | null
  isActive: boolean
  contactIds: string[]
}

/** Mirrors SaveCustomerCommunicationRuleRequest (create + update share the body). */
export interface SaveCommunicationRuleInput {
  type: CustomerCommunicationType
  customTypeLabel: string | null
  ccEmail: string | null
  languageCode: string | null
  fallbackContactId: string | null
  isActive: boolean
  contactIds: string[]
}

// --- Dieseltoeslag & PO-beleid (mirrors CustomerBillingConfigService DTOs) ---

export type DieselSurchargeBasis = 'OrderAmount' | 'InvoiceSubtotal'
export type DieselSurchargePresentation = 'PerOrderLine' | 'AggregatedLine'
export type DieselSurchargeRounding = 'NearestCent' | 'RoundUpCent'
export type PurchaseOrderPolicy = 'None' | 'Optional' | 'Required'

/** Vertaalsleutels — renderen als t(DIESEL_BASIS_LABEL_KEYS[basis]). */
export const DIESEL_BASIS_LABEL_KEYS: Record<DieselSurchargeBasis, string> = {
  OrderAmount: 'customers.dieselBasis.OrderAmount',
  InvoiceSubtotal: 'customers.dieselBasis.InvoiceSubtotal',
}

export const DIESEL_PRESENTATION_LABEL_KEYS: Record<DieselSurchargePresentation, string> = {
  PerOrderLine: 'customers.dieselPresentation.PerOrderLine',
  AggregatedLine: 'customers.dieselPresentation.AggregatedLine',
}

export const DIESEL_ROUNDING_LABEL_KEYS: Record<DieselSurchargeRounding, string> = {
  NearestCent: 'customers.dieselRounding.NearestCent',
  RoundUpCent: 'customers.dieselRounding.RoundUpCent',
}

export const PO_POLICY_LABEL_KEYS: Record<PurchaseOrderPolicy, string> = {
  None: 'customers.poPolicy.None',
  Optional: 'customers.poPolicy.Optional',
  Required: 'customers.poPolicy.Required',
}

export interface CustomerDieselSurcharge {
  enabled: boolean
  percent: number
  basis: DieselSurchargeBasis
  presentation: DieselSurchargePresentation
  rounding: DieselSurchargeRounding
  formulaDescription: string | null
  /** yyyy-MM-dd (DateOnly) or null. */
  effectiveFrom: string | null
  effectiveUntil: string | null
}

export interface CustomerPoNumber {
  id: string
  poNumber: string
  validFrom: string
  validUntil: string | null
  notes: string | null
  isEffectiveToday: boolean
}

export interface SaveCustomerPoNumberInput {
  poNumber: string
  validFrom: string
  validUntil: string | null
  notes: string | null
}

export interface CustomerPoPolicy {
  policy: PurchaseOrderPolicy
  effectivePoNumber: string | null
  history: CustomerPoNumber[]
}

export interface CustomerDetail extends CustomerVatProfile {
  id: string
  customerNumber: string
  name: string
  legalName: string | null
  vatNumber: string | null
  categoryId: string | null
  categoryName: string | null
  email: string | null
  phoneNumber: string | null
  website: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  invoiceEmail: string | null
  paymentTermDays: number
  defaultLanguageCode: string | null
  notes: string | null
  isActive: boolean
  isBlocked: boolean
  blockReason: string | null
  nickname: string | null
  companyNumber: string | null
  currencyCode: string
  iban: string | null
  bic: string | null
  bankName: string | null
  bankAccountNumber: string | null
  defaultLegalEntityId: string | null
  /** Read-only: laatste uitkomst van de Peppol-netwerkcontrole. */
  peppolValidationStatus: PeppolValidationStatus
  peppolValidatedAt: string | null
  peppolValidationReference: string | null
  /** Wave 2: toegestane facturerende entiteiten; leeg = alle actieve entiteiten toegestaan. */
  allowedLegalEntityIds?: string[] | null
  /** Wave 2: PerDossier | Weekly | Monthly | ByReference | Manual. */
  invoiceGrouping?: string
  /** GenerateOwn | CustomerDocument | PerOrder — wie het transportdocument aanlevert. */
  documentStrategy?: string
  contacts: CustomerContact[]
}

export interface CustomerInput extends CustomerVatProfile {
  /**
   * Contact persons created together with the customer (create flow only). Supersedes the
   * legacy single `initialContact`, which the form no longer sends.
   */
  contacts?: CustomerContactInput[] | null
  /** Optional manual customer number (create flow only); empty = automatic numbering. */
  customerNumber?: string | null
  name: string
  legalName: string | null
  vatNumber: string | null
  categoryId: string | null
  email: string | null
  phoneNumber: string | null
  website: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  invoiceEmail: string | null
  paymentTermDays: number
  defaultLanguageCode: string | null
  notes: string | null
  nickname: string | null
  companyNumber: string | null
  currencyCode: string | null
  iban: string | null
  bic: string | null
  bankName: string | null
  bankAccountNumber: string | null
  /** Standaard facturerende entiteit voor deze klant (null = tenant-standaard). */
  defaultLegalEntityId: string | null
  /** Wave 2: toegestane facturerende entiteiten; leeg = alle, null/weggelaten = ongewijzigd. */
  allowedLegalEntityIds?: string[] | null
  /** Wave 2: factuurgroepering; weggelaten = ongewijzigd (Manual bij aanmaak). */
  invoiceGrouping?: string | null
  /** Documentstrategie; weggelaten = ongewijzigd (GenerateOwn bij aanmaak). */
  documentStrategy?: string | null
}

export interface UpdateCustomerInput extends CustomerInput {
  isActive: boolean
}

export interface ChangeCustomerNumberInput {
  customerNumber: string
  reason: string
}

export type CustomerImportRowAction = 'Create' | 'Update' | 'Error'

export interface CustomerImportRow {
  rowNumber: number
  action: CustomerImportRowAction
  customerNumber: string | null
  name: string
  messages: string[]
}

export interface CustomerImportPreview {
  totalRows: number
  creates: number
  updates: number
  errors: number
  rows: CustomerImportRow[]
}

export interface CustomerImportCommit {
  created: number
  updated: number
  failed: number
  committed: boolean
  rows: CustomerImportRow[]
  errorWorkbookBase64: string | null
}

export interface CustomerContactInput {
  firstName: string
  lastName: string
  role: string | null
  email: string | null
  phoneNumber: string | null
  isPrimary: boolean
  notes: string | null
  displayName: string | null
  nickname: string | null
  mobilePhone: string | null
  departmentId: string | null
  preferredLanguageCode: string | null
  isActive: boolean
  contactType: CustomerContactType
}
