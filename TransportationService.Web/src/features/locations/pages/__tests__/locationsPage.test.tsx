import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom'
import { LocationsPage } from '../LocationsPage'
import type { LocationGroup, LocationListItem } from '../../types'

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const api = vi.hoisted(() => ({
  searchLocations: vi.fn(),
  searchLocationsGrouped: vi.fn(),
}))
vi.mock('../../api/locationsApi', () => api)

const customersApi = vi.hoisted(() => ({ searchCustomers: vi.fn() }))
vi.mock('../../../customers/api/customersApi', () => customersApi)

vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

function row(overrides: Partial<LocationListItem> = {}): LocationListItem {
  return {
    id: 'loc-1',
    code: 'LOC-AAA11111',
    name: 'Depot Antwerpen',
    type: 'ConstructionSite',
    city: 'Antwerpen',
    countryCode: 'BE',
    customerName: null,
    isActive: true,
    isDefaultLoadingLocation: false,
    isDefaultUnloadingLocation: false,
    isDefaultBillingLocation: false,
    ...overrides,
  }
}

function group(overrides: Partial<LocationGroup> = {}): LocationGroup {
  return {
    customerId: 'cust-1',
    customerName: 'Alfa NV',
    locations: [row({ customerName: 'Alfa NV' })],
    ...overrides,
  }
}

function DetailStub() {
  const { id } = useParams()
  return <div>detail-{id}</div>
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/locations']}>
      <Routes>
        <Route path="/locations" element={<LocationsPage />} />
        <Route path="/locations/:id" element={<DetailStub />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  window.localStorage.clear()
  auth.permissions = ['locations.create', 'locations.edit']
  api.searchLocations.mockReset().mockResolvedValue({ items: [row()], totalCount: 1, page: 1, pageSize: 25 })
  api.searchLocationsGrouped.mockReset().mockResolvedValue({
    items: [group(), group({ customerId: null, customerName: null, locations: [row({ id: 'loc-2', code: 'LOC-VRIJ', name: 'Vrij terrein' })] })],
    totalCount: 2,
    page: 1,
    pageSize: 25,
  })
  customersApi.searchCustomers.mockReset().mockResolvedValue({
    items: [
      { id: 'cust-1', name: 'Alfa NV' },
      { id: 'cust-2', name: 'Beta BV' },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 200,
  })
})

describe('LocationsPage — platte weergave', () => {
  it('shows the Dutch type label and no row action buttons (acties leven op de detailpagina)', async () => {
    renderPage()
    expect(await screen.findByText('Depot Antwerpen')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).getByText('Werf')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Dupliceren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactiveren' })).not.toBeInTheDocument()
  })

  it('navigates to the detail page on row click', async () => {
    renderPage()
    await userEvent.click(await screen.findByText('Depot Antwerpen'))
    expect(await screen.findByText('detail-loc-1')).toBeInTheDocument()
  })

  it('passes server-side sort params and toggles direction; the header exposes aria-sort', async () => {
    renderPage()
    await screen.findByText('Depot Antwerpen')

    await userEvent.click(screen.getByRole('button', { name: /Naam/ }))
    await waitFor(() =>
      expect(api.searchLocations).toHaveBeenCalledWith(expect.objectContaining({ sort: 'name', dir: 'asc' })),
    )
    await screen.findByText('Depot Antwerpen')
    const sortedHeader = screen.getByRole('button', { name: /Naam/ }).closest('th')
    expect(sortedHeader).toHaveAttribute('aria-sort', 'ascending')

    await userEvent.click(screen.getByRole('button', { name: /Naam/ }))
    await waitFor(() =>
      expect(api.searchLocations).toHaveBeenCalledWith(expect.objectContaining({ sort: 'name', dir: 'desc' })),
    )
  })

  it('filters on customer via the searchable klantfilter (customerId query param)', async () => {
    renderPage()
    await screen.findByText('Depot Antwerpen')

    const combo = screen.getByRole('combobox', { name: 'Klant' })
    await userEvent.click(combo)
    await userEvent.click(await screen.findByRole('option', { name: 'Beta BV' }))

    await waitFor(() =>
      expect(api.searchLocations).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'cust-2' })),
    )
  })

  it('passes the postcode filter through', async () => {
    renderPage()
    await screen.findByText('Depot Antwerpen')
    await userEvent.type(screen.getByLabelText('Postcode'), '2000')
    await waitFor(() =>
      expect(api.searchLocations).toHaveBeenCalledWith(expect.objectContaining({ postalCode: '2000' })),
    )
  })
})

describe('LocationsPage — weergave per klant', () => {
  it('renders collapsible groups with the ongekoppelde bucket last', async () => {
    renderPage()
    await screen.findByText('Depot Antwerpen')

    await userEvent.click(screen.getByRole('button', { name: 'Per klant' }))

    expect(await screen.findByRole('button', { name: /Alfa NV/ })).toBeInTheDocument()
    const toggles = screen.getAllByRole('button', { name: /\(\d+\)/ })
    expect(toggles[toggles.length - 1]).toHaveTextContent('Ongekoppelde locaties')
    expect(screen.getByText('Vrij terrein')).toBeInTheDocument()
    expect(api.searchLocationsGrouped).toHaveBeenCalledWith(expect.objectContaining({ innerSort: 'name' }))

    // Collapsing hides the group's rows without removing the group header.
    await userEvent.click(screen.getByRole('button', { name: /Alfa NV/ }))
    expect(screen.getByRole('button', { name: /Alfa NV/ })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Depot Antwerpen')).not.toBeInTheDocument()
  })

  it('passes the binnen-sortering to the grouped endpoint', async () => {
    renderPage()
    await screen.findByText('Depot Antwerpen')
    await userEvent.click(screen.getByRole('button', { name: 'Per klant' }))
    await screen.findByRole('button', { name: /Alfa NV/ })

    await userEvent.selectOptions(screen.getByLabelText('Sorteer binnen klant'), 'code')
    await waitFor(() =>
      expect(api.searchLocationsGrouped).toHaveBeenCalledWith(expect.objectContaining({ innerSort: 'code' })),
    )
  })

  it('remembers the chosen view in localStorage', async () => {
    renderPage()
    await screen.findByText('Depot Antwerpen')
    await userEvent.click(screen.getByRole('button', { name: 'Per klant' }))
    expect(window.localStorage.getItem('locations.viewMode')).toBe('grouped')
    expect(screen.getByRole('button', { name: 'Per klant' })).toHaveAttribute('aria-pressed', 'true')
  })
})
