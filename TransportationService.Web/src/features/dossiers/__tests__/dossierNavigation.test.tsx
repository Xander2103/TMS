import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'
import type { DossierDetail, ReadinessIssue } from '../types'
import { formatDateTime } from '../../../utils/dates'

/**
 * Redesign 2026-09-11 — the dossier as a navigation-based workspace: a stable shell (header,
 * attention strip, subnav) with ONE subsection at a time selected by `/dossiers/:id/:section?`.
 * The Overzicht is a read-only summary; editing lives in the subsections. These tests prove the
 * navigation contract (deep links, back/forward, unknown segment, unsaved-changes guard),
 * the read-only overview and the attention jumps across tabs.
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
// The Documenten tab hosts the order documents panel: keep the type labels, mock the fetches.
const documents = vi.hoisted(() => ({ list: vi.fn(() => Promise.resolve([])) }))
vi.mock('../../transport-orders/api/orderDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../../transport-orders/api/orderDocumentsApi')>('../../transport-orders/api/orderDocumentsApi')
  return {
    ...actual,
    listOrderDocuments: documents.list,
    createOrderDocument: vi.fn(),
    updateOrderDocument: vi.fn(),
    deleteOrderDocument: vi.fn(),
    uploadOrderDocumentFile: vi.fn(),
    downloadOrderDocumentFile: vi.fn(),
  }
})
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

const OVERVIEW = '/dossiers/d-1'
const ROUTE_TAB = '/dossiers/d-1/route'
const PRICE_TAB = '/dossiers/d-1/prijs'
const ALL_TABS = ['Overzicht', 'Activiteiten', 'Route', 'Goederen', 'Verkoop & prijs', 'Documenten', 'Historiek']
const FULL_RIGHTS = ['dossiers.view', 'dossiers.manage', 'dossiers.price', 'orders.edit', 'orders.override_price', 'locations.create']

function renderPage(initialPath = OVERVIEW) {
  const router = createMemoryRouter([{ path: '/dossiers/:id/:section?', element: <DossierDetailPage /> }], {
    initialEntries: [initialPath],
  })
  return { ...render(<RouterProvider router={router} />), router }
}

function subnav() {
  return screen.getByRole('navigation', { name: 'Dossieronderdelen' })
}

function subnavLink(name: string) {
  return within(subnav()).getByRole('link', { name })
}

/** An overview card by its heading (article labelled by its h2). */
function card(name: string) {
  return screen.getByRole('article', { name })
}

const issue = (code: string, section: ReadinessIssue['section'], field: string | null, message: string): ReadinessIssue => ({
  code, severity: 'Warning', message, section, field, stage: 'Planning',
})

const financials = (agreedOrderTotal: number, billable: number, priced: number, zero = 0) => ({
  agreedOrderTotal, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0,
  pricedOrderCount: priced > 0 ? 1 : 0, billableActivityCount: billable, pricedActivityCount: priced, zeroPricedActivityCount: zero,
})

/**
 * Transport ORD-0001 (one-off € 450) + Opslag (unpriced): 1 of 2 units priced, 3 documents
 * (CMR + delivery note), no notes. The attention strip points at the missing unloading stop of
 * ORD-0001 and the missing price of Opslag.
 */
function overviewDossier(overrides: Partial<DossierDetail> = {}): DossierDetail {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: 'Pallets', agreedPrice: 450, isPriced: true }],
    financials: financials(450, 2, 1),
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 450 }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', icon: 'warehouse', hasStops: false, supportsGoods: false }),
    ],
    readiness: [
      { ...issue('order.confirm.stops', 'route', 'stops.unloading', 'ORD-0001: loslocatie is nog onbekend.'), transportOrderId: 'o-1', activityId: 'a-1' },
      { ...issue('pricing.missing', 'prijs', 'price', 'Nog geen verkoopprijs voor Opslag.'), activityId: 'a-2' },
    ],
    documentCount: 3,
    documentTypes: ['Cmr', 'DeliveryNote'],
    ...overrides,
  })
}

