import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'
import type { DossierDetail, ReadinessIssue } from '../types'
import type { TransportOrderDetail, TransportOrderInput } from '../../transport-orders/types'

/**
 * UX-sprint 2026-09-09 — the dossier as an operational work surface: contextual Attention
 * actions, inline route entry (ordered stops, id-preserving save), inline pricing (agreed price
 * = one-off agreement, free sales lines) and honest "no price yet" vs. zero.
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
}))
vi.mock('../api/dossiersApi', () => api)
vi.mock('../api/activityTypesApi', () => ({ listActivityTypes: () => Promise.resolve([]) }))

const orders = vi.hoisted(() => ({ get: vi.fn(), update: vi.fn(), saveLines: vi.fn(), setOneOff: vi.fn() }))
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: orders.get,
  updateTransportOrder: orders.update,
  saveOrderPriceLines: orders.saveLines,
  setOrderOneOffPrice: orders.setOneOff,
  searchTransportOrders: () => Promise.resolve({ items: [], totalCount: 0 }),
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [], totalCount: 0 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../users/api/usersApi', () => ({ getUsers: () => Promise.resolve([]) }))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
const lookup = vi.hoisted(() => ({ options: [] as { id: string; code: string; name: string }[] }))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: lookup.options, isLoading: false, error: null }),
}))
vi.mock('../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id, onChange }: { id?: string; onChange: (v: string) => void }) => (
    <input id={id} aria-label="locatie" onChange={(e) => onChange(e.target.value)} />
  ),
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

/**
 * Redesign 2026-09-11: the dossier is a navigation-based workspace (`/dossiers/:id/:section?`),
 * ONE subsection mounted at a time. Every test renders on the tab it exercises; flows that span
 * route AND price move between tabs through the attention links or the subnav.
 */
function renderPage(initialPath = '/dossiers/d-1') {
  const router = createMemoryRouter([{ path: '/dossiers/:id/:section?', element: <DossierDetailPage /> }], {
    initialEntries: [initialPath],
  })
  return { ...render(<RouterProvider router={router} />), router }
}

const ROUTE_TAB = '/dossiers/d-1/route'
const PRICE_TAB = '/dossiers/d-1/prijs'

/** The in-dossier subnav link for a tab (real links; the overview cards carry "Open …" links of their own). */
function subnavLink(name: string) {
  return within(screen.getByRole('navigation', { name: 'Dossieronderdelen' })).getByRole('link', { name })
}

const issue = (code: string, section: ReadinessIssue['section'], field: string | null, message: string): ReadinessIssue => ({
  code, severity: 'Warning', message, section, field, stage: 'Planning',
})

/** Direct transport with a draft order that has ONE loading stop, no date on it, no price. */
function unfinishedDossier(): DossierDetail {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: 0 }],
    financials: { agreedOrderTotal: 0, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0, pricedOrderCount: 0 },
    activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' })],
    readiness: [
      issue('order.confirm.stops', 'route', 'stops.unloading', 'ORD-0001: laad- en loslocatie zijn nog onbekend.'),
      issue('route.date_missing', 'route', 'stops.plannedFrom', 'ORD-0001: nog geen planningsdatum.'),
      issue('pricing.missing', 'prijs', 'price', 'ORD-0001: nog geen verkoopprijs.'),
    ],
  })
}

function draftOrder() {
  return orderDetail({
    status: 'Draft',
    agreedPrice: 0,
    stops: [{ ...orderDetail().stops[0], plannedFrom: null, plannedTo: null }],
  })
}

