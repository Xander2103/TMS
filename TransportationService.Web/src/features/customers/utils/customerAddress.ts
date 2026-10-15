import type { CustomerDetail } from '../types'

/** "Noorderlaan 147, 2030 Antwerpen (BE)" — empty parts are left out. */
export function formatCustomerAddress(customer: Pick<CustomerDetail, 'street' | 'houseNumber' | 'postalCode' | 'city' | 'countryCode'>): string {
  const street = [customer.street, customer.houseNumber].filter(Boolean).join(' ')
  const place = [customer.postalCode, customer.city].filter(Boolean).join(' ')
  const line = [street, place].filter(Boolean).join(', ')
  return line && customer.countryCode ? `${line} (${customer.countryCode})` : line
}
