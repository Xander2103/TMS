import type { CustomerInput } from './types'

/**
 * The few fields a planner fills to create a customer WITHOUT leaving the dossier flow. Only the
 * name is required — exactly the backend rule (`POST /api/customers`: "Naam is verplicht.");
 * nothing else is invented as mandatory. The customer number, numbering and all fiscal,
 * invoicing and Peppol settings stay on their server defaults, the same ones the full
 * Klanten → Nieuwe klant form sends when those fields are left untouched.
 */
export interface QuickCustomerFields {
  name: string
  /** Only sent by users with `customers.manage_fiscal` (the backend refuses it otherwise). */
  vatNumber: string
  email: string
  phoneNumber: string
  street: string
  houseNumber: string
  postalCode: string
  city: string
  countryCode: string | null
}

export const EMPTY_QUICK_CUSTOMER: QuickCustomerFields = {
  name: '',
  vatNumber: '',
  email: '',
  phoneNumber: '',
  street: '',
  houseNumber: '',
  postalCode: '',
  city: '',
  countryCode: null,
}

function nullable(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

/**
 * Same payload shape and defaults as `CustomerForm` in create mode (payment term 30 days,
 * DomesticVat, Peppol off, no contacts), so a quick-created customer is indistinguishable from
 * one created on the Klanten page — one backend rule set, one DTO.
 */
export function buildQuickCustomerInput(fields: QuickCustomerFields, options: { canManageFiscal: boolean }): CustomerInput {
  return {
    name: fields.name.trim(),
    legalName: null,
    vatNumber: options.canManageFiscal ? nullable(fields.vatNumber) : null,
    categoryId: null,
    email: nullable(fields.email),
    phoneNumber: nullable(fields.phoneNumber),
    website: null,
    street: nullable(fields.street),
    houseNumber: nullable(fields.houseNumber),
    postalCode: nullable(fields.postalCode),
    city: nullable(fields.city),
    countryCode: fields.countryCode || null,
    invoiceEmail: null,
    paymentTermDays: 30,
    defaultLanguageCode: null,
    notes: null,
    nickname: null,
    companyNumber: null,
    currencyCode: null,
    iban: null,
    bic: null,
    bankName: null,
    bankAccountNumber: null,
    defaultLegalEntityId: null,
    vatTreatment: 'DomesticVat',
    defaultVatRatePercent: null,
    vatCountryCode: null,
    vatNotes: null,
    peppolId: null,
    peppolScheme: null,
    peppolEnabled: false,
    peppolDeliveryPreference: 'Peppol',
    buyerReference: null,
    invoiceLanguageCode: null,
    purchaseOrderRequired: false,
    signedDeliveryNoteRequired: false,
    customerReferenceRequired: false,
  }
}