describe('Dossier work surface', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(unfinishedDossier())
    orders.get.mockResolvedValue(draftOrder())
  })

  it('the overview goods card shows the unit NAME from the catalogue, never the raw code', async () => {
    lookup.options = [{ id: 'u-ep', code: 'EUROPALLET', name: 'Europallet (EP)' }]
    try {
      renderPage()
      expect(await screen.findByText('2 × Europallet (EP)')).toBeInTheDocument()
      expect(screen.queryByText(/2 × EUROPALLET/)).not.toBeInTheDocument()
    } finally {
      lookup.options = []
    }
  })

  it('shows "Nog geen prijs" instead of € 0,00 for an unpriced dossier, and the amount once priced', async () => {
    const first = renderPage(PRICE_TAB)
    expect(await screen.findByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(document.getElementById('sectie-prijs')!).queryByText(/€\s0,00/)).not.toBeInTheDocument()
    first.unmount()

    const priced = unfinishedDossier()
    priced.financials = { ...priced.financials, agreedOrderTotal: 450, pricedOrderCount: 1 }
    priced.orders[0].agreedPrice = 450
    priced.readiness = []
    api.getDossier.mockResolvedValue(priced)
    renderPage(PRICE_TAB)
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    expect(await within(document.getElementById('sectie-prijs')!).findByText(/€\s450,00/)).toBeInTheDocument()
    expect(screen.queryByText('Nog geen prijs')).not.toBeInTheDocument()
  })

  it('"Ga naar route" focuses the first unresolved route field: the unloading location, then the date', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    const buttons = await screen.findAllByRole('button', { name: 'Ga naar route' })
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await user.click(buttons[0]) // stops.unloading
    const locationInputs = screen.getAllByLabelText('locatie')
    expect(locationInputs).toHaveLength(2)
    expect(document.activeElement).toBe(locationInputs[1])
    const unloadCard = locationInputs[1].closest('fieldset')!
    expect(within(unloadCard).getByText('2. Lossen')).toBeInTheDocument()

    await user.click(buttons[1]) // stops.plannedFrom → first stop without a date
    expect((document.activeElement as HTMLInputElement).type).toBe('date')
    expect(document.getElementById('sectie-route')?.hasAttribute('data-highlight')).toBe(true)
  })

  it('saves the route inline: keeps the existing stop id + version, adds the unloading stop, refreshes readiness', async () => {
    const user = userEvent.setup()
    const saved = orderDetail({
      status: 'Draft',
      version: 'ov-2',
      stops: [
        draftOrder().stops[0],
        { ...draftOrder().stops[0], id: 's-2', sequence: 2, stopType: 'Unloading', locationName: '', address: null, postalCode: null, city: 'Gent' },
      ],
    })
    orders.update.mockResolvedValue(saved)
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')

    expect(screen.getByRole('button', { name: 'Route opslaan' })).toBeDisabled()
    const unloadCard = screen.getAllByLabelText('locatie')[1].closest('fieldset')!
    await user.type(within(unloadCard).getByLabelText(/Plaats/), 'Gent')
    expect(screen.getByText('Niet-opgeslagen wijzigingen')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))

    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    const [orderId, payload] = orders.update.mock.calls[0] as [string, TransportOrderInput]
    expect(orderId).toBe('o-1')
    expect(payload.version).toBe('ov-1')
    expect(payload.stops).toHaveLength(2)
    expect(payload.stops[0].id).toBe('s-1')
    expect(payload.stops[0].stopType).toBe('Loading')
    expect(payload.stops[1]).toMatchObject({ id: null, stopType: 'Unloading', city: 'Gent' })
    // Fresh order + dossier re-read (readiness, totals) after the save.
    await waitFor(() => expect(api.getDossier).toHaveBeenCalledTimes(2))
    expect(screen.queryByText('Niet-opgeslagen wijzigingen')).not.toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalledWith('Route opgeslagen.')
  })

  it('adds extra ordered stops and lets the planner reorder them', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: '+ Extra laadstop' }))
    await user.click(screen.getByRole('button', { name: '+ Extra losstop' }))
    const legends = () => screen.getAllByRole('group').map((g) => g.querySelector('legend')?.textContent?.trim()).filter(Boolean)
    expect(legends()).toEqual(['1. Laden', '2. Lossen', '3. Laden', '4. Lossen'])

    await user.click(screen.getByRole('button', { name: 'Stop 3 omhoog' }))
    expect(legends()).toEqual(['1. Laden', '2. Laden', '3. Lossen', '4. Lossen'])
  })

  it('"Ga naar prijs" focuses the agreed-price field; saving it stores a one-off price agreement on the order', async () => {
    const user = userEvent.setup()
    orders.setOneOff.mockResolvedValue(orderDetail({ status: 'Draft', pricingSource: 'OneOff', oneOffFixedAmount: 450, agreedPrice: 450, version: 'ov-2' }))
    // Starts on the overview (order already loaded there): the attention link opens the price tab.
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    const agreed = await screen.findByLabelText('Afgesproken prijs (€)')
    await waitFor(() => expect(document.activeElement).toBe(agreed))

    await user.type(agreed, '450')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    // Price-only command (hardening 2026-09-11): the route is never sent along, so an incomplete
    // route (this order has no unloading stop) can never block a valid price.
    await waitFor(() => expect(orders.setOneOff).toHaveBeenCalledTimes(1))
    expect(orders.setOneOff).toHaveBeenCalledWith('o-1', { fixedAmount: 450, version: 'ov-1' })
    expect(orders.update).not.toHaveBeenCalled()
    await waitFor(() => expect(api.getDossier).toHaveBeenCalledTimes(2))
  })

  it('clearing the agreed price removes the price source again', async () => {
    const user = userEvent.setup()
    orders.get.mockResolvedValue(orderDetail({ status: 'Draft', pricingSource: 'OneOff', oneOffFixedAmount: 450, agreedPrice: 450 }))
    orders.setOneOff.mockResolvedValue(orderDetail({ status: 'Draft', agreedPrice: null, version: 'ov-2' }))
    renderPage(PRICE_TAB)
    const agreed = await screen.findByLabelText('Afgesproken prijs (€)')
    expect(agreed).toHaveValue('450')

    await user.clear(agreed)
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(orders.setOneOff).toHaveBeenCalledTimes(1))
    expect(orders.setOneOff).toHaveBeenCalledWith('o-1', { fixedAmount: null, version: 'ov-1' })
    expect(orders.update).not.toHaveBeenCalled()
  })

  it('adds a free sales line inline through the pricing-lines endpoint', async () => {
    const user = userEvent.setup()
    orders.saveLines.mockResolvedValue(
      orderDetail({
        status: 'Draft',
        agreedPrice: 450,
        version: 'ov-2',
        pricingLines: [{ label: 'Transport', amount: 450, source: 'Manueel', informational: false, kind: 'Manual', quantity: 1, unitPrice: 450, lineKey: 'manual:1' }],
      }),
    )
    renderPage(PRICE_TAB)
    await screen.findByText('Nog geen verkooplijnen.')

    await user.click(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' }))
    await user.type(screen.getByLabelText(/Omschrijving/), 'Transport')
    await user.clear(screen.getByLabelText('Aantal'))
    await user.type(screen.getByLabelText('Aantal'), '1')
    await user.type(screen.getByLabelText('Eenheidsprijs (€)'), '450')
    await user.click(screen.getByRole('button', { name: 'Toevoegen' }))

    await waitFor(() => expect(orders.saveLines).toHaveBeenCalledTimes(1))
    expect(orders.saveLines.mock.calls[0][0]).toBe('o-1')
    expect(orders.saveLines.mock.calls[0][1]).toEqual([
      { lineKey: null, label: 'Transport', quantity: 1, unitPrice: 450, amount: null, adjustReason: null, unit: null },
    ])
    expect(await screen.findByRole('cell', { name: 'Transport' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Verwijderen' })).toBeInTheDocument()
  })

  it('without an order the price section offers the create-order action and "Ga naar prijs" focuses it', async () => {
    const user = userEvent.setup()
    const dossier = dossierDetail({
      activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: null })],
      readiness: [{ code: 'pricing.none', severity: 'Info', message: 'Nog geen verkooplijnen of prijs.', section: 'prijs', field: 'price', stage: 'Commercial' }],
    })
    api.getDossier.mockResolvedValue(dossier)
    const created = dossierDetail({
      activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' })],
      orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: null }],
      version: 'v-2',
    })
    api.createOrderForActivity.mockResolvedValue(created)
    const { router } = renderPage()

    await user.click(await screen.findByRole('button', { name: 'Ga naar prijs' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    const createButton = await screen.findByRole('button', { name: 'Transportopdracht aanmaken' })
    await waitFor(() => expect(document.activeElement).toBe(createButton))

    await user.click(createButton)
    await waitFor(() => expect(api.createOrderForActivity).toHaveBeenCalledWith('d-1', 'a-1', 'v-1'))
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toBeInTheDocument()
  })

  it('renders the route read-only without order-edit rights', async () => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    renderPage(ROUTE_TAB)
    expect(await screen.findByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Route opslaan' })).not.toBeInTheDocument()
    expect(screen.getByText('Je kunt de route bekijken maar niet bewerken.')).toBeInTheDocument()
  })
})

// ---------------------------------------------------------------- hardening 2026-09-10

/** Two transport activities with their own orders: ORD-0001 priced (one-off € 450), ORD-0002 a bare draft. */
function twoOrderDossier(): DossierDetail {
  return dossierDetail({
    orders: [
      { linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: 'Pallets', agreedPrice: 450, isPriced: true },
      { linkId: 'l-2', orderId: 'o-2', orderNumber: 'ORD-0002', orderDate: '2026-08-13', status: 'Draft', goodsDescription: null, agreedPrice: 0, isPriced: false },
    ],
    financials: {
      agreedOrderTotal: 450, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0,
      pricedOrderCount: 1, billableActivityCount: 2, pricedActivityCount: 1, zeroPricedActivityCount: 0,
    },
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 450 }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'EXPRESS', activityTypeName: 'Express', linkedTransportOrderId: 'o-2', linkedOrderNumber: 'ORD-0002', linkedOrderStatus: 'Draft', agreedPrice: 0 }),
    ],
    readiness: [
      { ...issue('order.confirm.stops', 'route', 'stops.unloading', 'ORD-0002: loslocatie is nog onbekend.'), transportOrderId: 'o-2' },
      { ...issue('pricing.missing', 'prijs', 'price', 'ORD-0002: nog geen verkoopprijs.'), transportOrderId: 'o-2' },
    ],
  })
}

