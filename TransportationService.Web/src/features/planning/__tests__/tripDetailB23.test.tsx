import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { TripDetailPage } from '../pages/TripDetailPage'
import type { TripDetail } from '../types'

/** B23: trip detail — real links to orders and dossiers, visible save state, readiness summary first. */
const auth = vi.hoisted(() => ({ permissions: new Set<string>(['planning.edit']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const planningApi = vi.hoisted(() => ({
  getTrip: vi.fn(),
  updateTrip: vi.fn(),
  changeTripStatus: vi.fn(),
  deleteTrip: vi.fn(),
}))
vi.mock('../api/planningApi', () => planningApi)

const driversApi = vi.hoisted(() => ({ searchDrivers: vi.fn(), getDriver: vi.fn() }))
vi.mock('../../drivers/api/driversApi', () => driversApi)

const vehiclesApi = vi.hoisted(() => ({ getVehicleOptions: vi.fn() }))
vi.mock('../../vehicles/api/vehiclesApi', () => vehiclesApi)

const trailersApi = vi.hoisted(() => ({ getTrailerOptions: vi.fn() }))
vi.mock('../../trailers/api/trailersApi', () => trailersApi)

const ordersApi = vi.hoisted(() => ({ searchTransportOrders: vi.fn() }))
vi.mock('../../transport-orders/api/transportOrdersApi', () => ordersApi)

vi.mock('../../my-trips/api/myTripsApi', () => ({ getTripExecution: vi.fn() }))
vi.mock('../../pod/api/podApi', () => ({ getPodForStop: vi.fn() }))
const packagesApi = vi.hoisted(() => ({ getTripPackageReadiness: vi.fn() }))
vi.mock('../../packages/api/packagesApi', () => packagesApi)
vi.mock('../../transport-orders/api/transportDocumentsApi', () => ({ downloadTripDocuments: vi.fn() }))
vi.mock('../../trip-costing/components/TripCostingPanel', () => ({ TripCostingPanel: () => null }))

function trip(overrides: Partial<TripDetail> = {}): TripDetail {
  return {
    id: 'trip-1',
    tripNumber: 'RIT-0001',
    tripDate: '2026-09-22',
    status: 'Draft',
    driverId: null,
    driverName: null,
    vehicleId: null,
    vehicleNumber: null,
    vehicleLicensePlate: null,
    trailerId: null,
    trailerNumber: null,
    plannedStart: null,
    plannedEnd: null,
    plannedDistanceKm: null,
    plannedEmptyKm: null,
    actualDistanceKm: null,
    actualEmptyKm: null,
    notes: null,
    orders: [],
    conflicts: [],
    allowedTransitions: [],
    version: 'v1',
    overrides: [],
    ...overrides,
  }
}

const DRIVERS = [
  { id: 'd-jan', driverNumber: 'CH-001', fullName: 'Jan Peeters', employeeNumber: 'P-4711', categoryName: 'Kraan', availabilityStatus: 'Available', isActive: true, isBlocked: false },
  { id: 'd-els', driverNumber: 'CH-002', fullName: 'Els Janssens', employeeNumber: 'P-0815', categoryName: null, availabilityStatus: 'Available', isActive: true, isBlocked: false },
  { id: 'd-tom', driverNumber: 'CH-003', fullName: 'Tom Maes', employeeNumber: 'P-2000', categoryName: null, availabilityStatus: 'Available', isActive: true, isBlocked: false },
]
const VEHICLES = [
  { id: 'v-1', internalNumber: 'V-101', licensePlate: '1-ABC-123', brand: 'Volvo', model: 'FH16' },
  { id: 'v-2', internalNumber: 'V-202', licensePlate: '2-XYZ-987', brand: 'DAF', model: 'XF' },
  { id: 'v-3', internalNumber: 'V-303', licensePlate: '1-KLM-555', brand: 'Scania', model: 'R500' },
]
const TRAILERS = [
  { id: 't-1', internalNumber: 'OP-11', licensePlate: 'Q-AAA-111', brand: 'Krone', model: null },
  { id: 't-2', internalNumber: 'OP-22', licensePlate: 'Q-BBB-222', brand: 'Schmitz', model: null },
]
const ORDERS = [
  { id: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-09-20', customerId: 'c1', customerName: 'Nexans NV', customerReference: 'PO-778', status: 'Confirmed', goodsDescription: 'Kabelhaspels', firstLoadingCity: 'Gent', lastUnloadingCity: 'Luik', stopCount: 2, adrRequired: false, craneRequired: false, priority: 'Normal' },
  { id: 'o-2', orderNumber: 'ORD-0002', orderDate: '2026-09-20', customerId: 'c2', customerName: 'Bekaert', customerReference: null, status: 'Confirmed', goodsDescription: null, firstLoadingCity: 'Zwevegem', lastUnloadingCity: 'Antwerpen', stopCount: 2, adrRequired: false, craneRequired: false, priority: 'Normal' },
]

const ORDER_ON_TRIP = {
  transportOrderId: 'o-1', sequence: 1, orderNumber: 'ORD-0023', customerName: 'Nexans NV', orderStatus: 'Confirmed' as const,
  goodsDescription: 'Kabelhaspels', firstLoadingCity: 'Gent', lastUnloadingCity: 'Luik', adrRequired: false, craneRequired: false,
  dossierId: 'dos-22', dossierNumber: 'DOS-0022', customerReference: 'PO-778',
}

function pkg(n: number, status: string, exceptionState = 'None') {
  return {
    packageId: `p-${n}`, packageNumber: `COL-${String(n).padStart(3, '0')}`, description: 'Pallet', quantity: 1, unitType: 'Pallet', unitTypeLabel: null,
    status, exceptionState, isMandatory: true, isGroup: false, parentPackageId: null, barcodeValue: '', orderNumber: 'ORD-0023',
  }
}

async function renderPage() {
  const router = createMemoryRouter(
    [{ path: '/planning/:id', element: <TripDetailPage /> }, { path: '/dossiers/:id', element: <p>dossier</p> }],
    { initialEntries: ['/planning/trip-1'] },
  )
  render(<RouterProvider router={router} />)
  await screen.findByRole('heading', { name: /RIT-0001/ })
  await waitFor(() => expect(ordersApi.searchTransportOrders).toHaveBeenCalled())
  return router
}

describe('TripDetailPage — B23', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['planning.edit', 'packages.view'])
    driversApi.searchDrivers.mockResolvedValue({ items: DRIVERS, totalCount: 3, page: 1, pageSize: 200 })
    vehiclesApi.getVehicleOptions.mockResolvedValue(VEHICLES)
    trailersApi.getTrailerOptions.mockResolvedValue(TRAILERS)
    ordersApi.searchTransportOrders.mockResolvedValue({ items: ORDERS, totalCount: 2, page: 1, pageSize: 100 })
  })

  it('links every order and its dossier as identifier chips, and puts the trip in the dossier path', async () => {
    planningApi.getTrip.mockResolvedValue(trip({ orders: [ORDER_ON_TRIP] }))
    await renderPage()
    const orders = screen.getByRole('heading', { name: 'Opdrachten & stops (1)' }).closest('section')!
    expect(within(orders).getByRole('link', { name: 'ORD-0023' })).toHaveAttribute('href', '/transport-orders/o-1')
    expect(within(orders).getByRole('link', { name: 'DOS-0022' })).toHaveAttribute('href', '/dossiers/dos-22')
    const crumbs = screen.getByRole('navigation', { name: /kruimel|breadcrumb/i })
    expect(within(crumbs).getByRole('link', { name: 'DOS-0022' })).toHaveAttribute('href', '/dossiers/dos-22')
  })

  it('saving shows a lasting "Opgeslagen · hh:mm" next to the assignment, not only a toast', async () => {
    const user = userEvent.setup()
    planningApi.getTrip.mockResolvedValue(trip())
    planningApi.updateTrip.mockResolvedValue(trip({ driverId: 'd-els', driverName: 'Els Janssens', version: 'v2' }))
    await renderPage()
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('combobox', { name: 'Chauffeur' }))
    await user.click(await screen.findByRole('option', { name: /Els Janssens/ }))
    expect(screen.getAllByText('Niet-opgeslagen wijzigingen').length).toBeGreaterThan(0)
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText(/^✓ Opgeslagen · \d{2}:\d{2}$/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })

  it('readiness leads with the summary; critical packages show, the full list only on request', async () => {
    const user = userEvent.setup()
    planningApi.getTrip.mockResolvedValue(trip({ status: 'Planned' }))
    const outstanding = [pkg(1, 'Missing'), ...Array.from({ length: 15 }, (_, i) => pkg(i + 2, 'AwaitingLoading'))]
    packagesApi.getTripPackageReadiness.mockResolvedValue({
      tripId: 'trip-1', rule: 'Warn', totalPackages: 16, mandatoryPackages: 16, loadedCount: 0, notLoadedCount: 16,
      missingCount: 1, damagedCount: 0, openExceptionCount: 0, isComplete: false, requiresOverride: false, isBlocked: false,
      outstandingPackages: outstanding,
    })
    await renderPage()
    const section = (await screen.findByRole('heading', { name: 'Laadgereedheid' })).closest('section')!
    expect(section).toHaveTextContent('0 van 16 verplichte colli geladen')
    expect(within(section).getByText(/COL-001/)).toBeInTheDocument()
    expect(within(section).queryByText(/COL-016/)).not.toBeInTheDocument()

    await user.click(within(section).getByRole('button', { name: 'Toon 16 openstaande colli ▾' }))
    expect(within(section).getByText(/COL-016/)).toBeInTheDocument()
  })
})
