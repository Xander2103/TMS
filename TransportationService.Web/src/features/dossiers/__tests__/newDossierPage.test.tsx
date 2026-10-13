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

// The real guard needs the data router; a marker keeps its `when` observable.
vi.mock('../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: ({ when }: { when: boolean }) => <span data-testid="unsaved-guard" data-when={String(when)} />,
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const createCustomer = vi.hoisted(() => vi.fn())
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [{ id: 'c-1', name: 'Van Caudenberg BV' }], totalCount: 1 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
  createCustomer,
  updateCustomer: vi.fn(),
  deleteCustomer: vi.fn(),
  setCustomerActive: vi.fn(),
  setCustomerBlocked: vi.fn(),
  addCustomerContact: vi.fn(),
  updateCustomerContact: vi.fn(),
  removeCustomerContact: vi.fn(),
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

/** Opens a section of the sectioned create flow through its subnav tab (free order, no wizard). */
async function goTo(user: ReturnType<typeof userEvent.setup>, section: string) {
  await user.click(screen.getByRole('tab', { name: new RegExp(`^${section}`) }))
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

    // The intake sections appear in the subnav at once; Basis stays the active one.
    for (const section of ['Basis', 'Route', 'Goederen', 'Vereisten', 'Controle']) {
      expect(screen.getByRole('tab', { name: new RegExp(`^${section}`) })).toBeInTheDocument()
    }
    expect(screen.getByRole('tab', { name: /^Basis/ })).toHaveAttribute('aria-selected', 'true')

    // Route: 1 loading + 1 unloading stop with address selectors, dates and time requirement.
    await goTo(user, 'Route')
    expect(screen.getAllByLabelText('locatie')).toHaveLength(2)
    expect(legendWith(/1..*Laden/)).toBeInTheDocument()
    expect(legendWith(/2..*Lossen/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Losadres toevoegen' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '+ Losadres toevoegen' }))
    expect(screen.getAllByLabelText('locatie')).toHaveLength(3)

    // Goederen: the goods repeater starts with one line; no stop fields here.
    await goTo(user, 'Goederen')
    expect(screen.getByLabelText(/Verwacht aantal/)).toBeInTheDocument()
    expect(screen.queryByLabelText('locatie')).not.toBeInTheDocument()
  })

  it('Direct transport is a fixed A→B: no add/move/remove chrome', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await goTo(user, 'Route')

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
    await goTo(user, 'Route')
    const cities = screen.getAllByLabelText(/Plaats/)
    await user.type(cities[0], 'Hoeilaart')
    await user.type(cities[1], 'Gent')
    // One goods line with a description.
    await goTo(user, 'Goederen')
    await user.type(screen.getByLabelText('Omschrijving'), 'Bouwmateriaal')

    // Controle recaps the real state from every section before the create.
    await goTo(user, 'Controle')
    expect(screen.getByText('Van Caudenberg BV')).toBeInTheDocument()
    expect(screen.getByText('Direct transport')).toBeInTheDocument()
    expect(screen.getByText('Klantreferentie: REF-42')).toBeInTheDocument()
    expect(screen.getByText('1 laadstop(s), 1 losstop(s)')).toBeInTheDocument()
    expect(screen.getByText('Hoeilaart → Gent')).toBeInTheDocument()
    expect(screen.getByText('Bouwmateriaal')).toBeInTheDocument()
    expect(screen.getByText('1 goederenlijn(en)')).toBeInTheDocument()
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
    await goTo(user, 'Route')
    const cities = screen.getAllByLabelText(/Plaats/)
    await user.type(cities[0], 'Hoeilaart')
    await user.type(cities[1], 'Gent')
    await goTo(user, 'Goederen')
    await user.type(screen.getByLabelText('Omschrijving'), 'Bouwmateriaal')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() => expect(createOrder).toHaveBeenCalled())
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    // No navigation, no reset: everything the planner typed is still there — in every section.
    expect(navigateSpy).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Omschrijving')).toHaveValue('Bouwmateriaal')
    await goTo(user, 'Route')
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
    await goTo(user, 'Route')
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await goTo(user, 'Goederen')
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
    await goTo(user, 'Route')
    expect(screen.queryByLabelText(/Duur \(uren\)/)).not.toBeInTheDocument()

    await goTo(user, 'Basis')
    await user.click(screen.getByRole('radio', { name: /Kraantransport/ }))
    await goTo(user, 'Route')
    const duration = screen.getByLabelText(/Duur \(uren\)/)
    await user.type(duration, '4.5')
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await goTo(user, 'Goederen')
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
    await goTo(user, 'Route')
    await user.type(screen.getByLabelText(/Duur \(uren\)/), '-1')
    // Submitting from another section still validates everything and brings the failing one back.
    await goTo(user, 'Controle')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    // Both the inline field error and the validation summary announce the problem; the Route
    // tab carries the error count and is opened again.
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0)
    expect(createOrder).not.toHaveBeenCalled()
    expect(createFast).not.toHaveBeenCalled()
    expect(screen.getByRole('tab', { selected: true })).toHaveAttribute('data-has-error', 'true')
    expect(screen.getByRole('tab', { name: /^Route/ })).toHaveAttribute('data-has-error', 'true')
    await goTo(user, 'Route')
    expect(screen.getByLabelText(/Duur \(uren\)/)).toHaveValue(-1)
  })

  it('asks before a type switch that drops filled extra unload addresses', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Distributie/ }))
    await goTo(user, 'Route')
    await user.click(screen.getByRole('button', { name: '+ Losadres toevoegen' }))
    const cities = screen.getAllByLabelText(/Plaats/)
    await user.type(cities[2], 'Mechelen')

    await goTo(user, 'Basis')
    await user.click(screen.getByRole('radio', { name: /Direct transport/ }))
    // The switch waits for confirmation; nothing is dropped yet.
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/verwijdert 1 ingevuld/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Wisselen' }))

    expect(screen.getByRole('radio', { name: /Direct transport/ })).toBeChecked()
    await goTo(user, 'Route')
    expect(screen.getAllByLabelText('locatie')).toHaveLength(2)
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
    await goTo(user, 'Route')
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await goTo(user, 'Basis')
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

  // Regression (pre-deploy review 2026-09-04): every user-entered order field must count as
  // "touched", otherwise the dossier-only fast path silently drops it.
  it('a ticked ADR flag alone routes to the order create instead of the silent fast path', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await goTo(user, 'Vereisten')
    await user.click(screen.getByLabelText('ADR-transport'))
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    // Not the silent fast path: the order rules apply and tell the user what is still missing —
    // and the section that owns the problem (Goederen) opens with its error count.
    expect((await screen.findAllByText(/Vul minstens een hoeveelheid en eenheid in/)).length).toBeGreaterThan(0)
    expect(createFast).not.toHaveBeenCalled()
    expect(screen.getByRole('tab', { name: /^Goederen/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: /^Goederen/ })).toHaveAttribute('data-has-error', 'true')
    await goTo(user, 'Vereisten')
    expect(screen.getByLabelText('ADR-transport')).toBeChecked()

    await goTo(user, 'Goederen')
    await user.type(screen.getByLabelText('Omschrijving'), 'Gevaarlijke stoffen')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    expect(createOrder.mock.calls[0][0]).toEqual(expect.objectContaining({ activityTypeId: 'at-direct', adrRequired: true }))
    expect(createFast).not.toHaveBeenCalled()
  })

  it('a header weight alone (goods line removed) is saved through the order create, not dropped', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await goTo(user, 'Goederen')
    // Removing the seeded goods line reveals the classic header inputs.
    await user.click(screen.getByRole('button', { name: 'Verwijderen' }))
    await user.type(screen.getByLabelText('Gewicht (kg)'), '1200')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    // Weight alone is not a complete goods entry: the order rules surface, nothing is discarded.
    expect((await screen.findAllByText(/Vul minstens een hoeveelheid en eenheid in/)).length).toBeGreaterThan(0)
    expect(createFast).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Gewicht (kg)')).toHaveValue(1200)

    await user.type(screen.getByLabelText('Omschrijving goederen'), 'Staalplaten')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    expect(createOrder.mock.calls[0][0]).toEqual(expect.objectContaining({ activityTypeId: 'at-direct', weightKg: 1200 }))
    expect(createFast).not.toHaveBeenCalled()
  })

  it('an untouched Kraantransport still takes the fast path: the implied crane flag is not user data', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Kraantransport/ }))
    await goTo(user, 'Vereisten')
    expect(screen.getByLabelText('Kraan vereist')).toBeChecked()
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() =>
      expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c-1', activityTypeId: 'at-kraan' })),
    )
    expect(createOrder).not.toHaveBeenCalled()
  })

  it('switching away from Kraantransport clears the implied crane flag unless the user set it', async () => {
    const user = userEvent.setup()
    renderPage()

    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Kraantransport/ }))
    await goTo(user, 'Vereisten')
    expect(screen.getByLabelText('Kraan vereist')).toBeChecked()
    await goTo(user, 'Basis')
    await user.click(screen.getByRole('radio', { name: /Direct transport/ }))
    await goTo(user, 'Vereisten')
    expect(screen.getByLabelText('Kraan vereist')).not.toBeChecked()

    // Untouched after the switch → still the fast path.
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() => expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ activityTypeId: 'at-direct' })))
    expect(createOrder).not.toHaveBeenCalled()
  })
})