function firstOrder() {
  return orderDetail({ id: 'o-1', orderNumber: 'ORD-0001', status: 'Draft', pricingSource: 'OneOff', oneOffFixedAmount: 450, agreedPrice: 450 })
}

function secondOrder() {
  return orderDetail({
    id: 'o-2', orderNumber: 'ORD-0002', status: 'Draft', agreedPrice: 0, version: 'ov-2',
    stops: [{ ...orderDetail().stops[0], id: 's-9', locationName: 'Depot Gent', city: 'Gent', plannedFrom: null, plannedTo: null }],
  })
}

describe('Dossier work surface — several transport orders', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(twoOrderDossier())
    orders.get.mockImplementation((id: string) => Promise.resolve(id === 'o-2' ? secondOrder() : firstOrder()))
  })

  it('makes the target order explicit: a switcher in route and price, defaulting to the first order', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')

    // Route switches between orders ("Opdracht"); price switches between billable units ("Eenheid").
    const routeSwitch = screen.getByRole('group', { name: 'Opdracht' })
    expect(within(routeSwitch).getByRole('button', { name: /ORD-0001/ })).toHaveAttribute('aria-pressed', 'true')
    expect(within(routeSwitch).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'false')

    await user.click(subnavLink('Verkoop & prijs'))
    const priceSwitch = await screen.findByRole('group', { name: 'Eenheid' })
    expect(within(priceSwitch).getByRole('button', { name: /ORD-0001/ })).toHaveAttribute('aria-pressed', 'true')
    expect(orders.get).toHaveBeenCalledWith('o-1')
    expect(orders.get).not.toHaveBeenCalledWith('o-2')
  })

  it('never presents a partial aggregate as the dossier price: € 450 is marked "1 van 2 activiteiten geprijsd"', async () => {
    renderPage(PRICE_TAB)
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    const total = price.querySelector('.dossier-price-total')!
    expect(await within(total as HTMLElement).findByText(/€\s450,00/)).toBeInTheDocument()
    expect(within(total as HTMLElement).getByText('1 van 2 activiteiten geprijsd')).toBeInTheDocument()
    // Per-unit lines follow the backend flag, not "amount > 0".
    const rows = within(price).getAllByRole('listitem')
    expect(rows[0]).toHaveTextContent('ORD-0001 — Direct transport')
    expect(rows[0]).toHaveTextContent(/€\s450,00/)
    expect(rows[1]).toHaveTextContent('ORD-0002 — Express')
    expect(rows[1]).toHaveTextContent('Nog niet geprijsd')
  })

  it('switching the target loads that order into the editors and saves to it', async () => {
    const user = userEvent.setup()
    orders.update.mockResolvedValue(secondOrder())
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await user.click(within(screen.getByRole('group', { name: 'Opdracht' })).getByRole('button', { name: /ORD-0002/ }))
    await waitFor(() => expect(orders.get).toHaveBeenCalledWith('o-2'))
    await screen.findByDisplayValue('Depot Gent')
    expect(screen.queryByDisplayValue('Nexans site Antwerpen')).not.toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Opdracht' })).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'true')
    // The selection lives in the shell: the price tab reflects it and shows ORD-0002's agreed price (empty).
    await user.click(subnavLink('Verkoop & prijs'))
    const units = await screen.findByRole('group', { name: 'Eenheid' })
    expect(within(units).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'true')
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toHaveValue('')
    // Back to the route: still ORD-0002, nothing reloaded.
    await user.click(subnavLink('Route'))
    await screen.findByDisplayValue('Depot Gent')
    expect(orders.get).toHaveBeenCalledTimes(2)

    const unloadCard = screen.getAllByLabelText('locatie')[1].closest('fieldset')!
    await user.type(within(unloadCard).getByLabelText(/Plaats/), 'Brussel')
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    const [orderId, payload] = orders.update.mock.calls[0] as [string, TransportOrderInput]
    expect(orderId).toBe('o-2')
    expect(payload.version).toBe('ov-2')
    expect(payload.stops[0].id).toBe('s-9')
  })

  it('an attention action for the second order selects that order first, then focuses the field', async () => {
    const user = userEvent.setup()
    // Overview → "Ga naar route" (route tab) → "Ga naar prijs" (price tab): one flow across tabs.
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar route' }))
    expect(router.state.location.pathname).toBe(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    const routeSwitch = screen.getByRole('group', { name: 'Opdracht' })
    expect(within(routeSwitch).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'true')
    // stops.unloading → the (still empty) unloading location of ORD-0002.
    await waitFor(() => expect(document.activeElement).toBe(screen.getAllByLabelText('locatie')[1]))

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    const agreed = await screen.findByLabelText('Afgesproken prijs (€)')
    await waitFor(() => expect(document.activeElement).toBe(agreed))
    expect(screen.getByText('Opdracht ORD-0002')).toBeInTheDocument()
  })

  it('locks the switcher while the route has unsaved changes, so edits can never land on another order', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')

    const unloadCard = screen.getAllByLabelText('locatie')[1].closest('fieldset')!
    await user.type(within(unloadCard).getByLabelText(/Plaats/), 'Gent')
    expect(within(screen.getByRole('group', { name: 'Opdracht' })).getByRole('button', { name: /ORD-0002/ })).toBeDisabled()
    expect(screen.getAllByText('Sla de route op of maak de wijzigingen ongedaan om van opdracht te wisselen.').length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Wijzigingen ongedaan maken' }))
    expect(within(screen.getByRole('group', { name: 'Opdracht' })).getByRole('button', { name: /ORD-0002/ })).toBeEnabled()
    expect(orders.get).not.toHaveBeenCalledWith('o-2')
  })
})

