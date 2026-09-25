import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import { clearVehicleCapacityCache } from '../useVehicleCapacity'
import type { TripDetail } from '../../planning/types'
import type { DossierActivity, DossierActivityAssignment, DossierDetail, ReadinessIssue } from '../types'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'

/**
 * Master sprint 2026-09-21 — planning an activity from the dossier (D1), the Overzicht blocks
 * (spec §17), attention jumps that select the activity, real vehicle capacities (D4) and the note
 * count that follows the notes panel. Driver/vehicle/trailer live ONLY on the trip.
 */

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.has(code)),
  }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  getDossier: vi.fn(),
  updateDossier: vi.fn(),
  closeDossier: vi.fn(),
  reopenDossier: vi.fn(),
  linkDossierOrder: vi.fn(),
  unlinkDossierOrder: vi.fn(),
  addDossierRelation: vi.fn(),
  removeDossierRelation: vi.fn(),
  listDossiers: vi.fn(() => Promise.resolve([])),
  addDossierActivity: vi.fn(),
  updateDossierActivity: vi.fn(),
  deleteDossierActivity: vi.fn(),
  createOrderForActivity: vi.fn(),
  changeDossierLegalEntity: vi.fn(),
  setActivityPrice: vi.fn(),
  planDossierActivity: vi.fn(),
}))
vi.mock('../api/dossiersApi', () => api)
vi.mock('../api/activityTypesApi', () => ({ listActivityTypes: () => Promise.resolve([]) }))

const orders = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: orders.get,
  updateTransportOrder: vi.fn(),
  saveOrderPriceLines: vi.fn(),
  setOrderOneOffPrice: vi.fn(),
  searchTransportOrders: () => Promise.resolve({ items: [], totalCount: 0 }),
}))

const planningApi = vi.hoisted(() => ({ getTrip: vi.fn() }))
vi.mock('../../planning/api/planningApi', () => planningApi)
const tripCommands = vi.hoisted(() => ({ assignDriver: vi.fn(), assignVehicle: vi.fn(), assignTrailer: vi.fn() }))
vi.mock('../../planning-center/api/planningCenterApi', () => tripCommands)

const driversApi = vi.hoisted(() => ({ searchDrivers: vi.fn(), getDriver: vi.fn() }))
vi.mock('../../drivers/api/driversApi', () => driversApi)
const vehiclesApi = vi.hoisted(() => ({ getVehicleOptions: vi.fn(), getVehicle: vi.fn() }))
vi.mock('../../vehicles/api/vehiclesApi', () => vehiclesApi)
const trailersApi = vi.hoisted(() => ({ getTrailerOptions: vi.fn() }))
vi.mock('../../trailers/api/trailersApi', () => trailersApi)

// The notes panel has its own suite; here only its "something changed" signal matters.
vi.mock('../notes/DossierNotesPanel', () => ({
  DossierNotesPanel: ({ onChanged, canWrite }: { onChanged?: () => void; canWrite: boolean }) => (
    <div data-testid="notes-panel" data-can-write={String(canWrite)}>
      <button type="button" onClick={() => onChanged?.()}>
        notitie-opgeslagen
      </button>
    </div>
  ),
}))
vi.mock('../../auditing/components/AuditHistoryPanel', () => ({ AuditHistoryPanel: () => null }))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [], totalCount: 0 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../users/api/usersApi', () => ({ getUsers: () => Promise.resolve([]) }))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../warehousing/api/warehousingApi', () => ({ listWarehouses: () => Promise.resolve([]) }))
vi.mock('../../locations/api/locationsApi', () => ({
  getLocation: () => Promise.resolve({ openingIntervals: [] }),
  getLocationOptions: () => Promise.resolve([]),
  createLocation: vi.fn(),
}))
vi.mock('../../tarification/api/pricingApi', async () => {
  const actual = await vi.importActual<typeof import('../../tarification/api/pricingApi')>('../../tarification/api/pricingApi')
  return {
    ...actual,
    listServiceOptions: () => Promise.resolve([]),
    getCustomerPricingConfig: () => Promise.resolve({ preferredUnits: [], serviceOptions: [] }),
    listUnitTypeMaster: () => Promise.resolve([]),
    previewPrice: () => Promise.resolve({ lines: [], total: 0, currency: 'EUR', configurationError: null }),
  }
})

