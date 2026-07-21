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
}

export type VatTreatment =
  | 'DomesticVat'
  | 'ReverseCharge'
  | 'IntraCommunitySupply'
  | 'ExportOutsideEu'
  | 'VatExempt'
  | 'Other'

export const VAT_TREATMENT_LABELS: Record<VatTreatment, string> = {
  DomesticVat: 'Binnenlandse BTW',
  ReverseCharge: 'BTW verlegd',
  IntraCommunitySupply: 'Intracommunautaire levering',
  ExportOutsideEu: 'Uitvoer buiten de EU',
  VatExempt: 'Vrijgesteld van BTW',
  Other: 'Afwijkende regeling',
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

export interface CustomerVatProfile {
  vatTreatment: VatTreatment
  defaultVatRatePercent: number | null
  vatCountryCode: string | null
  vatNotes: string | null
  peppolId: string | null
  peppolScheme: string | null
  invoiceLanguageCode: string | null
  purchaseOrderRequired: boolean
  signedDeliveryNoteRequired: boolean
  customerReferenceRequired: boolean
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
  contacts: CustomerContact[]
}

export interface CustomerInput extends CustomerVatProfile {
  /** Optional first contact created together with the customer (create flow only). */
  initialContact?: CustomerContactInput | null
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
}
