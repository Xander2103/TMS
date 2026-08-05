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