function renderPage(initialPath: string) {
  const router = createMemoryRouter(
    [
      { path: '/dossiers/:id/:section?', element: <DossierDetailPage /> },
      { path: '/planning/:id', element: <p>ritpagina</p> },
    ],
    { initialEntries: [initialPath] },
  )
  return { ...render(<RouterProvider router={router} />), router }
}

const ACTIVITIES_TAB = '/dossiers/d-1/activiteiten'

const DRIVERS = [
  { id: 'd-jan', driverNumber: 'CH-001', fullName: 'Jan Peeters', employeeNumber: 'P-4711', categoryName: null, availabilityStatus: 'Available', isActive: true, isBlocked: false, fixedVehicleId: 'v-1', fixedVehicleNumber: 'V-101', fixedVehiclePlate: '1-ABC-123' },
  { id: 'd-els', driverNumber: 'CH-002', fullName: 'Els Janssens', employeeNumber: 'P-0815', categoryName: null, availabilityStatus: 'Available', isActive: true, isBlocked: false, fixedVehicleId: 'v-2', fixedVehicleNumber: 'V-202', fixedVehiclePlate: '2-XYZ-987' },
]
const VEHICLES = [
  { id: 'v-1', internalNumber: 'V-101', licensePlate: '1-ABC-123', brand: 'Volvo', model: 'FH16' },
  { id: 'v-2', internalNumber: 'V-202', licensePlate: '2-XYZ-987', brand: 'DAF', model: 'XF' },
  { id: 'v-3', internalNumber: 'V-303', licensePlate: '1-KLM-555', brand: 'Scania', model: 'R500' },
]
const TRAILERS = [{ id: 't-1', internalNumber: 'OP-11', licensePlate: 'Q-AAA-111', brand: 'Krone', model: null }]

function assignment(overrides: Partial<DossierActivityAssignment> = {}): DossierActivityAssignment {
  return {
    tripId: 'trip-1', tripNumber: 'RIT-0007', tripDate: '2026-09-22', tripStatus: 'Draft',
    driverId: 'd-jan', driverName: 'Jan Peeters',
    vehicleId: 'v-1', vehicleNumber: 'V-101', vehiclePlate: '1-ABC-123', vehicleSelectionSource: 'Suggested',
    trailerId: null, trailerNumber: null, trailerPlate: null,
    tripCount: 1, otherOrderCount: 0,
    ...overrides,
  }
}

function trip(overrides: Partial<TripDetail> = {}): TripDetail {
  return {
    id: 'trip-1', tripNumber: 'RIT-0007', tripDate: '2026-09-22', status: 'Draft',
    driverId: 'd-jan', driverName: 'Jan Peeters',
    vehicleId: 'v-1', vehicleNumber: 'V-101', vehicleLicensePlate: '1-ABC-123', vehicleSelectionSource: 'Suggested',
    trailerId: null, trailerNumber: null,
    plannedStart: null, plannedEnd: null, plannedDistanceKm: null, plannedEmptyKm: null,
    actualDistanceKm: null, actualEmptyKm: null, notes: null,
    orders: [], conflicts: [], allowedTransitions: [], version: 'tv-1', overrides: [],
    ...overrides,
  }
}

const orderActivity = (overrides: Partial<DossierActivity> = {}) =>
  dossierActivity({ id: 'a-6', linkedTransportOrderId: 'o-6', linkedOrderNumber: '0006', linkedOrderStatus: 'Confirmed', ...overrides })

function dossierWith(activities: DossierActivity[], overrides: Partial<DossierDetail> = {}): DossierDetail {
  return dossierDetail({
    activities,
    orders: activities
      .filter((a) => a.linkedTransportOrderId)
      .map((a) => ({ linkId: `l-${a.id}`, orderId: a.linkedTransportOrderId!, orderNumber: a.linkedOrderNumber!, orderDate: '2026-09-20', status: 'Confirmed', goodsDescription: null, agreedPrice: a.agreedPrice })),
    ...overrides,
  })
}

const card = (number: string) => within(screen.getByRole('listitem', { name: number }))
const cell = (scope: ReturnType<typeof within>, label: string) => scope.getByText(label).parentElement!.querySelector('dd')!
const combobox = (name: string) => screen.getByRole('combobox', { name }) as HTMLInputElement

async function pick(user: ReturnType<typeof userEvent.setup>, input: HTMLInputElement, query: string, option: RegExp) {
  await user.click(input)
  await user.type(input, query)
  await user.click(await screen.findByRole('option', { name: option }))
}

