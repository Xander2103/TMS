import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createPreparedLocation,
  createPreparedLocations,
  preparedLocationToInput,
  type PreparedLocation,
} from '../preparedLocations'
import { createLocation } from '../../../locations/api/locationsApi'

vi.mock('../../../locations/api/locationsApi', () => ({
  createLocation: vi.fn(),
}))

const createLocationMock = vi.mocked(createLocation)

function row(overrides: Partial<PreparedLocation>): PreparedLocation {
  return { ...createPreparedLocation(), ...overrides }
}

describe('preparedLocationToInput', () => {
  it('maps a staged row to a full POST /api/locations payload with the customerId', () => {
    const input = preparedLocationToInput(
      row({
        name: '  Depot A ',
        type: 'Warehouse',
        street: ' Kaai ',
        houseNumber: ' 12 ',
        postalCode: ' 2000 ',
        city: ' Antwerpen ',
        countryCode: 'BE',
        contactPhone: ' +32 3 111 22 33 ',
      }),
      'cust-1',
    )

    expect(input).toMatchObject({
      code: '',
      name: 'Depot A',
      type: 'Warehouse',
      street: 'Kaai',
      houseNumber: '12',
      postalCode: '2000',
      city: 'Antwerpen',
      countryCode: 'BE',
      contactPhone: '+32 3 111 22 33',
      customerId: 'cust-1',
      isActive: true,
    })
    // Untouched optional fields ride along explicitly (full DTO incl. openingIntervals).
    expect(input.openingIntervals).toEqual([])
    expect(input.isDefaultLoadingLocation).toBe(false)
  })

  it('sends null for blank optional fields', () => {
    const input = preparedLocationToInput(row({ name: 'Depot', street: '  ', city: '' }), 'cust-1')
    expect(input.street).toBeNull()
    expect(input.city).toBeNull()
    expect(input.contactPhone).toBeNull()
  })
})

describe('createPreparedLocations', () => {
  beforeEach(() => {
    createLocationMock.mockReset()
  })

  it('creates rows sequentially and reports every row ok', async () => {
    createLocationMock.mockResolvedValue({ id: 'loc' } as never)
    const rows = [row({ name: 'A' }), row({ name: 'B' })]

    const results = await createPreparedLocations('cust-1', rows)

    expect(createLocationMock).toHaveBeenCalledTimes(2)
    expect(createLocationMock.mock.calls[0][0]).toMatchObject({ name: 'A', customerId: 'cust-1' })
    expect(createLocationMock.mock.calls[1][0]).toMatchObject({ name: 'B', customerId: 'cust-1' })
    expect(results).toEqual([
      { key: rows[0].key, label: 'A', ok: true },
      { key: rows[1].key, label: 'B', ok: true },
    ])
  })

  it('never throws on partial failure and keeps the failed row retryable', async () => {
    createLocationMock
      .mockResolvedValueOnce({ id: 'loc' } as never)
      .mockRejectedValueOnce(new Error('Code bestaat al.'))
    const rows = [row({ name: 'A' }), row({ name: 'B' })]

    const results = await createPreparedLocations('cust-1', rows)

    expect(results[0]).toEqual({ key: rows[0].key, label: 'A', ok: true })
    expect(results[1]).toEqual({ key: rows[1].key, label: 'B', ok: false, error: 'Code bestaat al.' })
  })

  it('falls back to a Dutch message for unknown errors', async () => {
    createLocationMock.mockRejectedValueOnce('boom')
    const results = await createPreparedLocations('cust-1', [row({ name: '' })])
    expect(results[0].ok).toBe(false)
    expect(results[0].label).toBe('Locatie')
    expect(results[0].error).toBe('De locatie kon niet worden aangemaakt.')
  })
})