describe('Dossier work surface — failure paths of the inline route save', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('create succeeds, stop save fails: the retry re-uses the created order and keeps the entered route', async () => {
    const user = userEvent.setup()
    const noOrder = dossierDetail({
      activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: null })],
      readiness: [{ ...issue('route.order_missing', 'route', 'stops.loading', 'Direct transport: route en goederen zijn nog niet ingevuld.'), activityId: 'a-1' }],
    })
    const linked = dossierDetail({
      version: 'v-2',
      activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' })],
      orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: null }],
    })
    api.getDossier.mockResolvedValue(noOrder)
    api.createOrderForActivity.mockResolvedValue(linked)
    const created = orderDetail({ status: 'Draft', stops: [], cargoItems: [], version: 'ov-1' })
    orders.get.mockResolvedValue(created)
    orders.update
      .mockRejectedValueOnce(new ApiError('Interne fout', 500, { title: 'Interne fout' }))
      .mockResolvedValueOnce(orderDetail({ status: 'Draft', version: 'ov-2' }))
    renderPage(ROUTE_TAB)

    const cards = await screen.findAllByLabelText('locatie')
    expect(cards).toHaveLength(2)
    await user.type(within(cards[0].closest('fieldset')!).getByLabelText(/Plaats/), 'Antwerpen')
    await user.type(within(cards[1].closest('fieldset')!).getByLabelText(/Plaats/), 'Gent')
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))

    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    expect(api.createOrderForActivity).toHaveBeenCalledTimes(1)
    expect(await screen.findByRole('alert')).toHaveTextContent('Interne fout')
    // The planner's route is still there — nothing was reset by the dossier/order refresh.
    await waitFor(() => expect(screen.getByDisplayValue('Antwerpen')).toBeInTheDocument())
    expect(screen.getByDisplayValue('Gent')).toBeInTheDocument()
    expect(screen.getByText('Niet-opgeslagen wijzigingen')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(2))
    // Retry PUTs the SAME order; no second transport order is created.
    expect(api.createOrderForActivity).toHaveBeenCalledTimes(1)
    expect((orders.update.mock.calls[1] as [string, TransportOrderInput])[0]).toBe('o-1')
    expect((orders.update.mock.calls[1] as [string, TransportOrderInput])[1].stops.map((s) => s.city)).toEqual(['Antwerpen', 'Gent'])
    expect(toast.showSuccess).toHaveBeenCalledWith('Route opgeslagen.')
  })

  it('create succeeds but the follow-up order load fails: the retry loads the linked order instead of creating again', async () => {
    const user = userEvent.setup()
    const noOrder = dossierDetail({ activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: null })] })
    const linked = dossierDetail({
      version: 'v-2',
      activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' })],
      orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: null }],
    })
    api.getDossier.mockResolvedValue(noOrder)
    api.createOrderForActivity.mockResolvedValue(linked)
    const created = orderDetail({ status: 'Draft', stops: [], cargoItems: [], version: 'ov-1' })
    orders.get.mockRejectedValueOnce(new ApiError('Netwerk', 0)).mockResolvedValue(created)
    orders.update.mockResolvedValue(orderDetail({ status: 'Draft', version: 'ov-2' }))
    renderPage(ROUTE_TAB)

    const cards = await screen.findAllByLabelText('locatie')
    await user.type(within(cards[0].closest('fieldset')!).getByLabelText(/Plaats/), 'Antwerpen')
    await user.type(within(cards[1].closest('fieldset')!).getByLabelText(/Plaats/), 'Gent')
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(api.createOrderForActivity).toHaveBeenCalledTimes(1))
    // First attempt could not load the created order → no PUT, error shown, route kept.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Route opslaan' })).toBeEnabled())
    expect(orders.update).not.toHaveBeenCalled()
    expect(screen.getByDisplayValue('Antwerpen')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    expect(api.createOrderForActivity).toHaveBeenCalledTimes(1)
    expect((orders.update.mock.calls[0] as [string, TransportOrderInput])[0]).toBe('o-1')
  })

  it('409 on save: the entered route stays, the banner is explicit, and Herladen adopts the authoritative state', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(unfinishedDossier())
    orders.get.mockResolvedValue(draftOrder())
    const colleague = orderDetail({
      status: 'Draft',
      version: 'ov-5',
      stops: [
        draftOrder().stops[0],
        { ...draftOrder().stops[0], id: 's-7', sequence: 2, stopType: 'Unloading', locationName: 'Magazijn Hasselt', city: 'Hasselt' },
      ],
    })
    orders.update.mockRejectedValueOnce(new ApiError('Conflict', 409, colleague)).mockResolvedValueOnce(orderDetail({ status: 'Draft', version: 'ov-6' }))
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')

    const unloadCard = screen.getAllByLabelText('locatie')[1].closest('fieldset')!
    await user.type(within(unloadCard).getByLabelText(/Plaats/), 'Gent')
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))

    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('gewijzigd door een collega')
    // Unsaved input is still on screen, nothing was silently replaced.
    expect(screen.getByDisplayValue('Gent')).toBeInTheDocument()
    expect(screen.getByText('Niet-opgeslagen wijzigingen')).toBeInTheDocument()
    expect(screen.queryByDisplayValue('Magazijn Hasselt')).not.toBeInTheDocument()

    await user.click(within(banner).getByRole('button', { name: 'Herladen' }))
    expect(await screen.findByDisplayValue('Magazijn Hasselt')).toBeInTheDocument()
    expect(screen.queryByDisplayValue('Gent')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Route opslaan' })).toBeDisabled()

    // Editing again after the reload saves against the colleague's version.
    await user.type(within(screen.getAllByLabelText('locatie')[1].closest('fieldset')!).getByLabelText(/Plaats/), 'X')
    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(2))
    expect((orders.update.mock.calls[1] as [string, TransportOrderInput])[1].version).toBe('ov-5')
  })
})

// ---------------------------------------------------------------- stap 13 (2026-09-11): activity pricing

const noFinancials = { agreedOrderTotal: 0, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0, pricedOrderCount: 0 }

/** Opslag (storage) as the only activity; unpriced. */
function storageOnlyDossier(): DossierDetail {
  return dossierDetail({
    financials: { ...noFinancials, billableActivityCount: 1, pricedActivityCount: 0, zeroPricedActivityCount: 0 },
    activities: [dossierActivity({ id: 'a-1', activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', icon: 'warehouse', hasStops: false, supportsGoods: false })],
    readiness: [{ ...issue('pricing.missing', 'prijs', 'price', 'Nog geen verkoopprijs voor Opslag.'), activityId: 'a-1' }],
  })
}

/** Direct transport (ORD-0001 € 450) + Opslag (€ 200) + Kraanwerk (unpriced): 2 of 3 units priced. */
function mixedDossier(): DossierDetail {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: 'Pallets', agreedPrice: 450, isPriced: true }],
    financials: { agreedOrderTotal: 650, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0, pricedOrderCount: 1, billableActivityCount: 3, pricedActivityCount: 2, zeroPricedActivityCount: 0 },
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 450 }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', hasStops: false, supportsGoods: false, pricingSource: 'OneOff', isPriced: true, agreedPrice: 200, pricingStatus: 'Draft', pricingVersion: 'pv-2' }),
      dossierActivity({ id: 'a-3', sequence: 3, activityTypeCode: 'KRAANWERK', activityTypeName: 'Kraanwerk', hasStops: false, supportsGoods: false, allowsDuration: true }),
    ],
    readiness: [{ ...issue('pricing.missing', 'prijs', 'price', 'Nog geen verkoopprijs voor Kraanwerk.'), activityId: 'a-3' }],
  })
}

