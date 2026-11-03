/** Mirrors LegalEntityDto (camelCase JSON of the backend record). */
export interface LegalEntity {
  id: string
  legalName: string
  tradingName: string | null
  companyNumber: string | null
  vatNumber: string | null
  peppolId: string | null
  peppolScheme: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  email: string | null
  phoneNumber: string | null
  website: string | null
  iban: string | null
  bic: string | null
  bankName: string | null
  defaultCurrency: string
  paymentTermDays: number
  invoiceNumberFormat: string
  invoiceSequencePadding: number
  invoicePrefix: string | null
  creditNotePrefix: string | null
  invoiceFooter: string | null
  hasLogo: boolean
  logoFileName: string | null
  /** Banner photo of the dossier and order headers; absent on older payloads. */
  hasHeaderImage?: boolean
  headerImageFileName?: string | null
  isActive: boolean
  isDefault: boolean
  /** MD-M2: optimistic-concurrency token; echoed as `expectedVersion`. Absent on older payloads. */
  version?: number
}

/** Mirrors LegalEntityOptionDto — compact shape for pickers (sidebar switcher, invoice forms). */
export interface LegalEntityOption {
  id: string
  displayName: string
  vatNumber: string | null
  isDefault: boolean
  isActive: boolean
  hasHeaderImage?: boolean
}

/** Mirrors SaveLegalEntityRequest (create + update share the same body). */
export interface SaveLegalEntityInput {
  legalName: string
  tradingName: string | null
  companyNumber: string | null
  vatNumber: string | null
  peppolId: string | null
  peppolScheme: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  email: string | null
  phoneNumber: string | null
  website: string | null
  iban: string | null
  bic: string | null
  bankName: string | null
  defaultCurrency: string | null
  paymentTermDays: number
  invoiceNumberFormat: string | null
  invoiceSequencePadding: number
  invoicePrefix: string | null
  creditNotePrefix: string | null
  invoiceFooter: string | null
  isDefault: boolean
  /** MD-M2: the version the edit is based on (update only); a stale one yields 409 concurrency_conflict. */
  expectedVersion?: number
}

/** Mirrors ActiveLegalEntityDto — the user's own (cosmetic) active-entity preference. */
export interface ActiveLegalEntity {
  legalEntityId: string | null
}