/** Direct transport with an unpriced draft order (one loading stop, no unloading stop, no date). */
function unpricedDossier(): DossierDetail {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: 0 }],
    financials: financials(0, 1, 0),
    activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' })],
    readiness: [
      { ...issue('order.confirm.stops', 'route', 'stops.unloading', 'ORD-0001: laad- en loslocatie zijn nog onbekend.'), transportOrderId: 'o-1' },
      { ...issue('pricing.missing', 'prijs', 'price', 'ORD-0001: nog geen verkoopprijs.'), transportOrderId: 'o-1' },
    ],
  })
}

/** Direct transport (ORD-0001 € 450) + Opslag (€ 200) + Kraanwerk (unpriced): the crane needs a price. */
function craneDossier(): DossierDetail {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12', status: 'Draft', goodsDescription: 'Pallets', agreedPrice: 450, isPriced: true }],
    financials: financials(650, 3, 2),
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 450 }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', hasStops: false, supportsGoods: false, pricingSource: 'OneOff', isPriced: true, agreedPrice: 200, pricingStatus: 'Draft', pricingVersion: 'pv-2' }),
      dossierActivity({ id: 'a-3', sequence: 3, activityTypeCode: 'KRAANWERK', activityTypeName: 'Kraanwerk', hasStops: false, supportsGoods: false, allowsDuration: true }),
    ],
    readiness: [{ ...issue('pricing.missing', 'prijs', 'price', 'Nog geen verkoopprijs voor Kraanwerk.'), activityId: 'a-3' }],
  })
}