describe('Dossier work surface — standalone billable activities carry their own price', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.price', 'orders.edit', 'orders.override_price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    orders.get.mockResolvedValue(firstOrder())
  })

  it('a storage-only dossier shows the activity price editor and never "Transportopdracht aanmaken"', async () => {
    api.getDossier.mockResolvedValue(storageOnlyDossier())
    renderPage(PRICE_TAB)
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    expect(await within(price).findByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(price).getByText('Activiteit Opslag')).toBeInTheDocument()
    expect(within(price).getByLabelText('Afgesproken prijs (€)')).toHaveValue('')
    expect(within(price).queryByRole('button', { name: 'Transportopdracht aanmaken' })).not.toBeInTheDocument()
    expect(within(price).queryByRole('button', { name: '+ Activiteit' })).not.toBeInTheDocument()
    expect(within(price).queryByText('Voeg een transportactiviteit toe om een prijs te kunnen invoeren.')).not.toBeInTheDocument()
    expect(within(price).queryByText(/niet op het dossier geprijsd/)).not.toBeInTheDocument()
    expect(document.getElementById('sectie-route')).toBeNull()
    expect(orders.get).not.toHaveBeenCalled()
  })

  it('a crane-only dossier shows the editor as well (any standalone billable type, not only storage)', async () => {
    api.getDossier.mockResolvedValue(
      dossierDetail({
        financials: { ...noFinancials, billableActivityCount: 1, pricedActivityCount: 0, zeroPricedActivityCount: 0 },
        activities: [dossierActivity({ id: 'a-1', activityTypeCode: 'KRAANWERK', activityTypeName: 'Kraanwerk', hasStops: false, supportsGoods: false, allowsDuration: true, label: 'Werf Gent' })],
      }),
    )
    renderPage(PRICE_TAB)
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    expect(await within(price).findByText('Activiteit Kraanwerk · Werf Gent')).toBeInTheDocument()
    expect(within(price).getByLabelText('Afgesproken prijs (€)')).toBeInTheDocument()
    expect(within(price).queryByRole('button', { name: 'Transportopdracht aanmaken' })).not.toBeInTheDocument()
  })

  it('saving € 350 calls the activity price endpoint with the record version and re-renders the returned dossier', async () => {
    const user = userEvent.setup()
    const before = storageOnlyDossier()
    before.activities[0].pricingVersion = 'pv-1'
    api.getDossier.mockResolvedValue(before)
    const after = storageOnlyDossier()
    after.activities[0] = { ...after.activities[0], pricingSource: 'OneOff', isPriced: true, agreedPrice: 350, pricingStatus: 'Draft', pricingVersion: 'pv-2' }
    after.financials = { ...after.financials, agreedOrderTotal: 350, pricedActivityCount: 1 }
    after.readiness = []
    api.setActivityPrice.mockResolvedValue(after)
    renderPage(PRICE_TAB)

    const agreed = await screen.findByLabelText('Afgesproken prijs (€)')
    await user.type(agreed, '350')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.setActivityPrice).toHaveBeenCalledTimes(1))
    expect(api.setActivityPrice).toHaveBeenCalledWith('d-1', 'a-1', { fixedAmount: 350, version: 'pv-1' })
    expect(orders.setOneOff).not.toHaveBeenCalled()
    expect(toast.showSuccess).toHaveBeenCalledWith('Afgesproken prijs opgeslagen.')
    const price = document.getElementById('sectie-prijs')!
    expect(await within(price).findByText('Huidige prijsafspraak: € 350,00')).toBeInTheDocument()
    expect(within(price.querySelector('.dossier-price-total')!).getByText(/€\s350,00/)).toBeInTheDocument()
    expect(screen.queryByText('Nog geen prijs')).not.toBeInTheDocument()
    // The next save goes against the new record version.
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('350')
  })

  it('an invalid or negative amount is refused inline; an empty field clears the price', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(mixedDossier())
    api.setActivityPrice.mockResolvedValue(mixedDossier())
    renderPage(PRICE_TAB)
    await screen.findByText('Opdracht ORD-0001')
    await user.click(within(screen.getByRole('group', { name: 'Eenheid' })).getByRole('button', { name: /Opslag/ }))
    const agreed = screen.getByLabelText('Afgesproken prijs (€)')
    expect(agreed).toHaveValue('200')

    await user.clear(agreed)
    await user.type(agreed, '-5')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(await screen.findByText('Geef een geldig bedrag op (0 of meer).')).toBeInTheDocument()
    expect(api.setActivityPrice).not.toHaveBeenCalled()

    await user.clear(agreed)
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(api.setActivityPrice).toHaveBeenCalledWith('d-1', 'a-2', { fixedAmount: null, version: 'pv-2' }))
    expect(toast.showSuccess).toHaveBeenCalledWith('Afgesproken prijs verwijderd.')
  })

  it('without dossiers.price the activity price is read-only', async () => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit'])
    api.getDossier.mockResolvedValue(storageOnlyDossier())
    renderPage(PRICE_TAB)
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    expect(await within(price).findByText('Nog geen prijs voor deze activiteit')).toBeInTheDocument()
    expect(within(price).queryByLabelText('Afgesproken prijs (€)')).not.toBeInTheDocument()
  })

  it('a locked activity price is read-only with its status', async () => {
    const locked = mixedDossier()
    locked.activities[1] = { ...locked.activities[1], pricingStatus: 'Invoiced' }
    api.getDossier.mockResolvedValue(locked)
    const user = userEvent.setup()
    renderPage(PRICE_TAB)
    await screen.findByText('Opdracht ORD-0001')
    await user.click(within(screen.getByRole('group', { name: 'Eenheid' })).getByRole('button', { name: /Opslag/ }))
    expect(screen.getByText('De prijs van deze activiteit is vergrendeld (Gefactureerd).')).toBeInTheDocument()
    expect(screen.getByText('Huidige prijsafspraak: € 200,00')).toBeInTheDocument()
    expect(screen.queryByLabelText('Afgesproken prijs (€)')).not.toBeInTheDocument()
  })

  it('409 on save shows the dossier conflict banner and an inline conflict message', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(storageOnlyDossier())
    const colleague = storageOnlyDossier()
    colleague.activities[0] = { ...colleague.activities[0], pricingSource: 'OneOff', isPriced: true, agreedPrice: 99, pricingVersion: 'pv-9' }
    api.setActivityPrice.mockRejectedValueOnce(new ApiError('Conflict', 409, colleague))
    renderPage(PRICE_TAB)

    await user.type(await screen.findByLabelText('Afgesproken prijs (€)'), '350')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((a) => /gewijzigd door een collega/.test(a.textContent ?? ''))).toBe(true)
    // The entered amount is still there — nothing was silently replaced.
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('350')

    await user.click(screen.getByRole('button', { name: 'Herladen' }))
    expect(await screen.findByText('Huidige prijsafspraak: € 99,00')).toBeInTheDocument()
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('99')
  })
})