/** Opens the activity drawer on its Planning block through the card action. */
async function openPlanning(user: ReturnType<typeof userEvent.setup>, number: string) {
  await screen.findByRole('listitem', { name: number })
  await user.click(card(number).getByRole('button', { name: 'Planning' }))
  return within(await screen.findByRole('region', { name: 'Planning' }))
}

describe('Dossier activity planning', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearVehicleCapacityCache()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'planning.view', 'planning.create', 'planning.edit'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    orders.get.mockResolvedValue(orderDetail({ id: 'o-6', orderNumber: '0006' }))
    driversApi.searchDrivers.mockResolvedValue({ items: DRIVERS, totalCount: DRIVERS.length, page: 1, pageSize: 200 })
    vehiclesApi.getVehicleOptions.mockResolvedValue(VEHICLES)
    vehiclesApi.getVehicle.mockRejectedValue(new Error('not used'))
    trailersApi.getTrailerOptions.mockResolvedValue(TRAILERS)
    planningApi.getTrip.mockResolvedValue(trip())
  })

  it('plans an unplanned activity through the plan endpoint and updates the card without a reload', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ plannedDate: '2026-09-22' })]))
    api.planDossierActivity.mockResolvedValue(dossierWith([orderActivity({ plannedDate: '2026-09-22', assignment: assignment() })], { version: 'v-2' }))
    renderPage(ACTIVITIES_TAB)

    await screen.findByRole('listitem', { name: '0006' })
    expect(cell(card('0006'), 'Chauffeur')).toHaveTextContent('Niet toegewezen')
    const planning = await openPlanning(user, '0006')
    expect(planning.getByText(/Nog niet op een rit/)).toBeInTheDocument()

    // Picking a driver proposes the fixed vehicle from the driver list — and says it is a proposal.
    await pick(user, combobox('Chauffeur'), 'peeters', /Jan Peeters/)
    await waitFor(() => expect(combobox('Voertuig').value).toBe('V-101 (1-ABC-123)'))
    expect(planning.getByText('Voorgesteld op basis van de vaste chauffeur-voertuigkoppeling')).toBeInTheDocument()
    expect(driversApi.getDriver).not.toHaveBeenCalled()

    await user.click(planning.getByRole('button', { name: 'Rit aanmaken en toewijzen' }))
    await waitFor(() => expect(api.planDossierActivity).toHaveBeenCalledTimes(1))
    expect(api.planDossierActivity).toHaveBeenCalledWith('d-1', 'a-6', {
      tripDate: '2026-09-22',
      driverId: 'd-jan',
      vehicleId: 'v-1',
      trailerId: null,
      vehicleSelectionSource: 'Suggested',
      version: 'v-1',
    })

    // The returned dossier is applied: card and drawer follow, no second getDossier.
    await waitFor(() => expect(cell(card('0006'), 'Chauffeur')).toHaveTextContent('Jan Peeters'))
    expect(cell(card('0006'), 'Kenteken')).toHaveTextContent('1-ABC-123')
    expect(cell(card('0006'), 'Kenteken')).toHaveTextContent('voorgesteld')
    expect(api.getDossier).toHaveBeenCalledTimes(1)
    expect(await screen.findByRole('link', { name: 'Open rit' })).toHaveAttribute('href', '/planning/trip-1')
  })

  it('may plan with an empty assignment and shows a refusal of the server as is', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ linkedOrderStatus: 'Draft' })]))
    api.planDossierActivity.mockRejectedValue(
      new ApiError('Opdracht 0006 is nog een concept en kan niet worden ingepland. Bevestig de opdracht eerst.', 400, null),
    )
    renderPage(ACTIVITIES_TAB)
    const planning = await openPlanning(user, '0006')

    await user.click(planning.getByRole('button', { name: 'Rit aanmaken en toewijzen' }))
    await waitFor(() => expect(api.planDossierActivity).toHaveBeenCalled())
    expect(api.planDossierActivity.mock.calls[0][2]).toMatchObject({ driverId: null, vehicleId: null, trailerId: null, vehicleSelectionSource: null })
    expect(await planning.findByRole('alert')).toHaveTextContent('Opdracht 0006 is nog een concept en kan niet worden ingepland. Bevestig de opdracht eerst.')
  })

  it('never replaces a hand-picked vehicle on a driver change — also after a reload (the source is stored)', async () => {
    const user = userEvent.setup()
    const manual = assignment({ vehicleId: 'v-3', vehicleNumber: 'V-303', vehiclePlate: '1-KLM-555', vehicleSelectionSource: 'Manual' })
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: manual })]))
    planningApi.getTrip.mockResolvedValue(trip({ vehicleId: 'v-3', vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555', vehicleSelectionSource: 'Manual' }))
    tripCommands.assignDriver.mockResolvedValue(
      trip({ driverId: 'd-els', driverName: 'Els Janssens', vehicleId: 'v-3', vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555', vehicleSelectionSource: 'Manual', version: 'tv-2' }),
    )
    renderPage(ACTIVITIES_TAB)
    const planning = await openPlanning(user, '0006')
    await waitFor(() => expect(combobox('Voertuig').value).toBe('V-303 (1-KLM-555)'))
    expect(cell(card('0006'), 'Kenteken')).not.toHaveTextContent('voorgesteld')

    await pick(user, combobox('Chauffeur'), 'els', /Els Janssens/)
    expect(combobox('Voertuig').value).toBe('V-303 (1-KLM-555)')
    expect(planning.queryByText(/Voorgesteld op basis van/)).not.toBeInTheDocument()

    // Saved through the EXISTING trip endpoint with the trip's own version; the vehicle is untouched.
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: { ...manual, driverId: 'd-els', driverName: 'Els Janssens' } })]))
    await user.click(planning.getByRole('button', { name: 'Toewijzing opslaan' }))
    await waitFor(() => expect(tripCommands.assignDriver).toHaveBeenCalledWith('trip-1', 'd-els', 'tv-1', false, null))
    expect(tripCommands.assignVehicle).not.toHaveBeenCalled()
    await waitFor(() => expect(cell(card('0006'), 'Chauffeur')).toHaveTextContent('Els Janssens'))
    expect(cell(card('0006'), 'Kenteken')).toHaveTextContent('1-KLM-555')
  })

  it('lets a suggested vehicle follow the driver and sends the source with the vehicle', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: assignment() })]))
    tripCommands.assignDriver.mockResolvedValue(
      trip({ driverId: 'd-els', driverName: 'Els Janssens', vehicleId: 'v-2', vehicleNumber: 'V-202', vehicleLicensePlate: '2-XYZ-987', vehicleSelectionSource: 'Suggested', version: 'tv-2' }),
    )
    tripCommands.assignVehicle.mockResolvedValue(
      trip({ driverId: 'd-els', driverName: 'Els Janssens', vehicleId: 'v-3', vehicleNumber: 'V-303', vehicleLicensePlate: '1-KLM-555', vehicleSelectionSource: 'Manual', version: 'tv-3' }),
    )
    renderPage(ACTIVITIES_TAB)
    const planning = await openPlanning(user, '0006')
    await waitFor(() => expect(combobox('Voertuig').value).toBe('V-101 (1-ABC-123)'))
    expect(planning.getByText('Voorgesteld op basis van de vaste chauffeur-voertuigkoppeling')).toBeInTheDocument()

    await pick(user, combobox('Chauffeur'), 'els', /Els Janssens/)
    await waitFor(() => expect(combobox('Voertuig').value).toBe('V-202 (2-XYZ-987)'))
    // The planner overrides the proposal: from now on it is a manual choice.
    await pick(user, combobox('Voertuig'), 'scania', /V-303/)
    expect(planning.queryByText(/Voorgesteld op basis van/)).not.toBeInTheDocument()

    await user.click(planning.getByRole('button', { name: 'Toewijzing opslaan' }))
    await waitFor(() => expect(tripCommands.assignVehicle).toHaveBeenCalled())
    expect(tripCommands.assignDriver).toHaveBeenCalledWith('trip-1', 'd-els', 'tv-1', false, null)
    // Second call uses the version the first one returned.
    expect(tripCommands.assignVehicle).toHaveBeenCalledWith('trip-1', 'v-3', 'tv-2', false, null, 'Manual')
  })

  it('warns that a change applies to the whole shared trip BEFORE saving', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: assignment({ otherOrderCount: 2 }) })]))
    tripCommands.assignDriver.mockResolvedValue(trip({ driverId: 'd-els', driverName: 'Els Janssens', version: 'tv-2' }))
    renderPage(ACTIVITIES_TAB)
    const planning = await openPlanning(user, '0006')
    const warning = 'Deze wijziging geldt voor de hele rit RIT-0007 (2 andere opdrachten).'
    expect(await planning.findByText(new RegExp(warning.replace(/[()]/g, '\\$&')))).toBeInTheDocument()

    await pick(user, combobox('Chauffeur'), 'els', /Els Janssens/)
    await user.click(planning.getByRole('button', { name: 'Toewijzing opslaan' }))
    const dialog = within(await screen.findByRole('dialog', { name: 'Wijziging voor de hele rit' }))
    expect(dialog.getByText(warning)).toBeInTheDocument()
    expect(tripCommands.assignDriver).not.toHaveBeenCalled()

    await user.click(dialog.getByRole('button', { name: 'Toch opslaan' }))
    await waitFor(() => expect(tripCommands.assignDriver).toHaveBeenCalledTimes(1))
  })

  it('reuses the conflict dialog: a blocking conflict can be overridden with a reason', async () => {
    const user = userEvent.setup()
    auth.permissions.add('planning.override')
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: assignment({ tripStatus: 'Planned' }) })]))
    planningApi.getTrip.mockResolvedValue(
      trip({
        status: 'Planned',
        conflicts: [{ code: 'CapacityCheckIncomplete', blocking: false, description: 'Capaciteit kon niet volledig worden gecontroleerd.', severity: 'Warning', category: 'Capacity', relatedEntityType: null, relatedEntityId: null, overrideAllowed: false, requiredPermission: null, suggestedAction: null }],
      }),
    )
    const conflict = { code: 'DriverDoubleBooked', blocking: true, description: 'Els Janssens rijdt al rit RIT-0003.', severity: 'Blocking', category: 'Resource', relatedEntityType: null, relatedEntityId: null, overrideAllowed: true, requiredPermission: 'planning.override', suggestedAction: null }
    tripCommands.assignDriver
      .mockRejectedValueOnce(new ApiError('Conflicten', 409, { message: 'De toewijzing is geweigerd.', conflicts: [conflict] }))
      .mockResolvedValueOnce(trip({ status: 'Planned', driverId: 'd-els', driverName: 'Els Janssens', version: 'tv-2' }))
    renderPage(ACTIVITIES_TAB)
    const planning = await openPlanning(user, '0006')
    // Warnings of the trip are shown as warnings, not as errors.
    expect(await planning.findByText(/Capaciteit kon niet volledig worden gecontroleerd/)).toBeInTheDocument()
    expect(planning.queryByRole('alert')).not.toBeInTheDocument()

    await pick(user, combobox('Chauffeur'), 'els', /Els Janssens/)
    await user.click(planning.getByRole('button', { name: 'Toewijzing opslaan' }))
    const dialog = within(await screen.findByRole('dialog', { name: 'Planningsconflicten' }))
    expect(dialog.getByText('Els Janssens rijdt al rit RIT-0003.')).toBeInTheDocument()
    await user.type(dialog.getByRole('textbox'), 'Klant vraagt deze chauffeur')
    await user.click(dialog.getByRole('button', { name: 'Overschrijven en doorgaan' }))
    await waitFor(() => expect(tripCommands.assignDriver).toHaveBeenLastCalledWith('trip-1', 'd-els', 'tv-1', true, 'Klant vraagt deze chauffeur'))
  })

  it('is read-only without a planning permission', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: assignment() })]))
    renderPage(ACTIVITIES_TAB)
    const planning = await openPlanning(user, '0006')

    expect(planning.getByText('Jan Peeters')).toBeInTheDocument()
    expect(planning.getByText('V-101 (1-ABC-123)')).toBeInTheDocument()
    expect(planning.getByText('Je hebt geen recht om de planning te wijzigen.')).toBeInTheDocument()
    expect(planning.queryByRole('combobox')).not.toBeInTheDocument()
    expect(planning.queryByRole('button', { name: 'Toewijzing opslaan' })).not.toBeInTheDocument()
    // Nothing is loaded for a view the user cannot act on.
    expect(planningApi.getTrip).not.toHaveBeenCalled()
    expect(driversApi.searchDrivers).not.toHaveBeenCalled()
  })

  it('refreshes the note count on the card after the notes panel reports a change', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValueOnce(dossierWith([orderActivity({ noteCount: 1, latestNotePreview: 'Eerste notitie' })]))
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ noteCount: 2, latestNotePreview: 'Poort 3 gebruiken' })]))
    renderPage(ACTIVITIES_TAB)

    await user.click(await screen.findByRole('button', { name: /Eerste notitie/ }))
    await user.click(within(await screen.findByTestId('notes-panel')).getByRole('button', { name: 'notitie-opgeslagen' }))

    await waitFor(() => expect(card('0006').getByText('2 notities')).toBeInTheDocument())
    expect(card('0006').getByText('Poort 3 gebruiken')).toBeInTheDocument()
    expect(api.getDossier).toHaveBeenCalledTimes(2)
  })

  it('offers "+ Notitie toevoegen" in Historiek only for an open dossier the user may manage', async () => {
    api.getDossier.mockResolvedValue(dossierWith([orderActivity()], { status: 'Closed', closedAt: '2026-09-20T10:00:00Z' }))
    const closed = renderPage('/dossiers/d-1/historiek')
    expect(await screen.findByTestId('notes-panel')).toHaveAttribute('data-can-write', 'false')
    closed.unmount()

    api.getDossier.mockResolvedValue(dossierWith([orderActivity()]))
    renderPage('/dossiers/d-1/historiek')
    expect(await screen.findByTestId('notes-panel')).toHaveAttribute('data-can-write', 'true')
  })
})

