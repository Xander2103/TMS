/**
 * Single source of truth for the customer create/edit section navigation: the field keys
 * each section owns, driving the error badge on a section tab and the first-error routing
 * after a failed submit. The ordered section list itself is built inside CustomerForm (one
 * component, both modes) so create and edit always present the same configuration.
 *
 * `klantgegevens` is the merged quick-entry section (bedrijfsgegevens + algemene
 * contactgegevens + contactpersonen + hoofdzetel-adres); contact-repeater errors use dynamic
 * paths (`contacts[i].…`), covered by `isKlantgegevensFieldKey` instead of the static list.
 */
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
    'street',
    'houseNumber',
    'postalCode',
    'city',
    'countryCode',
  ],
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
 * Whether a (client or server) error path belongs to the merged Klantgegevens section:
 * its static keys plus every contact-repeater path — `contacts[i].…` from the multi-contact
 * payload and legacy `initialContact.…` paths the backend may still emit.
 */
export function isKlantgegevensFieldKey(key: string): boolean {
  return (
    CUSTOMER_SECTION_FIELD_KEYS.klantgegevens.includes(key) ||
    key.startsWith('contacts[') ||
    key.startsWith('initialContact.')
  )
}
