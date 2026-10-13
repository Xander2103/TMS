import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { TripDetailPage } from '../pages/TripDetailPage'
import type { TripDetail } from '../types'

/**
 * Sprint 2026-09-21 — driver, vehicle, trailer and "attach order" on the trip detail page are
 * searchable pickers instead of native selects, and a driver's fixed vehicle is proposed without
 * ever replacing a vehicle the planner picked by hand. Saving still sends plain ids.
 */
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
vi.mock('../../packages/api/packagesApi', () => ({ getTripPackageReadiness: vi.fn() }))
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

/** Fixed vehicle per driver, as `GET /api/drivers/{id}` exposes it. */
const FIXED_VEHICLE: Record<string, { id: string; label: string } | null> = {
  'd-jan': { id: 'v-1', label: 'V-101 (1-ABC-123)' },
  'd-els': { id: 'v-2', label: 'V-202 (2-XYZ-987)' },
  'd-tom': null,
}

const SUGGESTED_HINT = 'Voorgesteld op basis van vaste chauffeur-voertuigkoppeling'

async function renderPage() {
  const router = createMemoryRouter([{ path: '/planning/trips/:id', element: <TripDetailPage /> }], {
    initialEntries: ['/planning/trips/trip-1'],
  })
  const view = render(<RouterProvider router={router} />)
  await screen.findByRole('heading', { name: /RIT-0001/ })
  // Lists settle after the trip: wait until the pickers left their loading state.
  await waitFor(() => expect(vehiclesApi.getVehicleOptions).toHaveBeenCalled())
  await waitFor(() => expect(ordersApi.searchTransportOrders).toHaveBeenCalled())
  return view
}

const driverInput = () => screen.getByRole('combobox', { name: 'Chauffeur' }) as HTMLInputElement
const vehicleInput = () => screen.getByRole('combobox', { name: 'Voertuig' }) as HTMLInputElement
const trailerInput = () => screen.getByRole('combobox', { name: 'Oplegger' }) as HTMLInputElement
const orderInput = () => screen.getByRole('combobox', { name: 'Opdracht toevoegen' }) as HTMLInputElement

async function pick(user: ReturnType<typeof userEvent.setup>, input: HTMLInputElement, query: string, option: RegExp) {
  await user.click(input)
  await user.type(input, query)
  await user.click(await screen.findByRole('option', { name: option }))
}

