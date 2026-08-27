/**
 * Single source of truth for the customer create/edit section navigation: the ordered
 * section list plus the field keys each section owns, driving the error badge on a section
 * tab and the first-error routing after a failed submit. CustomerForm renders one component
 * for both modes, so create and edit always present the same configuration.
 *
 * Sprint 1C — the order follows the business question the user is answering:
 * wie is de klant? → waar werken we? → wie spreken we aan? → financieel/commercieel.
 * Adressen and Contactpersonen are sections of their own; they used to be buried inside the
 * merged "Klantgegevens" quick-entry section.
 */
export const CUSTOMER_SECTION_ORDER = [
  'klantgegevens',
  'adressen',
  'contactpersonen',
  'facturatie',
  'fiscaal',
  'bank',
  'communicatie',
  'tarieven',
  'historiek',
] as const

export type CustomerSectionId = (typeof CUSTOMER_SECTION_ORDER)[number]

export const CUSTOMER_SECTION_FIELD_KEYS: Record<string, string[]> = {
  klantgegevens: [
    'name',
    'nickname',
    'customerNumber',
    'legalName',
    'categoryId',
    'isActive',
    'email',
    'phoneNumber',
    'website',
    'defaultLanguageCode',
    'notes',
  ],
  adressen: ['street', 'houseNumber', 'postalCode', 'city', 'countryCode'],
  // Contact-repeater errors use dynamic paths; see isContactpersonenFieldKey.
  contactpersonen: [],
  fiscaal: [
    'vatNumber',
    'companyNumber',
    'vatTreatment',
    'defaultVatRatePercent',
    'vatCountryCode',
    'peppolId',
    'peppolScheme',
    'peppolEnabled',
    'peppolDeliveryPreference',
    'buyerReference',
  ],
  bank: ['iban', 'bic', 'bankAccountNumber', 'currencyCode'],
  facturatie: ['invoiceEmail', 'paymentTermDays', 'defaultLegalEntityId'],
  communicatie: [],
  tarieven: [],
  historiek: [],
}

/**
 * Whether a (client or server) error path belongs to the Contactpersonen section: every
 * contact-repeater path — `contacts[i].…` from the multi-contact payload and legacy
 * `initialContact.…` paths the backend may still emit.
 */
export function isContactpersonenFieldKey(key: string): boolean {
  return key.startsWith('contacts[') || key.startsWith('initialContact.')
}