describe('Dossier attention jumps', () => {
  const issue = (overrides: Partial<ReadinessIssue>): ReadinessIssue => ({
    code: 'pricing.missing', severity: 'Warning', message: '0006: verkoopprijs ontbreekt.', section: 'prijs', field: 'price', stage: 'Commercial', ...overrides,
  })

  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    orders.get.mockImplementation(async (id: string) => orderDetail({ id, orderNumber: id === 'o-6' ? '0006' : '0005', agreedPrice: 0 }))
  })

  it('clicking "0006: verkoopprijs ontbreekt." opens the price tab with activity 0006 selected', async () => {
    const user = userEvent.setup()
    const activities = [
      orderActivity({ id: 'a-5', sequence: 1, linkedTransportOrderId: 'o-5', linkedOrderNumber: '0005', agreedPrice: 300, isPriced: true, priceStatus: 'Priced' }),
      orderActivity({ id: 'a-6', sequence: 2, priceStatus: 'NotPriced' }),
    ]
    api.getDossier.mockResolvedValue(
      dossierWith(activities, {
        readiness: [
          issue({ transportOrderId: 'o-6' }),
          // Only the id travels: the strip names the order itself.
          issue({ code: 'route.date_missing', section: 'route', field: 'stops.plannedFrom', message: 'planningsdatum ontbreekt.', transportOrderId: 'o-6' }),
        ],
      }),
    )
    const { router } = renderPage('/dossiers/d-1')

    expect(await screen.findByRole('button', { name: '0006: planningsdatum ontbreekt.' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '0006: verkoopprijs ontbreekt.' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/dossiers/d-1/prijs'))
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    // The target order of the editors is the one the warning is about — not the default first one.
    await waitFor(() => expect(orders.get).toHaveBeenCalledWith('o-6'))
  })

  it('an issue about one activity on the Activiteiten tab outlines that card', async () => {
    const user = userEvent.setup()
    const activities = [
      orderActivity({ id: 'a-5', sequence: 1, linkedTransportOrderId: 'o-5', linkedOrderNumber: '0005' }),
      orderActivity({ id: 'a-6', sequence: 2 }),
    ]
    api.getDossier.mockResolvedValue(
      dossierWith(activities, { readiness: [issue({ code: 'activity.check', section: 'activiteiten', field: null, message: '0006: controleer de activiteit.', activityId: 'a-6' })] }),
    )
    const { router } = renderPage('/dossiers/d-1')

    await user.click(await screen.findByRole('button', { name: '0006: controleer de activiteit.' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/dossiers/d-1/activiteiten'))
    expect(await screen.findByRole('listitem', { name: '0006' })).toHaveClass('is-highlighted')
    expect(screen.getByRole('listitem', { name: '0005' })).not.toHaveClass('is-highlighted')
  })
})

describe('Dossier overview (spec §17)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearVehicleCapacityCache()
    auth.permissions = new Set(['dossiers.view'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  const overviewCard = (title: string) => within(screen.getByRole('article', { name: title }))

  it('lists every activity with number, driver, plate and price or price status', async () => {
    const activities = [
      orderActivity({ id: 'a-5', sequence: 1, linkedTransportOrderId: 'o-5', linkedOrderNumber: '0005', agreedPrice: 450, isPriced: true, priceStatus: 'Priced', assignment: assignment() }),
      orderActivity({ id: 'a-6', sequence: 2, agreedPrice: 0, isPriced: false, priceStatus: 'NotPriced' }),
      dossierActivity({ id: 'a-7', sequence: 3, hasStops: false, supportsGoods: false, activityTypeName: 'Opslag', agreedPrice: 0, isPriced: true, priceStatus: 'Free', freeConfirmed: true }),
    ]
    orders.get.mockResolvedValue(orderDetail({ id: 'o-5', orderNumber: '0005' }))
    api.getDossier.mockResolvedValue(
      dossierWith(activities, {
        noteCount: 3,
        notes: 'Legacy vrije tekst',
        documentCount: 4,
        documentTypes: ['Cmr'],
        financials: { agreedOrderTotal: 450, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0, billableActivityCount: 3, pricedActivityCount: 2, zeroPricedActivityCount: 0 },
      }),
    )
    renderPage('/dossiers/d-1')
    await screen.findByRole('article', { name: 'Activiteiten' })

    const list = overviewCard('Activiteiten')
    const rows = (await list.findAllByRole('listitem')).map((row) => row.textContent ?? '')
    expect(rows[0]).toMatch(/Direct transport.*0005.*Jan Peeters.*1-ABC-123.*€\s450,00/)
    expect(rows[1]).toMatch(/0006.*Niet toegewezen.*—.*Nog niet geprijsd/)
    expect(rows[1]).not.toMatch(/€/)
    expect(rows[2]).toMatch(/Opslag.*Gratis/)

    // Sales: the dossier total, "x van y" from the DTO counters and the units still without a price.
    const price = overviewCard('Verkoop & prijs')
    expect(price.getByText(/€\s450,00/)).toBeInTheDocument()
    expect(price.getByText('2 van 3 activiteiten geprijsd')).toBeInTheDocument()
    const missing = within(price.getByRole('list', { name: 'Ontbrekende prijzen' })).getAllByRole('listitem')
    expect(missing).toHaveLength(1)
    expect(missing[0]).toHaveTextContent('0006')

    // Documents: the unique count from the DTO. Notes: the count + link, never the legacy free text.
    expect(overviewCard('Documenten').getByText('4 documenten')).toBeInTheDocument()
    const history = overviewCard('Notities & historiek')
    expect(history.getByRole('link', { name: /3 dossiernotities/ })).toHaveAttribute('href', '/dossiers/d-1/historiek')
    expect(screen.queryByText('Legacy vrije tekst')).not.toBeInTheDocument()
  })

  it('shows the work site of an on-site lifting job — no fictitious load/unload rows, no goods error', async () => {
    const base = orderDetail()
    orders.get.mockResolvedValue({
      ...orderDetail({ id: 'o-6', orderNumber: '0006', cargoItems: [], goodsDescription: null }),
      craneJobKind: 'OnSiteLifting',
      workDescription: 'Airco-unit op dak plaatsen',
      stops: [{ ...base.stops[0], id: 's-site', stopType: 'Site', locationName: 'Werf Gent Dampoort', city: 'Gent' }],
    })
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ activityTypeName: 'Kraantransport', supportsOnSiteWork: true })]))
    renderPage('/dossiers/d-1')
    await screen.findByRole('article', { name: 'Route' })

    const route = overviewCard('Route')
    expect(await route.findByText('Werf Gent Dampoort')).toBeInTheDocument()
    expect(route.getByText('Werf')).toBeInTheDocument()
    expect(route.queryByText('Laden')).not.toBeInTheDocument()
    expect(route.queryByText('Lossen')).not.toBeInTheDocument()
    expect(route.getByText('Ingevuld')).toBeInTheDocument()

    const goods = overviewCard('Goederen')
    expect(goods.getByText('Niet van toepassing')).toBeInTheDocument()
    expect(goods.queryByText('Nog geen goederen toegevoegd.')).not.toBeInTheDocument()
    expect(goods.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('never calls a route complete while the DTO still flags a missing order route', async () => {
    orders.get.mockResolvedValue(
      orderDetail({ id: 'o-6', orderNumber: '0006', stops: [orderDetail().stops[0], { ...orderDetail().stops[0], id: 's-2', sequence: 2, stopType: 'Unloading', locationName: 'Klant Luik' }] }),
    )
    api.getDossier.mockResolvedValue(
      dossierWith(
        [orderActivity(), dossierActivity({ id: 'a-7', sequence: 2, activityTypeName: 'Natransport' })],
        { readiness: [{ code: 'route.order_missing', severity: 'Warning', message: 'Natransport: nog geen transportopdracht.', section: 'route', field: 'stops.loading', stage: 'Planning', activityId: 'a-7' }] },
      ),
    )
    renderPage('/dossiers/d-1')
    await screen.findByRole('article', { name: 'Route' })

    const route = overviewCard('Route')
    expect(await route.findByText('Klant Luik')).toBeInTheDocument()
    expect(route.getByText('Niet ingevuld')).toBeInTheDocument()
    expect(route.queryByText('Ingevuld')).not.toBeInTheDocument()
  })

  it('no longer edits the legacy free-text notes: the stored value travels back unchanged', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    const dossier = dossierWith([], { notes: 'Legacy vrije tekst' })
    api.getDossier.mockResolvedValue(dossier)
    api.updateDossier.mockResolvedValue({ ...dossier, title: 'Nieuwe titel', version: 'v-2' })
    renderPage('/dossiers/d-1')

    await user.click(await screen.findByRole('menuitem', { name: 'Bewerken', hidden: true }))
    const dialog = within(await screen.findByRole('dialog', { name: 'Dossier bewerken' }))
    expect(dialog.queryByLabelText('Notities')).not.toBeInTheDocument()
    expect(dialog.queryByDisplayValue('Legacy vrije tekst')).not.toBeInTheDocument()

    await user.clear(dialog.getByLabelText(/Titel/))
    await user.type(dialog.getByLabelText(/Titel/), 'Nieuwe titel')
    await user.click(dialog.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(api.updateDossier).toHaveBeenCalledTimes(1))
    expect(api.updateDossier.mock.calls[0][1]).toMatchObject({ title: 'Nieuwe titel', notes: 'Legacy vrije tekst', version: 'v-1' })
  })

  it('feeds the capacity hint with the payload and tail-lift capacity of the assigned vehicle', async () => {
    const line = { ...orderDetail().cargoItems[0], expectedQuantity: 2, weightPerUnitKg: 900, totalWeightKg: 1800 }
    orders.get.mockResolvedValue(orderDetail({ id: 'o-6', orderNumber: '0006', cargoItems: [line] }))
    vehiclesApi.getVehicle.mockResolvedValue({ id: 'v-1', payloadKg: 12000, tailLiftCapacityKg: 750 })
    api.getDossier.mockResolvedValue(dossierWith([orderActivity({ assignment: assignment() })]))
    renderPage('/dossiers/d-1/goederen')

    const hint = within(await screen.findByRole('complementary', { name: 'Operationele capaciteit' }))
    await waitFor(() => expect(vehiclesApi.getVehicle).toHaveBeenCalledWith('v-1', expect.any(AbortSignal)))
    expect(await hint.findByText(/12\.000 kg/)).toBeInTheDocument()
    // Heaviest unit (900 kg) above the tail lift (750 kg): a warning — and it never says "safe".
    expect(await hint.findByRole('alert')).toHaveTextContent(/900 kg/)
    expect(hint.getByText(/Capaciteit nog te controleren/)).toBeInTheDocument()
  })

  it('leaves the capacity unknown for an activity without a vehicle', async () => {
    const line = { ...orderDetail().cargoItems[0], weightPerUnitKg: 900, totalWeightKg: 1800 }
    orders.get.mockResolvedValue(orderDetail({ id: 'o-6', orderNumber: '0006', cargoItems: [line] }))
    api.getDossier.mockResolvedValue(dossierWith([orderActivity()]))
    renderPage('/dossiers/d-1/goederen')

    const hint = within(await screen.findByRole('complementary', { name: 'Operationele capaciteit' }))
    expect(hint.getByText(/Capaciteit nog te controleren/)).toBeInTheDocument()
    expect(hint.queryByRole('alert')).not.toBeInTheDocument()
    expect(vehiclesApi.getVehicle).not.toHaveBeenCalled()
  })
})
