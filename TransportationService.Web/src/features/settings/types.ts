/** Mirror of the backend CompanySettingsDto / UpdateCompanySettingsRequest (identical field set). */
export interface CompanySettings {
  companyLegalName: string | null
  tradingName: string | null
  companyNumber: string | null
  vatNumber: string | null
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  operationalStreet: string | null
  operationalHouseNumber: string | null
  operationalPostalCode: string | null
  operationalCity: string | null
  operationalCountryCode: string | null
  email: string | null
  phoneNumber: string | null
  website: string | null
  defaultLanguage: string
  timezone: string
  defaultCurrency: string
  dateFormat: string
  decimalSeparator: string
  defaultWeightUnit: string
  defaultDistanceUnit: string
  iban: string | null
  invoiceEmail: string | null
  paymentTermDays: number
  defaultVatRatePercent: number
  defaultLoadingMinutes: number
  defaultUnloadingMinutes: number
  trainingConflictSeverity: string
  capacityConflictSeverity: string
  shiftOverlapConflictSeverity: string
  /** Manual | Propose | Automatic: wat er gebeurt met een herlevering na een mislukte stop. */
  redeliveryMode: string
  /** Drempel (1-720 min) voor klantmeldingen bij ETA-verschuiving; null = geen drempelmeldingen. */
  etaShiftNotifyMinutes: number | null
  qualificationExpiryWarningDays: number
  employeeNumberPrefix: string | null
  employeeNumberNextValue: number
  customerNumberPrefix: string | null
  customerNumberNextValue: number
  driverNumberPrefix: string | null
  driverNumberNextValue: number
  orderNumberPrefix: string | null
  orderNumberNextValue: number
  tripNumberPrefix: string | null
  tripNumberNextValue: number
  invoiceNumberPrefix: string | null
  invoiceNumberNextValue: number
  vehicleNumberPrefix: string | null
  vehicleNumberNextValue: number
  trailerNumberPrefix: string | null
  trailerNumberNextValue: number
  defaultPageSize: number
  logoReference: string | null
}

export type UpdateCompanySettings = CompanySettings