describe('Dossier work surface — mixed dossier: transport + storage + crane', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.price', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(mixedDossier())
    orders.get.mockResolvedValue(firstOrder())
  })

  it('totals over all priced units: € 650,00 with "2 van 3 activiteiten geprijsd" and one row per unit', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')
    // Only one transport activity: no route switcher on the route tab.
    expect(screen.queryByRole('group', { name: 'Opdracht' })).not.toBeInTheDocument()

    await user.click(subnavLink('Verkoop & prijs'))
    await screen.findByText('Opdracht ORD-0001')
    const price = document.getElementById('sectie-prijs')!
    const total = price.querySelector<HTMLElement>('.dossier-price-total')!
    expect(within(total).getByText(/€\s650,00/)).toBeInTheDocument()
    expect(within(total).getByText('2 van 3 activiteiten geprijsd')).toBeInTheDocument()
    const rows = within(price).getAllByRole('listitem')
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent('ORD-0001 — Direct transport')
    expect(rows[0]).toHaveTextContent(/€\s450,00/)
    expect(rows[1]).toHaveTextContent('Opslag')
    expect(rows[1]).toHaveTextContent(/€\s200,00/)
    expect(rows[2]).toHaveTextContent('Kraanwerk')
    expect(rows[2]).toHaveTextContent('Nog niet geprijsd')
    // The unit switcher lists all three units.
    const units = screen.getByRole('group', { name: 'Eenheid' })
    expect(within(units).getAllByRole('button')).toHaveLength(3)
    expect(within(units).getByRole('button', { name: /ORD-0001/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('selecting a standalone unit changes only the price target: Opslag shows € 200, Kraan an empty editor, saves go to the selected id', async () => {
    const user = userEvent.setup()
    api.setActivityPrice.mockResolvedValue(mixedDossier())
    renderPage(PRICE_TAB)
    await screen.findByText('Opdracht ORD-0001')
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toHaveValue('450')

    const units = screen.getByRole('group', { name: 'Eenheid' })
    await user.click(within(units).getByRole('button', { name: /Opslag/ }))
    expect(screen.getByText('Activiteit Opslag')).toBeInTheDocument()
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('200')
    expect(screen.getByText('Huidige prijsafspraak: € 200,00')).toBeInTheDocument()
    // No order was (re)loaded for the switch: the route target is untouched.
    expect(orders.get).toHaveBeenCalledTimes(1)

    await user.click(within(units).getByRole('button', { name: /Kraanwerk/ }))
    expect(screen.getByText('Activiteit Kraanwerk')).toBeInTheDocument()
    const agreed = screen.getByLabelText('Afgesproken prijs (€)')
    expect(agreed).toHaveValue('')
    await user.type(agreed, '75')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(api.setActivityPrice).toHaveBeenCalledTimes(1))
    expect(api.setActivityPrice).toHaveBeenCalledWith('d-1', 'a-3', { fixedAmount: 75, version: null })
    expect(api.setActivityPrice).not.toHaveBeenCalledWith('d-1', 'a-2', expect.anything())
    expect(orders.setOneOff).not.toHaveBeenCalled()

    // Back to the transport unit: the order editor with its own agreed price again.
    await user.click(within(units).getByRole('button', { name: /ORD-0001/ }))
    expect(screen.getByText('Opdracht ORD-0001')).toBeInTheDocument()
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('450')
    // The route tab still works on ORD-0001, loaded exactly once.
    await user.click(subnavLink('Route'))
    expect(await screen.findByDisplayValue('Nexans site Antwerpen')).toBeInTheDocument()
    expect(orders.get).toHaveBeenCalledTimes(1)
  })

  it('"Ga naar prijs" for the crane attention item selects Kraanwerk and focuses its agreed price', async () => {
    const user = userEvent.setup()
    renderPage(PRICE_TAB)
    await screen.findByText('Opdracht ORD-0001')

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    const units = screen.getByRole('group', { name: 'Eenheid' })
    await waitFor(() => expect(within(units).getByRole('button', { name: /Kraanwerk/ })).toHaveAttribute('aria-pressed', 'true'))
    expect(screen.getByText('Activiteit Kraanwerk')).toBeInTheDocument()
    await waitFor(() => expect(document.activeElement).toBe(document.getElementById('dossier-activity-agreed-price')))
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledTimes(1)
    // No order was loaded for the jump: a standalone unit has nothing to fetch.
    expect(orders.get).toHaveBeenCalledTimes(1)
  })
})

describe('Dossier work surface — intentional € 0 is priced, visible and questioned', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.price', 'orders.edit', 'orders.override_price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('€ 0 on a standalone activity shows € 0,00 (not "Nog geen prijs") and the local zero warning', async () => {
    const zero = storageOnlyDossier()
    zero.activities[0] = { ...zero.activities[0], pricingSource: 'OneOff', isPriced: true, agreedPrice: 0, pricingStatus: 'Draft', pricingVersion: 'pv-1' }
    zero.financials = { ...zero.financials, agreedOrderTotal: 0, pricedActivityCount: 1, zeroPricedActivityCount: 1 }
    zero.readiness = [{ ...issue('pricing.zero', 'prijs', 'price', 'Opslag heeft een verkoopprijs van € 0,00. Controleer of dit bewust is.'), activityId: 'a-1' }]
    api.getDossier.mockResolvedValue(zero)
    renderPage(PRICE_TAB)
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    expect(await within(price.querySelector<HTMLElement>('.dossier-price-total')!).findByText(/€\s0,00/)).toBeInTheDocument()
    expect(screen.queryByText('Nog geen prijs')).not.toBeInTheDocument()
    expect(within(price).getByRole('note')).toHaveTextContent('⚠ Deze activiteit heeft een verkoopprijs van € 0,00. Controleer of dit bewust is.')
    expect(within(price).getByLabelText('Afgesproken prijs (€)')).toHaveValue('0')
  })

  /** ORD-0001 with a deliberate one-off € 0. */
  function zeroOrderDossier(amount: number): DossierDetail {
    return dossierDetail({
      orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: amount, isPriced: true }],
      financials: {
        agreedOrderTotal: amount, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0,
        pricedOrderCount: 1, billableActivityCount: 1, pricedActivityCount: 1, zeroPricedActivityCount: amount === 0 ? 1 : 0,
      },
      activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: amount })],
      readiness: amount === 0
        ? [{ ...issue('pricing.zero', 'prijs', 'price', 'Opdracht ORD-0001 heeft een verkoopprijs van € 0,00. Controleer of dit bewust is.'), transportOrderId: 'o-1', activityId: 'a-1' }]
        : [],
    })
  }
  const zeroOrder = (amount: number, version = 'ov-1') =>
    orderDetail({ status: 'Draft', pricingSource: 'OneOff', oneOffFixedAmount: amount, agreedPrice: amount, version })

  it('€ 0 on an order shows € 0,00 as total plus the order zero warning; 0 → 120 removes it, 120 → 0 brings it back', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(zeroOrderDossier(0))
    orders.get.mockResolvedValue(zeroOrder(0))
    renderPage(PRICE_TAB)
    const agreed = await screen.findByLabelText('Afgesproken prijs (€)')
    const price = document.getElementById('sectie-prijs')!
    const total = price.querySelector<HTMLElement>('.dossier-price-total')!
    expect(within(total).getByText(/€\s0,00/)).toBeInTheDocument()
    expect(screen.queryByText('Nog geen prijs')).not.toBeInTheDocument()
    expect(within(price).getByRole('note')).toHaveTextContent('⚠ Deze opdracht heeft een verkoopprijs van € 0,00. Controleer of dit bewust is.')
    expect(agreed).toHaveValue('0')

    // 0 → 120: the saved order is positive and the refetched dossier agrees → warning gone.
    orders.setOneOff.mockResolvedValueOnce(zeroOrder(120, 'ov-2'))
    api.getDossier.mockResolvedValue(zeroOrderDossier(120))
    await user.clear(agreed)
    await user.type(agreed, '120')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(orders.setOneOff).toHaveBeenCalledWith('o-1', { fixedAmount: 120, version: 'ov-1' }))
    await waitFor(() => expect(within(total).getByText(/€\s120,00/)).toBeInTheDocument())
    expect(within(price).queryByRole('note')).not.toBeInTheDocument()

    // 120 → 0: a deliberate zero again → the warning is back, the amount stays visible.
    orders.setOneOff.mockResolvedValueOnce(zeroOrder(0, 'ov-3'))
    api.getDossier.mockResolvedValue(zeroOrderDossier(0))
    await user.clear(screen.getByLabelText('Afgesproken prijs (€)'))
    await user.type(screen.getByLabelText('Afgesproken prijs (€)'), '0')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(orders.setOneOff).toHaveBeenCalledWith('o-1', { fixedAmount: 0, version: 'ov-2' }))
    expect(await within(price).findByRole('note')).toHaveTextContent('⚠ Deze opdracht heeft een verkoopprijs van € 0,00.')
    await waitFor(() => expect(within(total).getByText(/€\s0,00/)).toBeInTheDocument())
    expect(screen.queryByText('Nog geen prijs')).not.toBeInTheDocument()
  })

  it('an unpriced order at engine-zero gets NO zero warning (provenance, not magnitude)', async () => {
    api.getDossier.mockResolvedValue(unfinishedDossier())
    orders.get.mockResolvedValue(draftOrder())
    renderPage(PRICE_TAB)
    // Wait for the order to be on screen: the warning must never appear, also not once loaded.
    await screen.findByLabelText('Afgesproken prijs (€)')
    expect(screen.getByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(document.getElementById('sectie-prijs')!).queryByRole('note')).not.toBeInTheDocument()
  })
})

