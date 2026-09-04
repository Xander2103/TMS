import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewDossierPage } from '../pages/NewDossierPage'
import { dossierDetail } from './fixtures'

const navigateSpy = vi.hoisted(() => vi.fn())
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => navigateSpy,
}))

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['dossiers.manage', 'orders.create', 'locations.create']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [{ id: 'c-1', name: 'Van Caudenberg BV' }], totalCount: 1 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: () => Promise.resolve([]),
}))
vi.mock('../../warehousing/api/warehousingApi', () => ({
  listWarehouses: () => Promise.resolve([]),
}))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [{ id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' }],
    isLoading: false,
    error: null,
  }),
}))
vi.mock('../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../tarification/api/pricingApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../tarification/api/pricingApi')>()),
  listServiceOptions: () => Promise.resolve([]),
  getCustomerPricingConfig: () => Promise.resolve({ preferredUnits: [], serviceOptions: [] }),
  listUnitTypeMaster: () => Promise.resolve([]),
  previewPrice: () => Promise.resolve(null),
}))

vi.mock('../api/activityTypesApi', () => ({
  listActivityTypes: () =>
    Promise.resolve([
      {
        id: 'at-dist', code: 'DISTRIBUTIE', name: 'Distributie', isActive: true, sortOrder: 1,
        icon: 'route', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
        warehouseRelevant: true, allowsDuration: false, isQuickStart: true, quickStartOrder: 1,
        isSystemDefaultTransport: false,
      },
      {
        id: 'at-direct', code: 'DIRECT_TRANSPORT', name: 'Direct transport', isActive: true, sortOrder: 2,
        icon: 'truck', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: false, isQuickStart: true, quickStartOrder: 2,
        isSystemDefaultTransport: true,
      },
      {
        id: 'at-kraan', code: 'KRAANTRANSPORT', name: 'Kraantransport', isActive: true, sortOrder: 3,
        icon: 'crane', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: true, isQuickStart: true, quickStartOrder: 3,
        isSystemDefaultTransport: false,
      },
      {
        id: 'at-opslag', code: 'OPSLAG', name: 'Opslag', isActive: true, sortOrder: 4,
        icon: 'warehouse', kpiCategory: null, hasStops: false, supportsGoods: true, planningRelevant: false,
        warehouseRelevant: true, allowsDuration: false, isQuickStart: true, quickStartOrder: 4,
        isSystemDefaultTransport: false,
      },
      {
        id: 'at-overig', code: 'OVERIG', name: 'Overig', isActive: true, sortOrder: 9, icon: null,
        kpiCategory: null, hasStops: false, supportsGoods: false, planningRelevant: false,
        warehouseRelevant: false, allowsDuration: true, isQuickStart: false, quickStartOrder: 0,
        isSystemDefaultTransport: false,
      },
    ]),
}))

const createFast = vi.hoisted(() => vi.fn())
vi.mock('../api/dossiersApi', () => ({
  createDossierFast: createFast,
}))

const createOrder = vi.hoisted(() => vi.fn())
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  createTransportOrder: createOrder,
}))

const legendWith = (text: RegExp) =>
  screen.getByText((_, el) => el?.tagName === 'LEGEND' && text.test(el.textContent ?? ''))

async function pickCustomer(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('combobox', { name: 'Klant' }))
  await user.click(await screen.findByText('Van Caudenberg BV'))
}

function renderPage() {
  return render(
    <MemoryRouter>
      <NewDossierPage />
    </MemoryRouter>,
  )
}

