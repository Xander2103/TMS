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

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [{ id: 'c-1', name: 'Van Caudenberg BV' }], totalCount: 1 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
vi.mock('../../warehousing/api/warehousingApi', () => ({ listWarehouses: () => Promise.resolve([]) }))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [{ id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' }], isLoading: false, error: null }),
}))
vi.mock('../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../locations/api/customerAddressesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../locations/api/customerAddressesApi')>()),
  pickAddresses: () => Promise.resolve([]),
  checkAddressDuplicates: () => Promise.resolve({ hasExactMatch: false, candidates: [] }),
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

const activityType = (overrides: Record<string, unknown>) => ({
  isActive: true, sortOrder: 1, icon: 'truck', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
  warehouseRelevant: false, allowsDuration: false, isQuickStart: true, quickStartOrder: 1, isSystemDefaultTransport: false,
  isBillable: true, ...overrides,
})
vi.mock('../api/activityTypesApi', () => ({
  listActivityTypes: () =>
    Promise.resolve([
      activityType({ id: 'at-direct', code: 'DIRECT_TRANSPORT', name: 'Direct transport', quickStartOrder: 1 }),
      // The capability FLAG decides — deliberately on a type whose code says nothing about cranes.
      activityType({ id: 'at-lift', code: 'SPECIAAL', name: 'Speciaal werk', allowsDuration: true, quickStartOrder: 2, supportsOnSiteWork: true }),
      // …and a crane-coded type WITHOUT the flag offers no choice.
      activityType({ id: 'at-kraan', code: 'KRAANTRANSPORT', name: 'Kraantransport', allowsDuration: true, quickStartOrder: 3 }),
    ]),
}))

const createFast = vi.hoisted(() => vi.fn())
vi.mock('../api/dossiersApi', () => ({ createDossierFast: createFast }))
const createOrder = vi.hoisted(() => vi.fn())
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({ createTransportOrder: createOrder }))

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

// (Goods lines reuse the .tof-stop card class — only the route grid holds stop cards.)
const stopCards = () => Array.from(document.querySelectorAll<HTMLElement>('.tof-stops-grid > fieldset.tof-stop'))

