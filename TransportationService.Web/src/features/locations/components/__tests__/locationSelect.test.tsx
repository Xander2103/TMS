import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LocationSelect } from '../LocationSelect'

const api = vi.hoisted(() => ({ getLocationOptions: vi.fn() }))
vi.mock('../../api/locationsApi', () => api)

const option = {
  id: 'loc-1',
  code: 'LOC-1',
  name: 'Magazijn Antwerpen',
  type: 'Warehouse' as const,
  city: 'Antwerpen',
  isDefaultLoadingLocation: false,
  isDefaultUnloadingLocation: false,
  isDefaultBillingLocation: false,
  // Phase 7: options endpoint also carries the address line + postal code.
  address: 'Noorderlaan 10',
  postalCode: '2030',
}

const bare = {
  id: 'loc-2',
  code: 'LOC-2',
  name: 'Depot Gent',
  type: 'Depot' as const,
  city: null,
  isDefaultLoadingLocation: false,
  isDefaultUnloadingLocation: false,
  isDefaultBillingLocation: false,
}

beforeEach(() => {
  api.getLocationOptions.mockReset().mockResolvedValue([option, bare])
})

describe('LocationSelect (Phase 7 address line)', () => {
  it('renders the option with its full address line', async () => {
    render(<LocationSelect value="" onChange={() => {}} />)
    await userEvent.click(screen.getByRole('combobox'))
    expect(
      await screen.findByText('Magazijn Antwerpen (LOC-1) — Noorderlaan 10, 2030 Antwerpen'),
    ).toBeInTheDocument()
    // Without an address the plain label stays.
    expect(screen.getByText('Depot Gent (LOC-2)')).toBeInTheDocument()
  })

  it('finds a location by typing its postal code', async () => {
    render(<LocationSelect value="" onChange={() => {}} />)
    const combobox = screen.getByRole('combobox')
    await userEvent.click(combobox)
    await screen.findByText('Magazijn Antwerpen (LOC-1) — Noorderlaan 10, 2030 Antwerpen')
    await userEvent.type(combobox, '2030')
    expect(screen.getByText('Magazijn Antwerpen (LOC-1) — Noorderlaan 10, 2030 Antwerpen')).toBeInTheDocument()
    expect(screen.queryByText('Depot Gent (LOC-2)')).not.toBeInTheDocument()
  })
})

describe('LocationSelect (central address master provenance)', () => {
  // The backend already sorts customer → company → other customers; the picker must keep that
  // order and say in plain words where a non-customer address comes from.
  const ownAddress = { ...bare, id: 'own', code: 'OWN', name: 'Eigen kade', isLinkedToCustomer: true, linkedCustomerCount: 1, linkedCustomerNames: null }
  const companyAddress = { ...bare, id: 'company', code: 'CMP', name: 'Hoofddepot', isLinkedToCustomer: false, linkedCustomerCount: 0, linkedCustomerNames: null }
  const foreignAddress = {
    ...bare, id: 'foreign', code: 'FRN', name: 'Aankomsthal',
    isLinkedToCustomer: false, linkedCustomerCount: 2, linkedCustomerNames: 'Distri-Frais SPRL, Euro Retail Group',
  }

  beforeEach(() => {
    api.getLocationOptions.mockReset().mockResolvedValue([ownAddress, companyAddress, foreignAddress])
  })

  it('marks addresses of other customers as shared and company addresses as such, customer addresses first', async () => {
    render(<LocationSelect value="" onChange={() => {}} customerId="cust-1" />)
    await userEvent.click(screen.getByRole('combobox'))

    const shared = await screen.findByText('Aankomsthal (FRN) — gedeeld adres (Distri-Frais SPRL, Euro Retail Group)')
    const company = screen.getByText('Hoofddepot (CMP) — bedrijfsadres')
    const own = screen.getByText('Eigen kade (OWN)')
    expect(shared).toBeInTheDocument()
    expect(api.getLocationOptions).toHaveBeenCalledWith(undefined, 'cust-1')

    // Own address stays unmarked and precedes the company and shared ones in the list.
    const labels = screen.getAllByRole('option').map((o) => o.textContent)
    expect(labels.indexOf(own.textContent!)).toBeLessThan(labels.indexOf(company.textContent!))
    expect(labels.indexOf(company.textContent!)).toBeLessThan(labels.indexOf(shared.textContent!))
  })
})