/** Opslag as the only activity: no route, no goods. */
function storageOnlyDossier(): DossierDetail {
  return dossierDetail({
    financials: financials(0, 1, 0),
    activities: [dossierActivity({ id: 'a-1', activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', icon: 'warehouse', hasStops: false, supportsGoods: false })],
    readiness: [{ ...issue('pricing.missing', 'prijs', 'price', 'Nog geen verkoopprijs voor Opslag.'), activityId: 'a-1' }],
  })
}

/** ORD-0001 as a one-off € 450 with ONE planned loading stop (07:00–08:00 UTC = 09:00–10:00 tenant time) and no unloading stop. */
function pricedOrder() {
  return orderDetail({ status: 'Draft', pricingSource: 'OneOff', oneOffFixedAmount: 450, agreedPrice: 450 })
}

function draftOrder() {
  return orderDetail({ status: 'Draft', agreedPrice: 0, stops: [{ ...orderDetail().stops[0], plannedFrom: null, plannedTo: null }] })
}

describe('Dossier navigation — the Overzicht is a read-only summary', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(FULL_RIGHTS)
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(overviewDossier())
    orders.get.mockResolvedValue(pricedOrder())
  })

  it('renders summary cards only: no inputs, textareas, textboxes or save buttons anywhere on the page', async () => {
    renderPage()
    await screen.findByText('Nexans site Antwerpen')

    for (const title of ['Route', 'Activiteiten', 'Verkoop & prijs', 'Goederen', 'Documenten', 'Notities & historiek']) {
      expect(card(title)).toBeInTheDocument()
    }
    expect(document.querySelectorAll('input, textarea, select')).toHaveLength(0)
    expect(screen.queryAllByRole('textbox')).toEqual([])
    expect(screen.queryByRole('button', { name: 'Route opslaan' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Wijzigingen ongedaan maken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Opdracht' })).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Eenheid' })).not.toBeInTheDocument()
    // Only the overview is mounted: none of the editing subsections exist in the DOM.
    for (const id of ['sectie-route', 'sectie-goederen', 'sectie-prijs', 'sectie-activiteiten', 'sectie-documenten', 'sectie-notities']) {
      expect(document.getElementById(id)).toBeNull()
    }
    // Every card carries one navigation action.
    for (const action of ['Open route', 'Open activiteiten', 'Open prijsdetails', 'Open goederen', 'Open documenten', 'Open historiek']) {
      expect(screen.getAllByRole('link', { name: action }).length).toBeGreaterThanOrEqual(1)
    }
  })

  it('summarises the route of the loaded order: loading stop text, "Nog niet ingevuld" for the missing unloading stop, planned window', async () => {
    renderPage()
    const route = await screen.findByRole('article', { name: 'Route' })
    expect(await within(route).findByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(within(route).getByText('Laden')).toBeInTheDocument()
    expect(within(route).getByText('Lossen')).toBeInTheDocument()
    expect(within(route).getByText('Nog niet ingevuld')).toBeInTheDocument()
    expect(within(route).getByText('Niet ingevuld')).toBeInTheDocument()
    // The planned window of the loading stop, projected onto the tenant zone.
    expect(within(route).getAllByText('12-08 · 09:00–10:00').length).toBeGreaterThanOrEqual(1)
    expect(within(route).queryByText('Niet gepland')).not.toBeInTheDocument()
  })

  it('shows "Ingevuld" with both stops and "Niet gepland" without a planned window', async () => {
    orders.get.mockResolvedValue(
      orderDetail({
        status: 'Draft',
        stops: [
          { ...orderDetail().stops[0], plannedFrom: null, plannedTo: null },
          { ...orderDetail().stops[0], id: 's-2', sequence: 2, stopType: 'Unloading', locationName: 'Depot Gent', city: 'Gent', plannedFrom: null, plannedTo: null },
        ],
      }),
    )
    renderPage()
    const route = await screen.findByRole('article', { name: 'Route' })
    expect(await within(route).findByText('Depot Gent')).toBeInTheDocument()
    expect(within(route).getByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(within(route).getByText('Ingevuld')).toBeInTheDocument()
    expect(within(route).getByText('Niet gepland')).toBeInTheDocument()
    expect(within(route).queryByText('Nog niet ingevuld')).not.toBeInTheDocument()
  })

  it('summarises the activities: type names, "Opdracht ORD-0001" + status, "Nog niet geprijsd" for an unpriced standalone unit', async () => {
    renderPage()
    const activities = await screen.findByRole('article', { name: 'Activiteiten' })
    expect(within(activities).getByText('2 activiteiten')).toBeInTheDocument()
    expect(within(activities).getByText('Direct transport')).toBeInTheDocument()
    expect(within(activities).getByText('Opdracht ORD-0001')).toBeInTheDocument()
    expect(within(activities).getByText('Concept')).toBeInTheDocument()
    expect(within(activities).getByText('Opslag')).toBeInTheDocument()
    expect(within(activities).getByText('Nog niet geprijsd')).toBeInTheDocument()
    expect(within(activities).queryByText(/andere activiteit/)).not.toBeInTheDocument()
  })

  it('previews at most three activities and folds the rest into "+ 1 andere activiteit"', async () => {
    const many = overviewDossier()
    many.activities = [
      ...many.activities,
      dossierActivity({ id: 'a-3', sequence: 3, activityTypeCode: 'KRAANWERK', activityTypeName: 'Kraanwerk', hasStops: false, supportsGoods: false }),
      dossierActivity({ id: 'a-4', sequence: 4, activityTypeCode: 'WACHTTIJD', activityTypeName: 'Wachttijd', hasStops: false, supportsGoods: false }),
    ]
    api.getDossier.mockResolvedValue(many)
    renderPage()
    const activities = await screen.findByRole('article', { name: 'Activiteiten' })
    expect(within(activities).getByText('4 activiteiten')).toBeInTheDocument()
    expect(within(activities).getByText('Kraanwerk')).toBeInTheDocument()
    expect(within(activities).queryByText('Wachttijd')).not.toBeInTheDocument()
    expect(within(activities).getByText('+ 1 andere activiteit')).toBeInTheDocument()
  })

  it('summarises the price: € 450,00 marked "1/2 activiteiten geprijsd" and "Gedeeltelijk"', async () => {
    renderPage()
    const price = await screen.findByRole('article', { name: 'Verkoop & prijs' })
    expect(within(price).getByText(/€\s450,00/)).toBeInTheDocument()
    expect(within(price).getByText('1/2 activiteiten geprijsd')).toBeInTheDocument()
    expect(within(price).getByText('Gedeeltelijk')).toBeInTheDocument()
    expect(within(price).queryByText('Nog geen prijs')).not.toBeInTheDocument()
    expect(within(price).queryByText('⚠')).not.toBeInTheDocument()
  })

  it('shows "Nog geen prijs" + the "Geen prijs" badge for an unpriced dossier, never € 0,00', async () => {
    api.getDossier.mockResolvedValue(unpricedDossier())
    orders.get.mockResolvedValue(draftOrder())
    renderPage()
    const price = await screen.findByRole('article', { name: 'Verkoop & prijs' })
    expect(within(price).getByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(price).getByText('Geen prijs')).toBeInTheDocument()
    expect(within(price).getByText('0/1 activiteiten geprijsd')).toBeInTheDocument()
    expect(within(price).queryByText(/€\s0,00/)).not.toBeInTheDocument()
  })

  it('flags a deliberate € 0 with the ⚠ marker and the zero warning', async () => {
    api.getDossier.mockResolvedValue(overviewDossier({ financials: financials(0, 2, 1, 1), readiness: [] }))
    renderPage()
    const price = await screen.findByRole('article', { name: 'Verkoop & prijs' })
    // The amount itself is € 0,00 (priced, with provenance) — the warning line repeats it.
    expect(price.querySelector('.dossier-ov-amount strong')).toHaveTextContent(/€\s0,00/)
    expect(within(price).getByText('⚠')).toHaveAttribute('title', '⚠ Verkoopprijs van € 0,00 — controleer of dit bewust is.')
    expect(within(price).getByText('⚠ Verkoopprijs van € 0,00 — controleer of dit bewust is.')).toBeInTheDocument()
    expect(within(price).queryByText('Nog geen prijs')).not.toBeInTheDocument()
  })

  it('summarises the goods of the loaded order: "2 × EUROPALLET" and the line count', async () => {
    renderPage()
    const goods = await screen.findByRole('article', { name: 'Goederen' })
    expect(await within(goods).findByText('2 × EUROPALLET')).toBeInTheDocument()
    expect(within(goods).getByText('1 goederenlijn')).toBeInTheDocument()
  })

  it('summarises the documents: "3 documenten" with their distinct types "CMR · Leverbon"', async () => {
    renderPage()
    const docs = await screen.findByRole('article', { name: 'Documenten' })
    expect(within(docs).getByText('3 documenten')).toBeInTheDocument()
    expect(within(docs).getByText('CMR · Leverbon')).toBeInTheDocument()
    expect(within(docs).queryByText('Geen documenten.')).not.toBeInTheDocument()
  })

  it('shows "Geen documenten." when the dossier has none', async () => {
    api.getDossier.mockResolvedValue(overviewDossier({ documentCount: 0, documentTypes: [] }))
    renderPage()
    const docs = await screen.findByRole('article', { name: 'Documenten' })
    expect(within(docs).getByText('0 documenten')).toBeInTheDocument()
    expect(within(docs).getByText('Geen documenten.')).toBeInTheDocument()
  })

  it('summarises notes & history: "Nog geen notities." vs the note text, plus the last change', async () => {
    const { unmount } = renderPage()
    let history = await screen.findByRole('article', { name: 'Notities & historiek' })
    expect(within(history).getByText('0 notities')).toBeInTheDocument()
    expect(within(history).getByText('Nog geen notities.')).toBeInTheDocument()
    expect(within(history).getByText(`Laatste wijziging ${formatDateTime('2026-08-12T09:30:00Z')}`)).toBeInTheDocument()
    unmount()

    api.getDossier.mockResolvedValue(overviewDossier({ notes: 'Bel de klant vóór levering.', lastChangedAt: '2026-08-13T14:05:00Z' }))
    renderPage()
    history = await screen.findByRole('article', { name: 'Notities & historiek' })
    expect(within(history).getByText('1 notitie')).toBeInTheDocument()
    expect(within(history).getByText('Bel de klant vóór levering.')).toBeInTheDocument()
    expect(within(history).queryByText('Nog geen notities.')).not.toBeInTheDocument()
    expect(within(history).getByText(`Laatste wijziging ${formatDateTime('2026-08-13T14:05:00Z')}`)).toBeInTheDocument()
  })
})

describe('Dossier navigation — subnav, deep links and history', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(FULL_RIGHTS)
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(overviewDossier())
    orders.get.mockResolvedValue(pricedOrder())
  })

  it('renders all seven subsections as real links for a transport dossier, the active one marked aria-current', async () => {
    renderPage()
    await screen.findByText('Nexans site Antwerpen')
    const links = within(subnav()).getAllByRole('link')
    expect(links.map((link) => link.textContent?.trim())).toEqual(ALL_TABS)
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      OVERVIEW, '/dossiers/d-1/activiteiten', ROUTE_TAB, '/dossiers/d-1/goederen', PRICE_TAB, '/dossiers/d-1/documenten', '/dossiers/d-1/historiek',
    ])
    expect(subnavLink('Overzicht')).toHaveAttribute('aria-current', 'page')
    expect(subnavLink('Route')).not.toHaveAttribute('aria-current')
  })

  it('omits the Route and Goederen tabs and the Route card for a storage-only dossier', async () => {
    api.getDossier.mockResolvedValue(storageOnlyDossier())
    renderPage()
    await screen.findByRole('article', { name: 'Activiteiten' })
    expect(within(subnav()).getAllByRole('link').map((link) => link.textContent?.trim())).toEqual([
      'Overzicht', 'Activiteiten', 'Verkoop & prijs', 'Documenten', 'Historiek',
    ])
    expect(screen.queryByRole('heading', { name: 'Route' })).not.toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Route' })).not.toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Goederen' })).not.toBeInTheDocument()
    expect(orders.get).not.toHaveBeenCalled()
  })

  it('"Open route" navigates to the route tab and mounts the editor with its location inputs', async () => {
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')
    expect(screen.queryAllByLabelText('locatie')).toEqual([])

    await user.click(within(card('Route')).getAllByRole('link', { name: 'Open route' })[0])
    expect(router.state.location.pathname).toBe(ROUTE_TAB)
    expect(await screen.findByDisplayValue('Nexans site Antwerpen')).toBeInTheDocument()
    expect(screen.getAllByLabelText('locatie')).toHaveLength(2)
    expect(subnavLink('Route')).toHaveAttribute('aria-current', 'page')
    expect(screen.queryByRole('article', { name: 'Route' })).not.toBeInTheDocument()
    // The order was loaded by the shell on the overview; the tab switch loads nothing again.
    expect(orders.get).toHaveBeenCalledTimes(1)
  })

  it('"Open prijsdetails" navigates to the price tab with the agreed price input', async () => {
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(within(card('Verkoop & prijs')).getAllByRole('link', { name: 'Open prijsdetails' })[0])
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toHaveValue('450')
    expect(subnavLink('Verkoop & prijs')).toHaveAttribute('aria-current', 'page')
  })

  it('"Open documenten" navigates to the documents tab, which hosts the target order\'s documents panel', async () => {
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(within(card('Documenten')).getAllByRole('link', { name: 'Open documenten' })[0])
    expect(router.state.location.pathname).toBe('/dossiers/d-1/documenten')
    expect(await screen.findByText('Documenten van opdracht ORD-0001')).toBeInTheDocument()
    await waitFor(() => expect(documents.list).toHaveBeenCalledWith('o-1'))
    expect(document.getElementById('sectie-documenten')).not.toBeNull()
  })

  it('"Open historiek" navigates to the history tab with notes and the permission-gated audit trail', async () => {
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(within(card('Notities & historiek')).getAllByRole('link', { name: 'Open historiek' })[0])
    expect(router.state.location.pathname).toBe('/dossiers/d-1/historiek')
    const history = document.getElementById('sectie-notities')!
    expect(history).not.toBeNull()
    expect(within(history).getByRole('heading', { name: 'Wijzigingshistoriek' })).toBeInTheDocument()
    // No audit_logs.view: the panel explains instead of fetching.
    expect(within(history).getByText('Je hebt geen rechten om de historiek te bekijken.')).toBeInTheDocument()
    expect(within(history).getByText('Geen notities.')).toBeInTheDocument()
    expect(subnavLink('Historiek')).toHaveAttribute('aria-current', 'page')
  })

  it('a deep link to /dossiers/d-1/prijs renders the price tab directly', async () => {
    renderPage(PRICE_TAB)
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toHaveValue('450')
    expect(subnavLink('Verkoop & prijs')).toHaveAttribute('aria-current', 'page')
    expect(subnavLink('Overzicht')).not.toHaveAttribute('aria-current')
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
    expect(document.getElementById('sectie-route')).toBeNull()
  })

  it('an unknown segment redirects to the overview path', async () => {
    const { router } = renderPage('/dossiers/d-1/onbekend')
    await screen.findByRole('article', { name: 'Activiteiten' })
    await waitFor(() => expect(router.state.location.pathname).toBe(OVERVIEW))
    expect(subnavLink('Overzicht')).toHaveAttribute('aria-current', 'page')
  })

  it('a tab a dossier does not offer (Route for storage-only) also lands on the overview', async () => {
    api.getDossier.mockResolvedValue(storageOnlyDossier())
    const { router } = renderPage(ROUTE_TAB)
    await screen.findByRole('article', { name: 'Activiteiten' })
    await waitFor(() => expect(router.state.location.pathname).toBe(OVERVIEW))
    expect(document.getElementById('sectie-route')).toBeNull()
  })

  it('browser back returns from the route tab to the overview, forward returns to the route again', async () => {
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')
    await user.click(within(card('Route')).getAllByRole('link', { name: 'Open route' })[0])
    await screen.findByDisplayValue('Nexans site Antwerpen')

    await router.navigate(-1)
    await waitFor(() => expect(router.state.location.pathname).toBe(OVERVIEW))
    expect(await screen.findByRole('article', { name: 'Route' })).toBeInTheDocument()
    expect(screen.queryAllByLabelText('locatie')).toEqual([])
    expect(subnavLink('Overzicht')).toHaveAttribute('aria-current', 'page')

    await router.navigate(1)
    await waitFor(() => expect(router.state.location.pathname).toBe(ROUTE_TAB))
    expect(await screen.findByDisplayValue('Nexans site Antwerpen')).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Route' })).not.toBeInTheDocument()
    expect(subnavLink('Route')).toHaveAttribute('aria-current', 'page')
    // The shell stayed mounted the whole time: dossier and order were loaded exactly once.
    expect(api.getDossier).toHaveBeenCalledTimes(1)
    expect(orders.get).toHaveBeenCalledTimes(1)
  })

  it('a dirty route guards the subnav: "Blijven bewerken" keeps the input on the route tab, "Pagina verlaten" leaves', async () => {
    const user = userEvent.setup()
    const { router } = renderPage(ROUTE_TAB)
    await screen.findByDisplayValue('Nexans site Antwerpen')
    const unloadCard = screen.getAllByLabelText('locatie')[1].closest('fieldset')!
    await user.type(within(unloadCard).getByLabelText(/Plaats/), 'Gent')

    await user.click(subnavLink('Verkoop & prijs'))
    let dialog = await screen.findByRole('dialog', { name: 'Niet-opgeslagen wijzigingen' })
    expect(within(dialog).getByText('Je hebt wijzigingen die nog niet zijn opgeslagen. Weet je zeker dat je deze pagina wilt verlaten?')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Blijven bewerken' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe(ROUTE_TAB)
    expect(subnavLink('Route')).toHaveAttribute('aria-current', 'page')
    expect(screen.getByDisplayValue('Gent')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Nexans site Antwerpen')).toBeInTheDocument()
    expect(screen.queryByLabelText('Afgesproken prijs (€)')).not.toBeInTheDocument()

    await user.click(subnavLink('Verkoop & prijs'))
    dialog = await screen.findByRole('dialog', { name: 'Niet-opgeslagen wijzigingen' })
    await user.click(within(dialog).getByRole('button', { name: 'Pagina verlaten' }))
    await waitFor(() => expect(router.state.location.pathname).toBe(PRICE_TAB))
    expect(await screen.findByLabelText('Afgesproken prijs (€)')).toBeInTheDocument()
    expect(screen.queryAllByLabelText('locatie')).toEqual([])
    expect(orders.update).not.toHaveBeenCalled()
  })
})

