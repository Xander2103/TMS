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
  isActive: boolean
  isDefault: boolean
}

/** Mirrors LegalEntityOptionDto — compact shape for pickers (sidebar switcher, invoice forms). */
export interface LegalEntityOption {
  id: string
  displayName: string
  vatNumber: string | null
  isDefault: boolean
  isActive: boolean
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
}

/** Mirrors ActiveLegalEntityDto — the user's own (cosmetic) active-entity preference. */
export interface ActiveLegalEntity {
  legalEntityId: string | null
}
