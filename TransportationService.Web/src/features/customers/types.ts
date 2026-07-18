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
}

export interface CustomerDetail {
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
  contacts: CustomerContact[]
}

export interface CustomerInput {
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
}

export interface UpdateCustomerInput extends CustomerInput {
  isActive: boolean
}

export interface CustomerContactInput {
  firstName: string
  lastName: string
  role: string | null
  email: string | null
  phoneNumber: string | null
  isPrimary: boolean
  notes: string | null
}
