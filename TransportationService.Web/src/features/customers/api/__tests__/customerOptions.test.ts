import { describe, expect, it, vi } from 'vitest'

/**
 * PERF-H3: the shared picker source asks the server for what the user typed (one short page)
 * and renders name + "KL-… · BE0…" so a planner recognises the hit.
 */
const api = vi.hoisted(() => ({ searchCustomers: vi.fn(), getCustomer: vi.fn() }))
vi.mock('../customersApi', () => api)

import { customerOptionSubtitle, PICKER_PAGE_SIZE, resolveCustomerLabel, searchCustomerOptions, toCustomerOption } from '../customerOptions'

const customer = {
  id: 'c-1',
  customerNumber: 'KL-0133',
  name: 'Van Caudenberg BV',
  city: 'Gent',
  countryCode: 'BE',
  categoryName: null,
  isActive: true,
  isBlocked: false,
  vatNumber: 'BE0123456749',
}

describe('customerOptions', () => {
  it('searches one short page with the typed query and the active filter', async () => {
    api.searchCustomers.mockResolvedValue({ items: [customer], totalCount: 1, page: 1, pageSize: PICKER_PAGE_SIZE })
    const signal = new AbortController().signal

    const options = await searchCustomerOptions('  caud ', signal, { isActive: true })

    expect(api.searchCustomers).toHaveBeenCalledWith(
      { search: 'caud', isActive: true, categoryId: undefined, page: 1, pageSize: PICKER_PAGE_SIZE },
      signal,
    )
    expect(options).toEqual([toCustomerOption(customer)])
    expect(options[0]).toMatchObject({ value: 'c-1', label: 'Van Caudenberg BV', subtitle: 'KL-0133 · BE0123456749 · Gent' })
    expect(options[0].keywords).toContain('KL-0133')
    expect(options[0].keywords).toContain('BE0123456749')
  })

  it('sends no search term for an empty query (the server returns its first page)', async () => {
    api.searchCustomers.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: PICKER_PAGE_SIZE })
    await searchCustomerOptions('', new AbortController().signal)
    expect(api.searchCustomers.mock.calls.at(-1)?.[0]).toMatchObject({ search: undefined, pageSize: PICKER_PAGE_SIZE })
  })

  it('leaves out missing VAT numbers and cities from the subtitle', () => {
    expect(customerOptionSubtitle({ customerNumber: 'KL-0001', vatNumber: null, city: null })).toBe('KL-0001')
    expect(customerOptionSubtitle({ customerNumber: 'KL-0001', vatNumber: undefined, city: 'Gent' })).toBe('KL-0001 · Gent')
  })

  it('resolves a lone id to the customer name through the detail endpoint', async () => {
    api.getCustomer.mockResolvedValue({ ...customer, contacts: [] })
    expect(await resolveCustomerLabel('c-1', new AbortController().signal)).toBe('Van Caudenberg BV')
    expect(api.getCustomer).toHaveBeenCalledWith('c-1')
  })
})