/**
 * Dossier UX correction sprint (2026-10-08): "+ Nieuwe klant" from inside Nieuw dossier. The
 * quick-create dialog posts to the same customer endpoint as the Klanten page, auto-selects the
 * result and never touches the dossier form state.
 */
describe('NewDossierPage — + Nieuwe klant', () => {
  const quickCustomer = { id: 'c-new', customerNumber: 'KL-0042', name: 'Quick Test Customer' }

  beforeEach(() => {
    createCustomer.mockReset()
    navigateSpy.mockClear()
    auth.permissions = new Set(['dossiers.manage', 'orders.create', 'locations.create', 'customers.create'])
  })

  /** The combobox shows the selected label as value when closed and as placeholder while its list is open. */
  function expectSelectedCustomer(name: string) {
    const input = screen.getByRole('combobox', { name: 'Klant' }) as HTMLInputElement
    expect([input.value, input.getAttribute('placeholder')]).toContain(name)
  }

  it('offers "+ Nieuwe klant" under the field and as a permanent row in the dropdown — with customers.create', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('combobox', { name: 'Klant' })

    expect(screen.getByRole('button', { name: '+ Nieuwe klant' })).toBeInTheDocument()
    await user.click(screen.getByRole('combobox', { name: 'Klant' }))
    expect(await screen.findByText('Van Caudenberg BV')).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /\+ Nieuwe klant/ })).toBeInTheDocument()
  })

  it('hides every create action without customers.create', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['dossiers.manage', 'orders.create'])
    renderPage()
    await screen.findByRole('combobox', { name: 'Klant' })

    expect(screen.queryByRole('button', { name: '+ Nieuwe klant' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('combobox', { name: 'Klant' }))
    await screen.findByText('Van Caudenberg BV')
    expect(screen.queryByRole('option', { name: /Nieuwe klant/ })).not.toBeInTheDocument()
    await user.type(screen.getByRole('combobox', { name: 'Klant' }), 'Onbekend')
    expect(await screen.findByText('Geen klant gevonden')).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /nieuwe klant/ })).not.toBeInTheDocument()
  })

  it('a search without results offers to add the typed name as a new customer', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.type(await screen.findByRole('combobox', { name: 'Klant' }), 'Quick Test')

    // With nothing matching, the create row IS the empty state (the only row offered).
    const createRow = await screen.findByRole('option', { name: '+ "Quick Test" als nieuwe klant toevoegen' })
    expect(within(screen.getByRole('listbox')).getAllByRole('option')).toHaveLength(1)
    await user.click(createRow)
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe klant' })
    // The typed search becomes the name; nothing else is required.
    expect(within(dialog).getByLabelText(/^Naam/)).toHaveValue('Quick Test')
    expect(within(dialog).getByLabelText(/^Naam/)).toHaveFocus()
  })

  it('creates the customer through the customer API, selects it, keeps the dossier data and saves with the new CustomerId', async () => {
    const user = userEvent.setup()
    createCustomer.mockResolvedValue(quickCustomer)
    createFast.mockResolvedValue({ id: 'd-1', dossierNumber: 'DOS-0009' })
    renderPage()
    await screen.findByRole('combobox', { name: 'Klant' })

    // Data entered BEFORE the customer exists must survive the detour.
    await user.type(screen.getByLabelText('Klantreferentie'), 'REF-77')
    await user.click(screen.getByRole('button', { name: '+ Nieuwe klant' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe klant' })
    await user.type(within(dialog).getByLabelText(/^Naam/), 'Quick Test Customer')
    await user.type(within(dialog).getByLabelText('Plaats'), 'Gent')
    await user.click(within(dialog).getByRole('button', { name: 'Klant aanmaken' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nieuwe klant' })).not.toBeInTheDocument())
    expect(createCustomer).toHaveBeenCalledTimes(1)
    expect(createCustomer).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Quick Test Customer', city: 'Gent', paymentTermDays: 30, vatTreatment: 'DomesticVat', peppolEnabled: false, vatNumber: null }),
    )
    // Auto-selected (focus is back on the field), no second search; the earlier reference is still there.
    await waitFor(() => expectSelectedCustomer('Quick Test Customer'))
    expect(screen.getByLabelText('Klantreferentie')).toHaveValue('REF-77')
    expect(screen.getByRole('link', { name: /Klantgegevens openen/ })).toHaveAttribute('href', '/customers/c-new')

    await user.click(screen.getByLabelText('Opslag'))
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() => expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c-new', customerReference: 'REF-77' })))
  })

  it('selects the customer created from the dropdown row as well, and the dropdown lists it afterwards', async () => {
    const user = userEvent.setup()
    createCustomer.mockResolvedValue(quickCustomer)
    renderPage()
    await user.type(await screen.findByRole('combobox', { name: 'Klant' }), 'Quick')
    await user.click(await screen.findByRole('option', { name: '+ "Quick" als nieuwe klant toevoegen' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe klant' })
    await user.clear(within(dialog).getByLabelText(/^Naam/))
    await user.type(within(dialog).getByLabelText(/^Naam/), 'Quick Test Customer')
    await user.click(within(dialog).getByRole('button', { name: 'Klant aanmaken' }))

    await waitFor(() => expectSelectedCustomer('Quick Test Customer'))
    await user.click(screen.getByRole('combobox', { name: 'Klant' }))
    expect(await screen.findByRole('option', { name: /Quick Test Customer/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('option', { name: /Van Caudenberg BV/ })).toBeInTheDocument()
  })

  it('keeps the dialog open with the server validation error; the dossier form is untouched', async () => {
    const user = userEvent.setup()
    const { ApiError } = await import('../../../api/apiClient')
    createCustomer.mockRejectedValue(
      new ApiError('Validation failed', 400, { title: 'Validation failed', status: 400, errors: { vatNumber: ['Ongeldig BTW-nummer.'] } }),
    )
    auth.permissions.add('customers.manage_fiscal')
    renderPage()
    await screen.findByRole('combobox', { name: 'Klant' })
    await user.type(screen.getByLabelText('Klantreferentie'), 'REF-1')
    await user.click(screen.getByRole('button', { name: '+ Nieuwe klant' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe klant' })

    // Client side first: a name is required (same rule and wording as the Klanten page).
    await user.click(within(dialog).getByRole('button', { name: 'Klant aanmaken' }))
    expect(within(dialog).getByText('Naam is verplicht.')).toBeInTheDocument()
    expect(createCustomer).not.toHaveBeenCalled()

    await user.type(within(dialog).getByLabelText(/^Naam/), 'Foutje BV')
    await user.type(within(dialog).getByLabelText(/BTW-nummer/), 'XX')
    await user.click(within(dialog).getByRole('button', { name: 'Klant aanmaken' }))
    expect(await within(dialog).findByText('Ongeldig BTW-nummer.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Nieuwe klant' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Klant' })).toHaveValue('')
    expect(screen.getByLabelText('Klantreferentie')).toHaveValue('REF-1')
  })

  it('Escape closes only the quick-create and leaves the dossier form as it was', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('combobox', { name: 'Klant' })
    await user.type(screen.getByLabelText('Klantreferentie'), 'REF-2')
    await user.click(screen.getByRole('button', { name: '+ Nieuwe klant' }))
    await screen.findByRole('dialog', { name: 'Nieuwe klant' })

    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nieuwe klant' })).not.toBeInTheDocument())
    expect(createCustomer).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Klantreferentie')).toHaveValue('REF-2')
    expect(screen.getByRole('heading', { name: 'Nieuw dossier' })).toBeInTheDocument()
    expect(navigateSpy).not.toHaveBeenCalled()
  })

  it('never submits the customer twice while the first create is pending', async () => {
    const user = userEvent.setup()
    let release!: (value: typeof quickCustomer) => void
    createCustomer.mockImplementation(() => new Promise<typeof quickCustomer>((resolve) => { release = resolve }))
    renderPage()
    await screen.findByRole('combobox', { name: 'Klant' })
    await user.click(screen.getByRole('button', { name: '+ Nieuwe klant' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe klant' })
    await user.type(within(dialog).getByLabelText(/^Naam/), 'Dubbel BV')

    const submit = within(dialog).getByRole('button', { name: 'Klant aanmaken' })
    await user.click(submit)
    await user.click(screen.getByRole('button', { name: 'Aanmaken…' }))
    await user.keyboard('{Enter}')
    expect(createCustomer).toHaveBeenCalledTimes(1)

    release(quickCustomer)
    await waitFor(() => expectSelectedCustomer('Quick Test Customer'))
  })
})

/**
 * Create redesign (2026-10-08): ONE lifted form state behind a free subnavigation
 * (Basis · Route · Goederen · Vereisten · Controle) — no blocking wizard, nothing lost between
 * sections, validation routed to the section that owns the field, review before create.
 */
describe('NewDossierPage — subnavigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.manage', 'orders.create', 'locations.create'])
    createFast.mockResolvedValue(dossierDetail({ id: 'd-9', dossierNumber: 'DOS-0009' }))
    createOrder.mockResolvedValue({ id: 'o-1', orderNumber: 'ORD-0001', dossierId: 'd-77', dossierNumber: 'DOS-0077' })
  })

  const tabNames = () => screen.getAllByRole('tab').map((tab) => tab.textContent?.replace(/[!✓●○\d]/g, '').trim())

  it('starts on Basis with only Basis and Controle; the transport sections follow the chosen type', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('radio', { name: /Distributie/ })

    expect(tabNames()).toEqual(['Basis', 'Controle'])
    expect(screen.getByRole('tab', { name: /^Basis/ })).toHaveAttribute('aria-selected', 'true')
    // The same sections are offered to small screens as a select.
    expect(screen.getByRole('combobox', { name: 'Sectie' })).toBeInTheDocument()

    await pickCustomer(user)
    await user.click(screen.getByRole('radio', { name: /Opslag/ }))
    // A type without stops has no route/goods/requirements to fill.
    expect(tabNames()).toEqual(['Basis', 'Controle'])

    await user.click(screen.getByRole('radio', { name: /Direct transport/ }))
    expect(tabNames()).toEqual(['Basis', 'Route', 'Goederen', 'Vereisten', 'Controle'])
  })

  it('navigates in any order and keeps every section\'s data; Vorige/Volgende are only shortcuts', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.type(screen.getByLabelText('Klantreferentie'), 'REF-9')
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))

    // Basis → Goederen → Route → Vereisten → Basis, straight from the tabs.
    await goTo(user, 'Goederen')
    await user.type(screen.getByLabelText('Omschrijving'), 'Kabelhaspels')
    await goTo(user, 'Route')
    await user.type(screen.getAllByLabelText(/Plaats/)[0], 'Hoeilaart')
    await goTo(user, 'Vereisten')
    await user.click(screen.getByLabelText('Plateau vereist'))
    await user.type(screen.getByLabelText('Opmerkingen / instructies'), 'Bel vooraf')
    await goTo(user, 'Basis')
    expect(screen.getByLabelText('Klantreferentie')).toHaveValue('REF-9')
    expect(screen.getByRole('radio', { name: /Direct transport/ })).toBeChecked()

    // The footer "Naar Route →" is the primary forward action on an intake; it focuses the heading.
    await user.click(screen.getByRole('button', { name: 'Naar Route →' }))
    expect(screen.getByRole('heading', { name: 'Route' })).toHaveFocus()
    expect(screen.getAllByLabelText(/Plaats/)[0]).toHaveValue('Hoeilaart')
    await user.click(screen.getByRole('button', { name: '← Basis' }))
    expect(screen.getByRole('heading', { name: 'Basis' })).toHaveFocus()

    await goTo(user, 'Goederen')
    expect(screen.getByLabelText('Omschrijving')).toHaveValue('Kabelhaspels')
    await goTo(user, 'Vereisten')
    expect(screen.getByLabelText('Plateau vereist')).toBeChecked()
    expect(screen.getByLabelText('Opmerkingen / instructies')).toHaveValue('Bel vooraf')
    // Filled sections show it on their tab.
    expect(screen.getByRole('tab', { name: /^Route/ })).toHaveTextContent('●')
    expect(screen.getByRole('tab', { name: /^Goederen/ })).toHaveTextContent('●')
    expect(screen.getByRole('tab', { name: /^Vereisten/ })).toHaveTextContent('●')
    expect(screen.getByRole('tab', { name: /^Basis/ })).toHaveTextContent('✓')
  })

  it('the create payload carries the data of every section, and Controle recaps requirements and remarks', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await goTo(user, 'Route')
    await user.type(screen.getAllByLabelText(/Plaats/)[0], 'Hoeilaart')
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await goTo(user, 'Goederen')
    await user.type(screen.getByLabelText('Omschrijving'), 'Kabelhaspels')
    await goTo(user, 'Vereisten')
    await user.click(screen.getByLabelText('Plateau vereist'))
    await user.click(screen.getByLabelText(/Moffett/))
    await user.type(screen.getByLabelText('Opmerkingen / instructies'), 'Bel vooraf')

    await goTo(user, 'Controle')
    expect(screen.getByText('Plateau vereist')).toBeInTheDocument()
    expect(screen.getByText(/Moffett/)).toBeInTheDocument()
    expect(screen.getByText('Bel vooraf')).toBeInTheDocument()
    expect(screen.getByText('Kabelhaspels')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    const payload = createOrder.mock.calls[0][0]
    expect(payload).toEqual(expect.objectContaining({ plateauRequired: true, moffettRequired: true, notes: 'Bel vooraf', activityTypeId: 'at-direct' }))
    expect(payload.stops.map((stop: { city: string }) => stop.city)).toEqual(['Hoeilaart', 'Gent'])
    expect(payload.cargoItems[0]).toEqual(expect.objectContaining({ description: 'Kabelhaspels' }))
  })

  it('a validation error from Controle marks the owning section, and the summary link opens it on the field', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await goTo(user, 'Route')
    await user.type(screen.getAllByLabelText(/Plaats/)[0], 'Hoeilaart')
    await goTo(user, 'Vereisten')
    await user.click(screen.getByLabelText('ADR-transport'))
    await goTo(user, 'Controle')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    const goods = screen.getByRole('tab', { name: /^Goederen/ })
    expect(goods).toHaveAttribute('data-has-error', 'true')
    expect(within(goods).getByLabelText(/bevat .*fout/)).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /^Route/ })).not.toHaveAttribute('data-has-error')
    expect(createOrder).not.toHaveBeenCalled()

    // From another section, the summary link brings the planner straight to the failing field.
    await goTo(user, 'Basis')
    const link = screen.getAllByRole('button', { name: /Vul minstens een hoeveelheid en eenheid in/ })[0]
    await user.click(link)
    expect(screen.getByRole('tab', { name: /^Goederen/ })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(document.activeElement).toHaveAttribute('aria-invalid', 'true'))
  })

  it('a blanco dossier needs no fictitious sections: Basis and Controle only, with the review saying so', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /blanco dossier/i }))
    expect(tabNames()).toEqual(['Basis', 'Controle'])
    // Creating is the primary action here; Controle is one click away but never required.
    expect(screen.getByRole('button', { name: 'Dossier aanmaken' })).toHaveClass('ui-button-primary')

    await user.click(screen.getByRole('button', { name: 'Naar Controle →' }))
    expect(screen.getByText('Blanco dossier — alleen klant, referentie en datum')).toBeInTheDocument()
    expect(screen.getByText('Van Caudenberg BV')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() => expect(createFast).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c-1', activityTypeId: null })))
  })

  it('guards against leaving with unsaved input, but not before anything was entered or while creating', async () => {
    const user = userEvent.setup()
    renderPage()
    expect(screen.getByTestId('unsaved-guard')).toHaveAttribute('data-when', 'false')
    await pickCustomer(user)
    expect(screen.getByTestId('unsaved-guard')).toHaveAttribute('data-when', 'true')
    await user.click(await screen.findByRole('radio', { name: /blanco dossier/i }))
    let release: (value: unknown) => void = () => undefined
    createFast.mockImplementation(() => new Promise((resolve) => { release = resolve }))
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    // While the create runs the guard steps aside so the success navigation can proceed.
    await waitFor(() => expect(screen.getByTestId('unsaved-guard')).toHaveAttribute('data-when', 'false'))
    release(dossierDetail({ id: 'd-9', dossierNumber: 'DOS-0009' }))
    await waitFor(() => expect(navigateSpy).toHaveBeenCalledWith('/dossiers/d-9'))
  })
})