describe('NewDossierPage — soort kraanopdracht (D2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    createFast.mockResolvedValue(dossierDetail({ id: 'd-9', dossierNumber: 'DOS-0009' }))
    createOrder.mockResolvedValue({ id: 'o-1', orderNumber: 'ORD-0001', dossierId: 'd-77', dossierNumber: 'DOS-0077' })
  })

  it('offers the choice ONLY for a type with supportsOnSiteWork; default = transport met kraan (today’s behaviour)', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)

    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    expect(screen.queryByRole('radio', { name: 'Hijswerk op locatie' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: /Kraantransport/ }))
    expect(screen.queryByRole('radio', { name: 'Hijswerk op locatie' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: /Speciaal werk/ }))
    expect(screen.getByRole('radio', { name: 'Transport met kraan' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Hijswerk op locatie' })).not.toBeChecked()
    // Transport met kraan: the classic laden + lossen pair and the goods section.
    expect(stopCards()).toHaveLength(2)
    expect(screen.getByRole('heading', { name: 'Goederen' })).toBeInTheDocument()
  })

  it('a type without the flag submits no crane-job fields at all', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await user.type(screen.getAllByLabelText(/Plaats/)[0], 'Antwerpen')
    await user.type(screen.getAllByLabelText(/Plaats/)[1], 'Gent')
    await user.type(screen.getByLabelText('Omschrijving'), 'Paletten')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    expect(createOrder.mock.calls[0][0]).not.toHaveProperty('craneJobKind')
    expect(createOrder.mock.calls[0][0]).not.toHaveProperty('workDescription')
  })

  it('Hijswerk op locatie: ONE werfadres with the same address fields, no goods section, no laad/los stops', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Speciaal werk/ }))
    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))

    const cards = stopCards()
    expect(cards).toHaveLength(1)
    const site = cards[0]
    expect(within(site).getByText('Werfadres')).toBeInTheDocument()
    for (const label of ['locatie', 'Naam locatie', 'Straat + nr', 'Postcode', /Plaats/, 'Land', 'Van', 'Tot']) {
      expect(within(site).getByLabelText(label)).toBeInTheDocument()
    }
    // A time requirement ("leveren vóór 08:00") is a loading/unloading concept — not on a work site.
    expect(within(site).queryByLabelText('Tijdseis')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Goederen' })).not.toBeInTheDocument()
    expect(screen.getByLabelText(/Omschrijving werkzaamheden/)).toBeInTheDocument()
    expect(screen.getByText(/last die gehesen wordt/)).toBeInTheDocument()
  })

  it('submits an on-site job with a Site stop, the description and NO cargo; the end follows start + duration', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Speciaal werk/ }))
    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))

    const site = stopCards()[0]
    await user.type(within(site).getByLabelText('Naam locatie'), 'Werf Dupont')
    await user.type(within(site).getByLabelText('Straat + nr'), 'Avenue Sabin 1')
    await user.type(within(site).getByLabelText(/Plaats/), 'Waver')
    await user.type(within(site).getByLabelText('Datum werkzaamheden'), '2026-09-22')
    await user.type(within(site).getByLabelText('Van'), '22:00')
    await user.type(screen.getByLabelText('Geplande duur (uren)'), '4')
    await user.type(screen.getByLabelText(/Omschrijving werkzaamheden/), 'Airco-unit op dak plaatsen')

    // 22:00 + 4 u = 02:00 the next day — shown before saving.
    expect(within(site).getByLabelText('Tot')).toHaveValue('02:00')
    expect(within(site).getByText(/Eindigt op 23-09-2026/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1))
    const payload = createOrder.mock.calls[0][0]
    expect(payload).toMatchObject({
      activityTypeId: 'at-lift',
      activityDurationHours: 4,
      craneJobKind: 'OnSiteLifting',
      workDescription: 'Airco-unit op dak plaatsen',
      cargoItems: [],
      goodsDescription: null,
      quantity: null,
    })
    expect(payload.stops).toHaveLength(1)
    expect(payload.stops[0]).toMatchObject({
      stopType: 'Site', locationId: null, locationName: 'Werf Dupont', address: 'Avenue Sabin 1', city: 'Waver',
      plannedToIsManual: false, timeRequirement: 'None',
      // Tenant wall clock (Europe/Amsterdam, summer) → UTC on the wire; the end is on the NEXT day.
      plannedFrom: '2026-09-22T20:00:00Z', plannedTo: '2026-09-23T00:00:00Z',
    })
    expect(createFast).not.toHaveBeenCalled()
    expect(toast.showSuccess).toHaveBeenCalledTimes(1)
  })

  it('a typed Tot becomes manual, survives a start change, and "Automatisch berekenen" returns to automatic', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Speciaal werk/ }))
    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))
    const site = stopCards()[0]
    await user.type(within(site).getByLabelText('Datum werkzaamheden'), '2026-09-22')
    await user.type(within(site).getByLabelText('Van'), '08:00')
    await user.type(screen.getByLabelText('Geplande duur (uren)'), '1.5')
    expect(within(site).getByLabelText('Tot')).toHaveValue('09:30')

    await user.clear(within(site).getByLabelText('Tot'))
    await user.type(within(site).getByLabelText('Tot'), '15:00')
    await user.tab()
    expect(within(site).getByText('Einde handmatig ingesteld.')).toBeInTheDocument()

    await user.clear(within(site).getByLabelText('Van'))
    await user.type(within(site).getByLabelText('Van'), '09:00')
    await user.tab()
    expect(within(site).getByLabelText('Tot')).toHaveValue('15:00')

    await user.click(within(site).getByRole('button', { name: 'Automatisch berekenen' }))
    expect(within(site).getByLabelText('Tot')).toHaveValue('10:30')
  })

  it('a missing description blocks the save — nothing is created and no success toast is shown', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Speciaal werk/ }))
    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))
    await user.type(within(stopCards()[0]).getByLabelText(/Plaats/), 'Waver')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))

    expect((await screen.findAllByText('Beschrijf de werkzaamheden op locatie.')).length).toBeGreaterThan(0)
    expect(createOrder).not.toHaveBeenCalled()
    expect(createFast).not.toHaveBeenCalled()
    expect(toast.showSuccess).not.toHaveBeenCalled()
    // No goods error for a job that moves no goods.
    expect(screen.queryByText(/goederen/i, { selector: '[role="alert"] *' })).not.toBeInTheDocument()
  })

  it('a missing work-site location blocks too; switching back keeps the laden/lossen data untouched', async () => {
    const user = userEvent.setup()
    renderPage()
    await pickCustomer(user)
    await user.click(await screen.findByRole('radio', { name: /Speciaal werk/ }))
    await user.type(screen.getAllByLabelText(/Plaats/)[0], 'Antwerpen')

    await user.click(screen.getByRole('radio', { name: 'Hijswerk op locatie' }))
    await user.type(screen.getByLabelText(/Omschrijving werkzaamheden/), 'Hijsen')
    await user.click(screen.getByRole('button', { name: 'Dossier aanmaken' }))
    expect((await screen.findAllByText(/Kies een bestaand adres of vul een plaats of adres in voor de werf/)).length).toBeGreaterThan(0)
    expect(createOrder).not.toHaveBeenCalled()

    await user.click(screen.getByRole('radio', { name: 'Transport met kraan' }))
    expect(stopCards()).toHaveLength(2)
    expect(screen.getAllByLabelText(/Plaats/)[0]).toHaveValue('Antwerpen')
  })
})