describe('Dossier navigation — attention jumps land on the right tab and field', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(FULL_RIGHTS)
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('"Ga naar route" from the overview activates Route and focuses the unloading location', async () => {
    api.getDossier.mockResolvedValue(unpricedDossier())
    orders.get.mockResolvedValue(draftOrder())
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar route' }))
    expect(router.state.location.pathname).toBe(ROUTE_TAB)
    expect(subnavLink('Route')).toHaveAttribute('aria-current', 'page')
    const locationInputs = await screen.findAllByLabelText('locatie')
    expect(locationInputs).toHaveLength(2)
    await waitFor(() => expect(document.activeElement).toBe(locationInputs[1]))
    expect(within(locationInputs[1].closest('fieldset')!).getByText('2. Lossen')).toBeInTheDocument()
    expect(document.getElementById('sectie-route')?.hasAttribute('data-highlight')).toBe(true)
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('"Ga naar prijs" from the overview activates Verkoop & prijs and focuses the agreed price', async () => {
    api.getDossier.mockResolvedValue(unpricedDossier())
    orders.get.mockResolvedValue(draftOrder())
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    expect(subnavLink('Verkoop & prijs')).toHaveAttribute('aria-current', 'page')
    const agreed = await screen.findByLabelText('Afgesproken prijs (€)')
    await waitFor(() => expect(document.activeElement).toBe(agreed))
    expect(document.getElementById('sectie-prijs')?.hasAttribute('data-highlight')).toBe(true)
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('"Ga naar prijs" for the crane item selects Kraanwerk on the price tab and focuses its agreed price', async () => {
    api.getDossier.mockResolvedValue(craneDossier())
    orders.get.mockResolvedValue(pricedOrder())
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByText('Nexans site Antwerpen')

    await user.click(screen.getByRole('button', { name: 'Ga naar prijs' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    const units = await screen.findByRole('group', { name: 'Eenheid' })
    await waitFor(() => expect(within(units).getByRole('button', { name: /Kraanwerk/ })).toHaveAttribute('aria-pressed', 'true'))
    expect(screen.getByText('Activiteit Kraanwerk')).toBeInTheDocument()
    await waitFor(() => expect(document.activeElement).toBe(document.getElementById('dossier-activity-agreed-price')))
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledTimes(1)
    // A standalone unit needs no order: the one load was the shell's on the overview.
    expect(orders.get).toHaveBeenCalledTimes(1)
  })
})

describe('Dossier navigation — permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(overviewDossier())
    orders.get.mockResolvedValue(pricedOrder())
  })

  it('without orders.edit the overview still renders and the route tab is read-only', async () => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    const user = userEvent.setup()
    renderPage()
    expect(await screen.findByRole('article', { name: 'Route' })).toBeInTheDocument()
    expect(await within(card('Route')).findByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(card('Verkoop & prijs')).toBeInTheDocument()

    await user.click(subnavLink('Route'))
    expect(await screen.findByText('Je kunt de route bekijken maar niet bewerken.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Route opslaan' })).not.toBeInTheDocument()
    expect(screen.queryAllByLabelText('locatie')).toEqual([])
  })

  it('a view-only user gets the read-only overview without any edit controls or crash', async () => {
    auth.permissions = new Set(['dossiers.view'])
    renderPage()
    expect(await screen.findByRole('article', { name: 'Route' })).toBeInTheDocument()
    expect(await within(card('Route')).findByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(within(subnav()).getAllByRole('link')).toHaveLength(7)
    expect(document.querySelectorAll('input, textarea, select')).toHaveLength(0)
    expect(screen.queryByRole('button', { name: '+ Activiteit' })).not.toBeInTheDocument()
  })
})
