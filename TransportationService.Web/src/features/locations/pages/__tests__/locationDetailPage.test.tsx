import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom'
import { LocationDetailPage } from '../LocationDetailPage'
import type { LocationDetail } from '../../types'

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

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))

const api = vi.hoisted(() => ({
  getLocation: vi.fn(),
  updateLocation: vi.fn(),
  duplicateLocation: vi.fn(),
  deleteLocation: vi.fn(),
  setLocationActive: vi.fn(),
}))
vi.mock('../../api/locationsApi', () => api)

// The edit mode mounts LocationForm; keep its dependencies inert for these tests.
const customersApi = vi.hoisted(() => ({
  searchCustomers: vi.fn().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 }),
  getCustomer: vi.fn().mockResolvedValue({ name: '', contacts: [] }),
}))
vi.mock('../../../customers/api/customersApi', () => customersApi)
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: () => null,
}))

function detail(overrides: Partial<LocationDetail> = {}): LocationDetail {
  return {
    id: 'loc-1',
    code: 'LOC-AAA11111',
    name: 'Magazijn Leuven',
    type: 'Warehouse',
    street: 'Industrieweg',
    houseNumber: '12',
    postalCode: '3000',
    city: 'Leuven',
    countryCode: 'BE',
    latitude: null,
    longitude: null,
    contactName: 'An Peeters',
    contactPhone: '016 12 34 56',
    contactMobile: null,
    contactEmail: 'an@klant.be',
    customerContactId: null,
    externalReference: null,
    openingHours: null,
    openingIntervals: [{ dayOfWeek: 1, fromTime: '08:00', toTime: '12:00', note: null }],
    loadingInstructions: 'Aanmelden aan poort 4.',
    unloadingInstructions: null,
    accessInstructions: null,
    accessRestrictions: null,
    vehicleRestrictions: null,
    trailerRestrictions: null,
    alfapassRequired: false,
    appointmentRequired: true,
    gate: 'Poort 4',
    receptionPoint: null,
    dock: null,
    routeDescription: null,
    deliveryByAppointmentOnly: false,
    heightRestrictionMeters: 4,
    weightRestrictionTons: null,
    adrAllowed: null,
    craneRequired: false,
    forkliftAvailable: true,
    driverInstructions: null,
    internalMemo: 'Sleutel bij buurman.',
    defaultLoadingMinutes: 30,
    defaultUnloadingMinutes: null,
    preferredArrivalFrom: null,
    preferredArrivalTo: null,
    earliestArrival: null,
    latestArrival: null,
    isActive: true,
    customerId: 'cust-1',
    customerName: 'Alfa NV',
    notes: null,
    isDefaultLoadingLocation: false,
    isDefaultUnloadingLocation: false,
    isDefaultBillingLocation: false,
    ...overrides,
  }
}

function CustomerStub() {
  const { id } = useParams()
  return <div>customer-{id}</div>
}

function DetailRoute() {
  return <LocationDetailPage />
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/locations/loc-1']}>
      <Routes>
        <Route path="/locations/:id" element={<DetailRoute />} />
        <Route path="/customers/:id" element={<CustomerStub />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = ['locations.edit', 'locations.create', 'locations.delete']
  toast.showSuccess.mockReset()
  toast.showError.mockReset()
  api.getLocation.mockReset().mockResolvedValue(detail())
  api.setLocationActive.mockReset().mockResolvedValue(undefined)
  api.duplicateLocation.mockReset()
})