/**
 * Interaction contract (browser-smoke 2026-09-11): switching the target order INSIDE the dossier
 * is a local context switch — the page must stay mounted, no navigation may happen and the
 * sections must not collapse while the next order loads (a collapsed page makes the browser
 * clamp the scroll position, which is what "the page jumps to the top" was). jsdom has no
 * layout, so the test proves the architectural cause: same section nodes, same location, and
 * the section body reserving its previous height for the whole loading window. Redesign
 * 2026-09-11: only the active subsection is mounted, so the reservation is proven on the route
 * body (route tab) and the price body (price tab); a target switch is local state, never a
 * navigation (same location key).
 */
describe('Dossier work surface — order switch keeps the page in place', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(twoOrderDossier())
  })

  it('does not navigate, remount or collapse the route section while the next order loads', async () => {
    const user = userEvent.setup()
    // Every element measures 480px tall: the retained height must be exactly that during loading.
    const heightSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(480)
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
    let resolveSecond: (order: TransportOrderDetail) => void = () => {}
    orders.get.mockImplementation((id: string) =>
      id === 'o-2'
        ? new Promise<TransportOrderDetail>((resolve) => {
            resolveSecond = resolve
          })
        : Promise.resolve(firstOrder()),
    )
    try {
      const { router } = renderPage(ROUTE_TAB)
      await screen.findByDisplayValue('Nexans site Antwerpen')
      const locationBefore = router.state.location
      const routeSection = document.getElementById('sectie-route')!
      // Goods and price are separate subsections now: not mounted on the route tab.
      expect(document.getElementById('sectie-goederen')).toBeNull()
      expect(document.getElementById('sectie-prijs')).toBeNull()
      const body = routeSection.querySelector<HTMLElement>('.dossier-section-body')!
      expect(body).toBeTruthy()
      expect(body.style.minHeight).toBe('')

      await user.click(within(screen.getByRole('group', { name: 'Opdracht' })).getByRole('button', { name: /ORD-0002/ }))
      await waitFor(() => expect(orders.get).toHaveBeenCalledWith('o-2'))

      // Loading window: a placeholder is shown, but inside a body that keeps its previous height.
      expect(within(routeSection).getByText('Route laden…')).toBeInTheDocument()
      expect(body.style.minHeight).toBe('480px')
      expect(body).toHaveAttribute('aria-busy', 'true')
      expect(document.getElementById('sectie-route')).toBe(routeSection)

      resolveSecond(secondOrder())
      await screen.findByDisplayValue('Depot Gent')
      // Loaded: the reservation is released, the very same nodes are still on the page, nothing navigated or scrolled.
      expect(body.style.minHeight).toBe('')
      expect(body).not.toHaveAttribute('aria-busy')
      expect(document.getElementById('sectie-route')).toBe(routeSection)
      expect(routeSection.querySelector('.dossier-section-body')).toBe(body)
      expect(router.state.location.pathname).toBe(locationBefore.pathname)
      expect(router.state.location.key).toBe(locationBefore.key)
      expect(scrollTo).not.toHaveBeenCalled()
      expect(window.HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled()
    } finally {
      heightSpy.mockRestore()
      scrollTo.mockRestore()
    }
  })

  it('switching the billable unit on the price tab is local state: same location key, price body reserved while loading', async () => {
    const user = userEvent.setup()
    const heightSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(320)
    let resolveSecond: (order: TransportOrderDetail) => void = () => {}
    orders.get.mockImplementation((id: string) =>
      id === 'o-2'
        ? new Promise<TransportOrderDetail>((resolve) => {
            resolveSecond = resolve
          })
        : Promise.resolve(firstOrder()),
    )
    try {
      const { router } = renderPage(PRICE_TAB)
      await screen.findByText('Opdracht ORD-0001')
      const locationBefore = router.state.location
      const priceSection = document.getElementById('sectie-prijs')!
      const body = priceSection.querySelector<HTMLElement>('.dossier-section-body')!
      expect(body.style.minHeight).toBe('')

      await user.click(within(screen.getByRole('group', { name: 'Eenheid' })).getByRole('button', { name: /ORD-0002/ }))
      await waitFor(() => expect(orders.get).toHaveBeenCalledWith('o-2'))
      expect(body.style.minHeight).toBe('320px')
      expect(body).toHaveAttribute('aria-busy', 'true')

      resolveSecond(secondOrder())
      await screen.findByText('Opdracht ORD-0002')
      expect(body.style.minHeight).toBe('')
      expect(document.getElementById('sectie-prijs')).toBe(priceSection)
      expect(router.state.location.pathname).toBe(PRICE_TAB)
      expect(router.state.location.key).toBe(locationBefore.key)
      expect(window.HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled()
    } finally {
      heightSpy.mockRestore()
    }
  })

  it('"Ga naar prijs" still deliberately scrolls to and focuses its destination', async () => {
    const user = userEvent.setup()
    orders.get.mockImplementation((id: string) => Promise.resolve(id === 'o-2' ? secondOrder() : firstOrder()))
    const { router } = renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')
    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    await screen.findByText('Opdracht ORD-0002')
    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText('Afgesproken prijs (€)')))
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledTimes(1)
  })
})

