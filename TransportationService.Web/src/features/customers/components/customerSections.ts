/**
 * Single source of truth for the customer create/edit section navigation: the field keys
 * each section owns, driving the error badge on a section tab and the first-error routing
 * after a failed submit. The ordered section list itself is built inside CustomerForm (one
 * component, both modes) so create and edit always present the same configuration.
 */
export const CUSTOMER_SECTION_FIELD_KEYS: Record<string, string[]> = {
  algemeen: ['name', 'nickname', 'customerNumber', 'legalName', 'categoryId'],
  contact: ['email', 'phoneNumber', 'website', 'defaultLanguageCode'],
  adressen: ['street', 'houseNumber', 'postalCode', 'city', 'countryCode'],
  contactpersonen: ['initialContact.firstName', 'initialContact.lastName'],
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
  notities: [],
}