describe('LocationDetailPage', () => {
  it('renders the header with code/type/klantlink and the card grid', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Magazijn Leuven' })).toBeInTheDocument()
    // Code appears in both breadcrumbs and header subtitle.
    expect(screen.getAllByText('LOC-AAA11111').length).toBeGreaterThan(0)
    expect(screen.getByText('Magazijn')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Alfa NV' })).toHaveAttribute('href', '/customers/cust-1')
    expect(screen.getByText('Actief')).toBeInTheDocument()

    // Cards.
    const adres = screen.getByRole('region', { name: 'Adres' })
    expect(within(adres).getByText('Industrieweg 12')).toBeInTheDocument()
    expect(within(adres).getByText('3000 Leuven')).toBeInTheDocument()

    const contact = screen.getByRole('region', { name: 'Contact' })
    expect(within(contact).getByRole('link', { name: '016 12 34 56' })).toHaveAttribute('href', 'tel:016123456')
    expect(within(contact).getByRole('link', { name: 'an@klant.be' })).toHaveAttribute('href', 'mailto:an@klant.be')

    const hours = screen.getByRole('region', { name: 'Openingsuren' })
    expect(within(hours).getByText('08:00–12:00')).toBeInTheDocument()
    // A day without intervals reads as closed.
    expect(within(hours).getAllByText('Gesloten').length).toBe(6)

    const planning = screen.getByRole('region', { name: 'Planning' })
    expect(within(planning).getByText('30 min')).toBeInTheDocument()

    // Operationeel: only ACTIVE flags as a ✓-list, no "Nee"-rows.
    const operational = screen.getByRole('region', { name: 'Operationeel' })
    expect(within(operational).getByText('Poort 4')).toBeInTheDocument()
    expect(within(operational).getByText('Heftruck beschikbaar')).toBeInTheDocument()
    expect(within(operational).getByText('Afspraak verplicht')).toBeInTheDocument()
    expect(within(operational).queryByText(/Kraan vereist/)).not.toBeInTheDocument()
    expect(within(operational).getByText('4 m')).toBeInTheDocument()

    // Interne informatie is clearly labelled.
    const internal = screen.getByRole('region', { name: 'Interne informatie' })
    expect(within(internal).getByText('Alleen interne gebruikers')).toBeInTheDocument()
    expect(within(internal).getByText('Sleutel bij buurman.')).toBeInTheDocument()
  })

  it('shows "Geen bijzonderheden" when the location has no operational particulars', async () => {
    api.getLocation.mockResolvedValue(
      detail({
        gate: null,
        receptionPoint: null,
        appointmentRequired: false,
        forkliftAvailable: false,
        heightRestrictionMeters: null,
        internalMemo: null,
      }),
    )
    renderPage()
    const operational = await screen.findByRole('region', { name: 'Operationeel' })
    expect(within(operational).getByText('Geen bijzonderheden')).toBeInTheDocument()
    // Empty internal card is hidden entirely.
    expect(screen.queryByRole('region', { name: 'Interne informatie' })).not.toBeInTheDocument()
  })

  it('offers Bewerken / Dupliceren / Deactiveren in the header and deactivates via ConfirmDialog', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Magazijn Leuven' })

    expect(screen.getByRole('button', { name: 'Bewerken' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Dupliceren' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Deactiveren' }))
    const dialog = screen.getByRole('dialog', { name: 'Locatie deactiveren' })
    expect(within(dialog).getByText(/'Magazijn Leuven' deactiveren\?/)).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactiveren' }))

    expect(api.setLocationActive).toHaveBeenCalledWith('loc-1', false)
    expect(await screen.findByText('Inactief')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Activeren' })).toBeInTheDocument()
  })

  it('duplicates the location and navigates to the copy', async () => {
    api.duplicateLocation.mockResolvedValue(detail({ id: 'copy-1', code: 'LOC-BBB22222', name: 'Magazijn Leuven (kopie)' }))
    api.getLocation.mockImplementation((id: string) =>
      Promise.resolve(id === 'copy-1' ? detail({ id: 'copy-1', code: 'LOC-BBB22222', name: 'Magazijn Leuven (kopie)' }) : detail()),
    )
    renderPage()
    await screen.findByRole('heading', { name: 'Magazijn Leuven' })

    await userEvent.click(screen.getByRole('button', { name: 'Dupliceren' }))

    expect(api.duplicateLocation).toHaveBeenCalledWith('loc-1')
    expect(await screen.findByRole('heading', { name: 'Magazijn Leuven (kopie)' })).toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalledWith('Locatie gedupliceerd.')
  })

  it('opens the sectioned edit form via Bewerken', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Magazijn Leuven' })
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    expect(screen.getByRole('tab', { name: /Algemeen/ })).toBeInTheDocument()
    // The action bar renders twice (top + sticky bottom).
    expect(screen.getAllByRole('button', { name: 'Opslaan' }).length).toBeGreaterThan(0)
  })

  it('hides mutating actions without permissions', async () => {
    auth.permissions = []
    renderPage()
    await screen.findByRole('heading', { name: 'Magazijn Leuven' })
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Dupliceren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactiveren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })
})