/** Draft order with a complete, persisted two-stop route (no scaffolding rows in the editor). */
function fullRouteOrder() {
  const base = orderDetail()
  return orderDetail({
    status: 'Draft',
    agreedPrice: 0,
    stops: [
      { ...base.stops[0], id: 's-1', plannedFrom: null, plannedTo: null },
      { ...base.stops[0], id: 's-2', sequence: 2, stopType: 'Unloading', locationName: 'Depot Gent', address: '', postalCode: '', city: 'Gent', plannedFrom: null, plannedTo: null },
    ],
  })
}

function stopCards(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>('fieldset.tof-stop'))
}

/**
 * Browser-smoke 2026-09-11: "+ Extra losstop", leave it blank, "Route opslaan" → the stop vanished
 * without a word. Contract: an untouched new stop is only dropped after explicit confirmation
 * during save; a stop holding ANY entered data is incomplete (blocks the save with the existing
 * location-or-place rule) and is never discarded; persisted stops are never omitted.
 */
describe('Dossier work surface — empty and incomplete stops in the inline route editor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(unfinishedDossier())
    orders.get.mockResolvedValue(fullRouteOrder())
    orders.update.mockImplementation((_id: string, input: TransportOrderInput) =>
      Promise.resolve({
        ...fullRouteOrder(),
        version: 'ov-2',
        stops: input.stops.map((s, i) => ({ ...fullRouteOrder().stops[0], ...s, id: s.id ?? `new-${i}`, sequence: i + 1 })),
      }),
    )
  })

  it('an untouched extra stop is not silently dropped: saving asks first, and "Terug naar route" keeps it', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    await user.click(screen.getByRole('button', { name: '+ Extra losstop' }))
    expect(stopCards()).toHaveLength(3)

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Er is 1 lege stop zonder adres. Wil je deze lege stop verwijderen en de route opslaan?')).toBeInTheDocument()
    expect(orders.update).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Terug naar route' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(stopCards()).toHaveLength(3)
    expect(orders.update).not.toHaveBeenCalled()
    expect(screen.getByText('Niet-opgeslagen wijzigingen')).toBeInTheDocument()
  })

  it('confirming removes the empty stops and saves the remaining route with every persisted stop', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    await user.click(screen.getByRole('button', { name: '+ Extra laadstop' }))
    await user.click(screen.getByRole('button', { name: '+ Extra losstop' }))
    expect(stopCards()).toHaveLength(4)

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Er zijn 2 lege stops zonder adres. Wil je deze lege stops verwijderen en de route opslaan?')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Lege stops verwijderen & opslaan' }))

    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    const [orderId, payload] = orders.update.mock.calls[0] as [string, TransportOrderInput]
    expect(orderId).toBe('o-1')
    expect(payload.stops.map((s) => s.id)).toEqual(['s-1', 's-2'])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(stopCards()).toHaveLength(2)
  })

  it('a stop with only a reference is incomplete, not empty: the save is blocked with an inline message at that stop', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    await user.click(screen.getByRole('button', { name: '+ Extra losstop' }))
    await user.type(within(stopCards()[2]).getByLabelText('Referentie'), 'REF-9')

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    expect(await within(stopCards()[2]).findByText('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(orders.update).not.toHaveBeenCalled()
    expect(stopCards()).toHaveLength(3)
    expect(within(stopCards()[2]).getByLabelText('Referentie')).toHaveValue('REF-9')
  })

  it('a stop with only a planning date is incomplete as well and stays in the editor', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    await user.click(screen.getByRole('button', { name: '+ Extra laadstop' }))
    await user.type(within(stopCards()[2]).getByLabelText('Laaddatum'), '2026-09-12')

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    expect(await within(stopCards()[2]).findByText('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(orders.update).not.toHaveBeenCalled()
    expect(stopCards()).toHaveLength(3)
  })

  it('a stop with only a city is a valid free-address stop and is saved, never classified as empty', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    await user.click(screen.getByRole('button', { name: '+ Extra losstop' }))
    await user.type(within(stopCards()[2]).getByLabelText(/Plaats/), 'Brussel')

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    await waitFor(() => expect(orders.update).toHaveBeenCalledTimes(1))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const [, payload] = orders.update.mock.calls[0] as [string, TransportOrderInput]
    expect(payload.stops).toHaveLength(3)
    expect(payload.stops[2].city).toBe('Brussel')
    expect(payload.stops[2].id).toBeNull()
  })

  it('a persisted stop is never omitted from the save, even with its address fields cleared: the save blocks on it', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    const unloadCard = stopCards()[1]
    await user.clear(within(unloadCard).getByLabelText('Naam locatie'))
    await user.clear(within(unloadCard).getByLabelText(/Plaats/))

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    expect(await within(unloadCard).findByText('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(orders.update).not.toHaveBeenCalled()
    expect(stopCards()).toHaveLength(2)
  })

  it('Verwijderen removes an untouched new stop directly but asks before removing a stop that holds data', async () => {
    const user = userEvent.setup()
    renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Depot Gent')
    await user.click(screen.getByRole('button', { name: '+ Extra losstop' }))
    await user.click(screen.getByRole('button', { name: 'Stop 3 verwijderen' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(stopCards()).toHaveLength(2)

    await user.click(screen.getByRole('button', { name: 'Stop 2 verwijderen' }))
    let dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Stop verwijderen?')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Annuleren' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(stopCards()).toHaveLength(2)
    expect(screen.getByDisplayValue('Depot Gent')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Stop 2 verwijderen' }))
    dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Stop verwijderen' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(stopCards()).toHaveLength(1)
    expect(screen.queryByDisplayValue('Depot Gent')).not.toBeInTheDocument()
  })
})