describe('TripDetailPage — searchable pickers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    planningApi.getTrip.mockResolvedValue(trip())
    planningApi.updateTrip.mockImplementation(async (_id: string, input: Partial<TripDetail>) => trip({ ...input }))
    driversApi.searchDrivers.mockResolvedValue({ items: DRIVERS, totalCount: DRIVERS.length, page: 1, pageSize: 200 })
    driversApi.getDriver.mockImplementation(async (id: string) => ({ id, fixedVehicle: FIXED_VEHICLE[id] ?? null }))
    vehiclesApi.getVehicleOptions.mockResolvedValue(VEHICLES)
    trailersApi.getTrailerOptions.mockResolvedValue(TRAILERS)
    ordersApi.searchTransportOrders.mockResolvedValue({ items: ORDERS, totalCount: ORDERS.length, page: 1, pageSize: 100 })
  })

  it('renders comboboxes, not native selects, for the four entity pickers', async () => {
    await renderPage()
    for (const input of [driverInput(), vehicleInput(), trailerInput(), orderInput()]) {
      expect(input.tagName).toBe('INPUT')
    }
    expect(document.querySelector('.pl-assign select, .pl-add-order select')).toBeNull()
  })

  it('finds a driver by first name, last name (any order) and personnel number', async () => {
    const user = userEvent.setup()
    await renderPage()

    await user.click(driverInput())
    // Opening focuses the field: the planner types straight away.
    expect(driverInput()).toHaveFocus()
    expect(await screen.findAllByRole('option')).toHaveLength(3)

    await user.type(driverInput(), 'peeters jan')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: /Jan Peeters/ })).toHaveTextContent('Personeelsnr. P-4711')

    await user.clear(driverInput())
    await user.type(driverInput(), '0815')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: /Els Janssens/ })).toBeInTheDocument()

    await user.clear(driverInput())
    await user.type(driverInput(), 'zzz')
    expect(screen.getByText('Geen resultaten')).toBeInTheDocument()
  })

  it('selects with the keyboard, reverts on Escape and clears with the clear button', async () => {
    const user = userEvent.setup()
    await renderPage()

    await user.click(trailerInput())
    await user.type(trailerInput(), 'bbb')
    await user.keyboard('{Enter}')
    expect(trailerInput().value).toBe('OP-22 (Q-BBB-222)')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    // Escape closes the list and keeps the committed value.
    await user.click(trailerInput())
    await user.type(trailerInput(), 'op-11')
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(trailerInput().value).toBe('OP-22 (Q-BBB-222)')

    // Arrow keys: the first ArrowDown opens the list on row 1, ArrowUp wraps to the last row.
    await user.keyboard('{ArrowDown}{Enter}')
    expect(trailerInput().value).toBe('OP-11 (Q-AAA-111)')
    await user.keyboard('{ArrowDown}{ArrowUp}{Enter}')
    expect(trailerInput().value).toBe('OP-22 (Q-BBB-222)')

    const field = trailerInput().closest('.ui-searchable-select')!
    await user.click(field.querySelector<HTMLButtonElement>('.ui-searchable-select-clear')!)
    expect(trailerInput().value).toBe('')

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(planningApi.updateTrip).toHaveBeenCalled())
    expect(planningApi.updateTrip.mock.calls[0][1]).toMatchObject({ trailerId: null })
  })

  it('finds a vehicle by number, licence plate without separators and brand/model', async () => {
    const user = userEvent.setup()
    await renderPage()

    await user.click(vehicleInput())
    await user.type(vehicleInput(), '2xyz987')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: /V-202/ })).toHaveTextContent('DAF XF')

    await user.clear(vehicleInput())
    await user.type(vehicleInput(), 'scania')
    await user.click(screen.getByRole('option', { name: /V-303/ }))
    expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(planningApi.updateTrip).toHaveBeenCalled())
    const [tripId, payload] = planningApi.updateTrip.mock.calls[0]
    expect(tripId).toBe('trip-1')
    // Ids only, plus how the vehicle was chosen (the API persists it: a hand pick is 'Manual').
    expect(payload).toEqual({
      tripDate: '2026-09-22',
      driverId: null,
      vehicleId: 'v-3',
      trailerId: null,
      plannedStart: null,
      plannedEnd: null,
      notes: null,
      orderIds: [],
      plannedDistanceKm: null,
      plannedEmptyKm: null,
      vehicleSelectionSource: 'Manual',
    })
  })

  it('finds a trailer by number and licence plate', async () => {
    const user = userEvent.setup()
    await renderPage()
    await pick(user, trailerInput(), 'op-11', /OP-11/)
    expect(trailerInput().value).toBe('OP-11 (Q-AAA-111)')
    await user.click(trailerInput())
    await user.type(trailerInput(), 'qbbb222')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option', { name: /OP-22/ })).toBeInTheDocument()
  })

  it('attaches an order found by customer or reference and removes it from the adder', async () => {
    const user = userEvent.setup()
    await renderPage()

    await pick(user, orderInput(), 'po-778', /ORD-0001/)
    expect(screen.getByText(/ORD-0001 — Nexans NV \(Gent → Luik\)/)).toBeInTheDocument()
    expect(orderInput().value).toBe('')

    await user.click(orderInput())
    expect(screen.getAllByRole('option')).toHaveLength(1)
    await user.type(orderInput(), 'bekaert')
    await user.keyboard('{Enter}')

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(planningApi.updateTrip).toHaveBeenCalled())
    expect(planningApi.updateTrip.mock.calls[0][1]).toMatchObject({ orderIds: ['o-1', 'o-2'] })
  })

  it('shows a loading note while a list loads and the error when it fails', async () => {
    const user = userEvent.setup()
    let resolveVehicles: (value: typeof VEHICLES) => void = () => {}
    vehiclesApi.getVehicleOptions.mockReturnValue(new Promise((resolve) => (resolveVehicles = resolve)))
    trailersApi.getTrailerOptions.mockRejectedValue(new Error('boom'))
    await renderPage()

    await user.click(vehicleInput())
    expect(screen.getByText('Laden…')).toBeInTheDocument()
    resolveVehicles(VEHICLES)
    expect(await screen.findByRole('option', { name: /V-101/ })).toBeInTheDocument()

    await user.click(trailerInput())
    expect(await screen.findByText('De opleggers konden niet worden geladen.')).toBeInTheDocument()
    expect(screen.queryByText('Geen resultaten')).not.toBeInTheDocument()
  })

  describe('fixed-vehicle suggestion', () => {
    it('proposes the fixed vehicle of the chosen driver and says so', async () => {
      const user = userEvent.setup()
      await renderPage()

      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-101 (1-ABC-123)'))
      expect(screen.getByText(SUGGESTED_HINT)).toBeInTheDocument()
      expect(driversApi.getDriver).toHaveBeenCalledWith('d-jan')
    })

    it('lets a suggestion follow the driver: replaced, or dropped when the next driver has none', async () => {
      const user = userEvent.setup()
      await renderPage()

      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-101 (1-ABC-123)'))

      await pick(user, driverInput(), 'els', /Els Janssens/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-202 (2-XYZ-987)'))
      expect(screen.getByText(SUGGESTED_HINT)).toBeInTheDocument()

      await pick(user, driverInput(), 'tom', /Tom Maes/)
      await waitFor(() => expect(vehicleInput().value).toBe(''))
      expect(screen.queryByText(SUGGESTED_HINT)).not.toBeInTheDocument()
    })

    it('never replaces a vehicle the planner picked by hand', async () => {
      const user = userEvent.setup()
      await renderPage()

      await pick(user, vehicleInput(), 'v-303', /V-303/)
      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-jan'))
      await pick(user, driverInput(), 'els', /Els Janssens/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-els'))

      expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')
      expect(screen.queryByText(SUGGESTED_HINT)).not.toBeInTheDocument()
    })

    it('turns a suggestion into a manual choice once the planner overrides it', async () => {
      const user = userEvent.setup()
      await renderPage()

      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-101 (1-ABC-123)'))

      await pick(user, vehicleInput(), 'scania', /V-303/)
      expect(screen.queryByText(SUGGESTED_HINT)).not.toBeInTheDocument()

      await pick(user, driverInput(), 'els', /Els Janssens/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-els'))
      expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')
    })

    it('treats a stored vehicle without a source as manual, and a stored suggestion as replaceable', async () => {
      const user = userEvent.setup()
      planningApi.getTrip.mockResolvedValue(
        trip({ driverId: 'd-tom', driverName: 'Tom Maes', vehicleId: 'v-3', vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555' }),
      )
      await renderPage()
      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-jan'))
      expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')
    })

    it('reads vehicleSelectionSource from the trip once the API sends it', async () => {
      const user = userEvent.setup()
      planningApi.getTrip.mockResolvedValue(
        trip({ vehicleId: 'v-3', vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555', vehicleSelectionSource: 'Suggested' }),
      )
      await renderPage()
      expect(screen.getByText(SUGGESTED_HINT)).toBeInTheDocument()
      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-101 (1-ABC-123)'))
    })

    it('uses fixedVehicleId from the driver list and skips the driver-detail call', async () => {
      const user = userEvent.setup()
      driversApi.searchDrivers.mockResolvedValue({
        items: DRIVERS.map((driver) => ({
          ...driver,
          fixedVehicleId: FIXED_VEHICLE[driver.id]?.id ?? null,
          fixedVehicleNumber: null,
          fixedVehiclePlate: null,
        })),
        totalCount: DRIVERS.length, page: 1, pageSize: 200,
      })
      await renderPage()

      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-101 (1-ABC-123)'))
      expect(screen.getByText(SUGGESTED_HINT)).toBeInTheDocument()
      // A driver without a fixed vehicle (null in the list) drops the suggestion — still no lookup.
      await pick(user, driverInput(), 'tom', /Tom Maes/)
      await waitFor(() => expect(vehicleInput().value).toBe(''))
      expect(driversApi.getDriver).not.toHaveBeenCalled()

      await pick(user, driverInput(), 'els', /Els Janssens/)
      await waitFor(() => expect(vehicleInput().value).toBe('V-202 (2-XYZ-987)'))
      await user.click(screen.getByRole('button', { name: 'Opslaan' }))
      await waitFor(() => expect(planningApi.updateTrip).toHaveBeenCalled())
      expect(planningApi.updateTrip.mock.calls[0][1]).toMatchObject({ driverId: 'd-els', vehicleId: 'v-2', vehicleSelectionSource: 'Suggested' })
    })

    it('persists a manual choice: it survives a driver change, the save and a reload', async () => {
      const user = userEvent.setup()
      // The API echoes what it stored, vehicle labels included.
      planningApi.updateTrip.mockImplementation(async (_id: string, input: Partial<TripDetail>) =>
        trip({ ...input, vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555', driverName: 'Jan Peeters' }),
      )
      const first = await renderPage()

      await pick(user, vehicleInput(), 'v-303', /V-303/)
      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-jan'))
      expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')

      await user.click(screen.getByRole('button', { name: 'Opslaan' }))
      await waitFor(() => expect(planningApi.updateTrip).toHaveBeenCalled())
      const saved = planningApi.updateTrip.mock.calls[0][1]
      expect(saved).toMatchObject({ driverId: 'd-jan', vehicleId: 'v-3', vehicleSelectionSource: 'Manual' })
      first.unmount()

      // Reload: the stored source comes back with the trip, so the next driver still cannot replace it.
      planningApi.getTrip.mockResolvedValue(
        trip({ ...saved, driverName: 'Jan Peeters', vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555' }),
      )
      await renderPage()
      expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')
      await pick(user, driverInput(), 'els', /Els Janssens/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-els'))
      expect(vehicleInput().value).toBe('V-303 (1-KLM-555)')
      expect(screen.queryByText(SUGGESTED_HINT)).not.toBeInTheDocument()
    })

    it('ignores a fixed vehicle that is not selectable and survives a failed lookup', async () => {
      const user = userEvent.setup()
      driversApi.getDriver.mockImplementation(async (id: string) => {
        if (id === 'd-els') throw new Error('403')
        return { id, fixedVehicle: { id: 'v-retired', label: 'V-999' } }
      })
      await renderPage()
      await pick(user, driverInput(), 'jan p', /Jan Peeters/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-jan'))
      await pick(user, driverInput(), 'els', /Els Janssens/)
      await waitFor(() => expect(driversApi.getDriver).toHaveBeenCalledWith('d-els'))
      expect(vehicleInput().value).toBe('')
      expect(screen.queryByText(SUGGESTED_HINT)).not.toBeInTheDocument()
    })
  })
})
