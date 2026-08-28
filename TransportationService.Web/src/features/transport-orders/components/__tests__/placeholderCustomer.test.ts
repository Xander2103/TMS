import { describe, expect, it } from 'vitest'
import { isPlaceholderCustomerName } from '../placeholderCustomer'

describe('isPlaceholderCustomerName', () => {
  it('recognises the temporary-customer naming convention in NL/FR/EN', () => {
    expect(isPlaceholderCustomerName('VCB tijdelijk')).toBe(true)
    expect(isPlaceholderCustomerName('Tijdelijke klant Antwerpen')).toBe(true)
    expect(isPlaceholderCustomerName('TMP - nog te bepalen')).toBe(true)
    expect(isPlaceholderCustomerName('Client provisoire')).toBe(true)
    expect(isPlaceholderCustomerName('Unknown consignee')).toBe(true)
  })

  it('leaves ordinary customer names alone', () => {
    expect(isPlaceholderCustomerName('Tempo Logistics NV')).toBe(false)
    expect(isPlaceholderCustomerName('Client SA')).toBe(false)
    expect(isPlaceholderCustomerName(null)).toBe(false)
    expect(isPlaceholderCustomerName('')).toBe(false)
  })
})
