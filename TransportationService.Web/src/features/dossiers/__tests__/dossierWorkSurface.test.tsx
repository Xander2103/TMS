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
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
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

function renderPage() {
  const router = createMemoryRouter([{ path: '/dossiers/:id', element: <DossierDetailPage /> }], {
    initialEntries: ['/dossiers/d-1'],
  })
  return { ...render(<RouterProvider router={router} />), router }
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

  it('shows "Nog geen prijs" instead of € 0,00 for an unpriced dossier, and the amount once priced', async () => {
    const first = renderPage()
    expect(await screen.findByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(document.getElementById('sectie-prijs')!).queryByText(/€\s0,00/)).not.toBeInTheDocument()
    first.unmount()

    const priced = unfinishedDossier()
    priced.financials = { ...priced.financials, agreedOrderTotal: 450, pricedOrderCount: 1 }
    priced.orders[0].agreedPrice = 450
    priced.readiness = []
    api.getDossier.mockResolvedValue(priced)
    renderPage()
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    expect(await within(document.getElementById('sectie-prijs')!).findByText(/€\s450,00/)).toBeInTheDocument()
    expect(screen.queryByText('Nog geen prijs')).not.toBeInTheDocument()
  })

  it('"Ga naar route" focuses the first unresolved route field: the unloading location, then the date', async () => {
    const user = userEvent.setup()
    renderPage()
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
    renderPage()
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
    renderPage()
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
    renderPage()
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    const agreed = screen.getByLabelText('Afgesproken prijs (€)')
    expect(document.activeElement).toBe(agreed)

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
    renderPage()
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
    renderPage()
    await screen.findByText('Nog geen verkooplijnen.')

    await user.click(screen.getByRole('button', { name: '+ Verkooplijn' }))
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
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Ga naar prijs' }))
    const createButton = screen.getByRole('button', { name: 'Transportopdracht aanmaken' })
    expect(document.activeElement).toBe(createButton)

    await user.click(createButton)
    await waitFor(() => expect(api.createOrderForActivity).toHaveBeenCalledWith('d-1', 'a-1', 'v-1'))
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toBeInTheDocument()
  })

  it('renders the route read-only without order-edit rights', async () => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    renderPage()
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
    financials: { agreedOrderTotal: 450, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0, pricedOrderCount: 1 },
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'EXPRESS', activityTypeName: 'Express', linkedTransportOrderId: 'o-2', linkedOrderNumber: 'ORD-0002', linkedOrderStatus: 'Draft' }),
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
    renderPage()
    await screen.findByDisplayValue('Nexans site Antwerpen')

    const switchers = screen.getAllByRole('group', { name: 'Opdracht' })
    expect(switchers).toHaveLength(2) // route + price
    const routeSwitch = switchers[0]
    expect(within(routeSwitch).getByRole('button', { name: /ORD-0001/ })).toHaveAttribute('aria-pressed', 'true')
    expect(within(routeSwitch).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'false')
    expect(orders.get).toHaveBeenCalledWith('o-1')
    expect(orders.get).not.toHaveBeenCalledWith('o-2')
  })

  it('never presents a partial aggregate as the dossier price: € 450 is marked "1 van 2 opdrachten geprijsd"', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    const total = price.querySelector('.dossier-price-total')!
    expect(await within(total as HTMLElement).findByText(/€\s450,00/)).toBeInTheDocument()
    expect(within(total as HTMLElement).getByText('1 van 2 opdrachten geprijsd')).toBeInTheDocument()
    // Per-order lines follow the backend flag, not "amount > 0".
    const rows = within(price).getAllByRole('listitem')
    expect(rows[0]).toHaveTextContent('ORD-0001')
    expect(rows[0]).toHaveTextContent(/€\s450,00/)
    expect(rows[1]).toHaveTextContent('ORD-0002')
    expect(rows[1]).toHaveTextContent('—')
  })

  it('switching the target loads that order into the editors and saves to it', async () => {
    const user = userEvent.setup()
    orders.update.mockResolvedValue(secondOrder())
    renderPage()
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await user.click(within(screen.getAllByRole('group', { name: 'Opdracht' })[0]).getByRole('button', { name: /ORD-0002/ }))
    await waitFor(() => expect(orders.get).toHaveBeenCalledWith('o-2'))
    await screen.findByDisplayValue('Depot Gent')
    expect(screen.queryByDisplayValue('Nexans site Antwerpen')).not.toBeInTheDocument()
    // Both switchers reflect the same selection; the price panel now shows ORD-0002's agreed price (empty).
    for (const group of screen.getAllByRole('group', { name: 'Opdracht' })) {
      expect(within(group).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'true')
    }
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('')

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
    renderPage()
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar route' }))
    await screen.findByDisplayValue('Depot Gent')
    const routeSwitch = screen.getAllByRole('group', { name: 'Opdracht' })[0]
    expect(within(routeSwitch).getByRole('button', { name: /ORD-0002/ })).toHaveAttribute('aria-pressed', 'true')
    // stops.unloading → the (still empty) unloading location of ORD-0002.
    await waitFor(() => expect(document.activeElement).toBe(screen.getAllByLabelText('locatie')[1]))

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(document.activeElement).toBe(screen.getByLabelText('Afgesproken prijs (€)'))
    expect(screen.getByText('Opdracht ORD-0002')).toBeInTheDocument()
  })

  it('locks the switcher while the route has unsaved changes, so edits can never land on another order', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByDisplayValue('Nexans site Antwerpen')

    const unloadCard = screen.getAllByLabelText('locatie')[1].closest('fieldset')!
    await user.type(within(unloadCard).getByLabelText(/Plaats/), 'Gent')
    for (const group of screen.getAllByRole('group', { name: 'Opdracht' })) {
      expect(within(group).getByRole('button', { name: /ORD-0002/ })).toBeDisabled()
    }
    expect(screen.getAllByText('Sla de route op of maak de wijzigingen ongedaan om van opdracht te wisselen.').length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Wijzigingen ongedaan maken' }))
    expect(within(screen.getAllByRole('group', { name: 'Opdracht' })[0]).getByRole('button', { name: /ORD-0002/ })).toBeEnabled()
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
    renderPage()

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
    renderPage()

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
    renderPage()
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

describe('Dossier work surface — standalone activities', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('a storage-only dossier never suggests creating a transport order to attach a price', async () => {
    api.getDossier.mockResolvedValue(
      dossierDetail({
        activities: [dossierActivity({ id: 'a-1', activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', hasStops: false, supportsGoods: false })],
        readiness: [],
      }),
    )
    renderPage()
    await screen.findByRole('heading', { name: 'Verkoop & prijs' })
    const price = document.getElementById('sectie-prijs')!
    expect(await within(price).findByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(price).getByText(/worden in deze versie niet op het dossier geprijsd/)).toBeInTheDocument()
    expect(within(price).queryByRole('button', { name: 'Transportopdracht aanmaken' })).not.toBeInTheDocument()
    expect(within(price).queryByRole('button', { name: '+ Activiteit' })).not.toBeInTheDocument()
    expect(within(price).queryByText('Voeg een transportactiviteit toe om een prijs te kunnen invoeren.')).not.toBeInTheDocument()
    expect(document.getElementById('sectie-route')).toBeNull()
  })
})

/**
 * Interaction contract (browser-smoke 2026-09-11): switching the target order INSIDE the dossier
 * is a local context switch — the page must stay mounted, no navigation may happen and the
 * sections must not collapse while the next order loads (a collapsed page makes the browser
 * clamp the scroll position, which is what "the page jumps to the top" was). jsdom has no
 * layout, so the test proves the architectural cause: same section nodes, same location, and
 * the section body reserving its previous height for the whole loading window.
 */
describe('Dossier work surface — order switch keeps the page in place', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price', 'locations.create'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(twoOrderDossier())
  })

  it('does not navigate, remount or collapse the route/goods/price sections while the next order loads', async () => {
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
      const { router } = renderPage()
      await screen.findByDisplayValue('Nexans site Antwerpen')
      const locationBefore = router.state.location
      const routeSection = document.getElementById('sectie-route')!
      const goodsSection = document.getElementById('sectie-goederen')!
      const priceSection = document.getElementById('sectie-prijs')!
      const bodies = [routeSection, goodsSection, priceSection].map((section) => section.querySelector<HTMLElement>('.dossier-section-body')!)
      expect(bodies.every(Boolean)).toBe(true)
      for (const body of bodies) expect(body.style.minHeight).toBe('')

      await user.click(within(screen.getAllByRole('group', { name: 'Opdracht' })[0]).getByRole('button', { name: /ORD-0002/ }))
      await waitFor(() => expect(orders.get).toHaveBeenCalledWith('o-2'))

      // Loading window: placeholders are shown, but inside bodies that keep their previous height.
      expect(within(routeSection).getByText('Route laden…')).toBeInTheDocument()
      for (const body of bodies) {
        expect(body.style.minHeight).toBe('480px')
        expect(body).toHaveAttribute('aria-busy', 'true')
      }
      expect(document.getElementById('sectie-route')).toBe(routeSection)
      expect(document.getElementById('sectie-prijs')).toBe(priceSection)

      resolveSecond(secondOrder())
      await screen.findByDisplayValue('Depot Gent')
      // Loaded: the reservation is released, the very same nodes are still on the page, nothing navigated or scrolled.
      for (const body of bodies) {
        expect(body.style.minHeight).toBe('')
        expect(body).not.toHaveAttribute('aria-busy')
      }
      expect(document.getElementById('sectie-route')).toBe(routeSection)
      expect(document.getElementById('sectie-goederen')).toBe(goodsSection)
      expect(document.getElementById('sectie-prijs')).toBe(priceSection)
      expect(routeSection.querySelector('.dossier-section-body')).toBe(bodies[0])
      expect(router.state.location.pathname).toBe(locationBefore.pathname)
      expect(router.state.location.key).toBe(locationBefore.key)
      expect(scrollTo).not.toHaveBeenCalled()
      expect(window.HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled()
    } finally {
      heightSpy.mockRestore()
      scrollTo.mockRestore()
    }
  })

  it('"Ga naar prijs" still deliberately scrolls to and focuses its destination', async () => {
    const user = userEvent.setup()
    orders.get.mockImplementation((id: string) => Promise.resolve(id === 'o-2' ? secondOrder() : firstOrder()))
    renderPage()
    await screen.findByDisplayValue('Nexans site Antwerpen')
    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
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
    renderPage()
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
    renderPage()
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
    renderPage()
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
    renderPage()
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
    renderPage()
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
    renderPage()
    await screen.findByDisplayValue('Depot Gent')
    const unloadCard = stopCards()[1]
    await user.clear(within(unloadCard).getByLabelText('Naam (vrij adres)'))
    await user.clear(within(unloadCard).getByLabelText(/Plaats/))

    await user.click(screen.getByRole('button', { name: 'Route opslaan' }))
    expect(await within(unloadCard).findByText('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(orders.update).not.toHaveBeenCalled()
    expect(stopCards()).toHaveLength(2)
  })

  it('Verwijderen removes an untouched new stop directly but asks before removing a stop that holds data', async () => {
    const user = userEvent.setup()
    renderPage()
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