describe('NewDossierPage — one-page intake', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.manage', 'orders.create', 'locations.create'])
    createFast.mockResolvedValue(dossierDetail({ id: 'd-9', dossierNumber: 'DOS-0009' }))
    createOrder.mockResolvedValue({ id: 'o-1', orderNumber: 'ORD-0001', dossierId: 'd-77', dossierNumber: 'DOS-0077' })
  })

  it('renders klant, referentie, datum and type tiles; route/goods stay hidden until a type is chosen', async () => {
    renderPage()

    expect(screen.getByRole('combobox', { name: 'Klant' })).toBeInTheDocument()
    expect(screen.getByLabelText('Klantreferentie')).toBeInTheDocument()
    expect(screen.getByLabelText('Datum')).toHaveValue(new Date().toISOString().slice(0, 10))

    // Real radios: quick-start tiles sorted, blanco as a quiet option below (not a tile).
    expect(await screen.findByRole('radio', { name: /Distributie/ })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Direct transport/ })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /blanco dossier/i })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Overig/ })).not.toBeInTheDocument()
    // Nothing is preselected; the intake stays hidden and submit is disabled.
    expect(screen.queryByRole('radio', { checked: true })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('locatie')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Dossier aanmaken' })).toBeDisabled()
  })

  it('blanco keeps the classic minimal flow', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /blanco dossier/i }))
    expect(screen.queryByLabelText('locatie')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c-1', activityTypeId: null })),
    )
    expect(createOrder).not.toHaveBeenCalled()
    expect(navigateSpy).toHaveBeenCalledWith('/dossiers/d-9')
  })

  it('Distributie shows the transport intake immediately: route, goods and + Losadres toevoegen', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Distributie/ }))

    // 1 loading + 1 unloading stop appear with address selectors, dates and time requirement.
    expect(screen.getAllByLabelText('locatie')).toHaveLength(2)
    expect(legendWith(/1..*Laden/)).toBeInTheDocument()
    expect(legendWith(/2..*Lossen/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Losadres toevoegen' })).toBeInTheDocument()
    // The goods repeater starts with one line.
    expect(screen.getByLabelText(/Verwacht aantal/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '+ Losadres toevoegen' }))
    expect(screen.getAllByLabelText('locatie')).toHaveLength(3)
  })

  it('Direct transport is a fixed A→B: no add/move/remove chrome', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))

    expect(screen.getAllByLabelText('locatie')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: '+ Losadres toevoegen' })).not.toBeInTheDocument()
    // Compact mode: no per-stop move/collapse buttons, and no remove on the fixed pair.
    expect(screen.queryByRole('button', { name: '↑' })).not.toBeInTheDocument()
    const routeSection = legendWith(/1..*Laden/).closest('fieldset')!
    expect(within(routeSection).queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })

  it('an untouched intake submits the classic dossier-only create with the chosen type (fast path)', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Distributie/ }))
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c-1', activityTypeId: 'at-dist' })),
    )
    expect(createOrder).not.toHaveBeenCalled()
  })

  it('a filled intake creates order + stops + goods in ONE call and navigates to the wrapper dossier', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))

    await user.type(screen.getByLabelText('Klantreferentie'), 'REF-42')
    // Free addresses on both stops.
    const cities = screen.getAllByLabelText(/Plaats/)
    await user.type(cities[0], 'Hoeilaart')
    await user.type(cities[1], 'Gent')
    // One goods line with a description.
    await user.type(screen.getByLabelText('Omschrijving'), 'Bouwmateriaal')

    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    const payload = createOrder.mock.calls[0][0]
    expect(payload).toEqual(
      expect.objectContaining({ customerId: 'c-1', customerReference: 'REF-42', activityTypeId: 'at-direct' }),
    )
    expect(payload.stops).toHaveLength(2)
    expect(payload.stops[0]).toEqual(expect.objectContaining({ stopType: 'Loading', city: 'Hoeilaart' }))
    expect(payload.stops[1]).toEqual(expect.objectContaining({ stopType: 'Unloading', city: 'Gent' }))
    expect(payload.cargoItems).toHaveLength(1)
    expect(payload.cargoItems[0]).toEqual(expect.objectContaining({ description: 'Bouwmateriaal' }))
    expect(createFast).not.toHaveBeenCalled()
    expect(navigateSpy).toHaveBeenCalledWith('/dossiers/d-77')
  })

  it('keeps the form state on a server error', async () => {
    const user = userEvent.setup()
    createOrder.mockRejectedValue(new Error('boom'))
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    const cities = screen.getAllByLabelText(/Plaats/)
    await user.type(cities[0], 'Hoeilaart')
    await user.type(cities[1], 'Gent')
    await user.type(screen.getByLabelText('Omschrijving'), 'Bouwmateriaal')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() => expect(createOrder).toHaveBeenCalled())
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    // No navigation, no reset: everything the planner typed is still there.
    expect(navigateSpy).not.toHaveBeenCalled()
    expect(screen.getAllByLabelText(/Plaats/)[1]).toHaveValue('Gent')
    expect(screen.getByRole('button', { name: 'Dossier aanmaken' })).toBeEnabled()
  })

  it('submits only once while the request is pending', async () => {
    const user = userEvent.setup()
    let resolveCreate: (value: unknown) => void = () => undefined
    createOrder.mockImplementation(() => new Promise((resolve) => { resolveCreate = resolve }))
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await user.type(screen.getByLabelText('Omschrijving'), 'Bouwmateriaal')

    const submit = screen.getByRole('button', { name: 'Dossier aanmaken' })
    await user.click(submit)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Aanmaken…' })).toBeDisabled())
    await user.click(screen.getByRole('button', { name: 'Aanmaken…' }))
    expect(createOrder).toHaveBeenCalledTimes(1)
    resolveCreate({ id: 'o-1', orderNumber: 'ORD-0001', dossierId: 'd-77', dossierNumber: 'DOS-0077' })
  })

  it('Kraantransport shows the duration field and submits it with the intake; other types hide it', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    // Duration is domain-driven (AllowsDuration), so Direct transport has no such field.
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    expect(screen.queryByLabelText(/Duur \(uren\)/)).not.toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: /Kraantransport/ }))
    const duration = screen.getByLabelText(/Duur \(uren\)/)
    await user.type(duration, '4.5')
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await user.type(screen.getByLabelText('Omschrijving'), 'Betonelementen')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    const payload = createOrder.mock.calls[0][0]
    expect(payload).toEqual(
      expect.objectContaining({ activityTypeId: 'at-kraan', activityDurationHours: 4.5, craneRequired: true }),
    )
  })

  it('rejects a negative duration client-side and keeps the form state', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Kraantransport/ }))
    await user.type(screen.getByLabelText(/Duur \(uren\)/), '-1')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    // Both the inline field error and the validation summary announce the problem.
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0)
    expect(createOrder).not.toHaveBeenCalled()
    expect(createFast).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/Duur \(uren\)/)).toHaveValue(-1)
  })

  it('asks before a type switch that drops filled extra unload addresses', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Distributie/ }))
    await user.click(screen.getByRole('button', { name: '+ Losadres toevoegen' }))
    const cities = screen.getAllByLabelText(/Plaats/)
    await user.type(cities[2], 'Mechelen')

    await user.click(screen.getByRole('radio', { name: /Direct transport/ }))
    // The switch waits for confirmation; nothing is dropped yet.
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/verwijdert 1 ingevuld/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Wisselen' }))

    expect(screen.getAllByLabelText('locatie')).toHaveLength(2)
    expect(screen.getByRole('radio', { name: /Direct transport/ })).toBeChecked()
  })

  it('switching without data loss never asks', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Distributie/ }))
    await user.click(screen.getByRole('radio', { name: /Direct transport/ }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Direct transport/ })).toBeChecked()
  })

  it('warns when submitting a type without route support while route/goods carry data', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await user.click(screen.getByRole('radio', { name: /Opslag/ }))
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    // The filled route would be discarded — an explicit confirmation gates the create.
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ activityTypeId: 'at-opslag' })),
    )
    expect(createOrder).not.toHaveBeenCalled()
  })

  it('falls back to the classic create when the user lacks orders.create', async () => {
    auth.permissions = new Set(['dossiers.manage'])
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Distributie/ }))
    // No transport intake without the order permission — only the hint.
    expect(screen.queryByLabelText('locatie')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ activityTypeId: 'at-dist' })),
    )
  })
})
