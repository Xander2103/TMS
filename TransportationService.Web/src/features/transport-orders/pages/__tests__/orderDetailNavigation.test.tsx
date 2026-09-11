import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { TransportOrderDetailPage } from '../TransportOrderDetailPage'
import { orderDetail } from '../../../dossiers/__tests__/fixtures'
import type { OrderPricingSnapshot, TransportOrderDetail } from '../../types'
import type { Package } from '../../../packages/types'
import type { OrderDocument } from '../../api/orderDocumentsApi'
import type { OrderTimelineEvent } from '../../api/transportOrdersApi'
import { formatDateTime } from '../../../../utils/dates'

/**
 * Redesign 2026-09-12 — the transport order as a navigation-based workspace: one shell (header
 * with the commercial bar and grouped actions, attention strip, subnav) and ONE subsection at a
 * time selected by `/transport-orders/:id/:section?`. The Overzicht is a read-only summary with
 * "Open …" links; editing lives in the subsections. These tests prove the navigation contract
 * (subnav, deep links, unknown segment), the summary cards, the attention strip and the header.
 */

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.has(c)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [{ id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' }],
    isLoading: false,
    error: null,
  }),
}))
// Subsection panels with their own fetches: stubbed, exactly like the other detail-page suites.
vi.mock('../../components/OrderDocumentsPanel', () => ({ OrderDocumentsPanel: () => <div /> }))
vi.mock('../../components/OrderDocumentStrategyPanel', () => ({ OrderDocumentStrategyPanel: () => <div /> }))
vi.mock('../../components/OrderTimelinePanel', () => ({ OrderTimelinePanel: () => <div /> }))
vi.mock('../../components/StopExecutionPlanDialog', () => ({ StopExecutionPlanDialog: () => <div /> }))
vi.mock('../../../packages/components/OrderPackagesPanel', () => ({ OrderPackagesPanel: () => <div /> }))
vi.mock('../../../packages/components/CustomerPackagesSummary', () => ({ CustomerPackagesSummary: () => <div /> }))
vi.mock('../../../customers/components/CustomerMessagesPanel', () => ({ CustomerMessagesPanel: () => <div /> }))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))

// The overview fetches four light lists for its cards; each one is a mock we control per test.
const api = vi.hoisted(() => ({
  getTransportOrder: vi.fn(),
  getTransportOrderTimeline: vi.fn(),
  listOrderDocuments: vi.fn(),
  listOrderPackages: vi.fn(),
  listCustomerMessages: vi.fn(),
}))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getTransportOrder: api.getTransportOrder,
  getTransportOrderTimeline: api.getTransportOrderTimeline,
}))
vi.mock('../../api/orderDocumentsApi', async (orig) => ({
  ...(await orig<typeof import('../../api/orderDocumentsApi')>()),
  listOrderDocuments: api.listOrderDocuments,
}))
vi.mock('../../../packages/api/packagesApi', () => ({ listOrderPackages: api.listOrderPackages }))
vi.mock('../../../customers/api/customerMessagesApi', () => ({ listCustomerMessages: api.listCustomerMessages }))

const OVERVIEW = '/transport-orders/order-1'
const PRICE_TAB = '/transport-orders/order-1/prijs'
const STOPS_TAB = '/transport-orders/order-1/stops'
const ALL_TABS = ['Overzicht', 'Lading', 'Prijs', 'Stops', 'Colli', 'Historiek', 'Berichten']
const FULL_RIGHTS = ['orders.view', 'orders.edit', 'orders.delete', 'orders.create', 'packages.view', 'customer_messages.view']

const STOP_PLANNED_FROM = '2026-08-12T07:00:00Z'

function order(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return orderDetail({ id: 'order-1', ...overrides })
}

function snapshot(overrides: Partial<OrderPricingSnapshot> = {}): OrderPricingSnapshot {
  return {
    tariffDate: '2026-08-12',
    currency: 'EUR',
    zoneCode: null,
    zoneName: null,
    agreementNames: null,
    unitSummary: null,
    calculatedTotal: 428.5,
    overrideAmount: null,
    overrideReason: null,
    overriddenByUserId: null,
    overriddenAtUtc: null,
    explanation: 'Basisregel: 428.50 EUR',
    status: 'Draft',
    linesTotal: 428.5,
    ...overrides,
  }
}

const pricedOrder = (overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail =>
  order({
    pricingSnapshot: snapshot(),
    pricingLines: [
      { id: 'line-1', label: 'Basisregel', amount: 428.5, source: 'Regel X', informational: false, kind: 'Auto', lineKey: 'rule:r1', quantity: 2, unitPrice: 214.25 },
    ],
    ...overrides,
  })

function orderDocument(id: string, title: string, documentType: OrderDocument['documentType'], hasAttachment = true): OrderDocument {
  return {
    id, transportOrderId: 'order-1', documentType, customTypeName: null, title, hasAttachment,
    fileName: hasAttachment ? `${id}.pdf` : null, issueDate: null, notes: null, customerVisible: false,
  }
}

function event(title: string, timestamp: string, userName: string | null = 'Dev Admin'): OrderTimelineEvent {
  return { timestamp, category: 'order', title, detail: null, userName }
}

function renderPage(initialPath = OVERVIEW) {
  const router = createMemoryRouter([{ path: '/transport-orders/:id/:section?', element: <TransportOrderDetailPage /> }], {
    initialEntries: [initialPath],
  })
  return { ...render(<RouterProvider router={router} />), router }
}

function subnav() {
  return screen.getByRole('navigation', { name: 'Onderdelen van de opdracht' })
}

function subnavLink(name: string) {
  return within(subnav()).getByRole('link', { name })
}

/** An overview card by its accessible name (article labelled by its h2). */
function card(name: string) {
  return screen.getByRole('article', { name })
}

/**
 * An overview card by its visible heading. Needed for the two cards that share tab="lading":
 * OverviewCard derives the h2 id from the tab, so "Lading / Goederen" and "Foto's & documenten"
 * both render <h2 id="tod-lading-title"> and the documents card's aria-labelledby resolves to the
 * FIRST one — see the skipped test below.
 */
function cardByHeading(name: string) {
  const heading = screen.getByRole('heading', { name })
  const article = heading.closest('article')
  if (!article) throw new Error(`no card for heading ${name}`)
  return article as HTMLElement
}

/** The page header (the overview cards carry their own <header>, so the role alone is ambiguous). */
function header() {
  const element = document.querySelector<HTMLElement>('header.tod-header')
  if (!element) throw new Error('page header not rendered')
  return element
}

function attention() {
  return screen.getByRole('region', { name: 'Aandachtspunten' })
}

beforeEach(() => {
  auth.permissions = new Set(FULL_RIGHTS)
  api.getTransportOrder.mockReset().mockResolvedValue(order())
  api.getTransportOrderTimeline.mockReset().mockResolvedValue([])
  api.listOrderDocuments.mockReset().mockResolvedValue([])
  api.listOrderPackages.mockReset().mockResolvedValue([])
  api.listCustomerMessages.mockReset().mockResolvedValue([])
})

describe('order detail — subnav and sections', () => {
  it('renders the seven section links and marks the overview as the current page', async () => {
    renderPage()
    await screen.findByText('ORD-0001 — Nexans NV')

    const links = within(subnav()).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual(ALL_TABS)
    expect(subnavLink('Overzicht')).toHaveAttribute('aria-current', 'page')
    expect(subnavLink('Prijs')).not.toHaveAttribute('aria-current')
    expect(subnavLink('Stops')).toHaveAttribute('href', STOPS_TAB)
    expect(subnavLink('Overzicht')).toHaveAttribute('href', OVERVIEW)
  })

  it('a deep link to /stops renders the stops tab directly, without the overview cards', async () => {
    renderPage(STOPS_TAB)
    await screen.findByText('ORD-0001 — Nexans NV')

    const table = await screen.findByRole('table')
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers).toEqual(expect.arrayContaining(['#', 'Type', 'Locatie', 'Adres', 'Gepland', 'Tijdseis']))
    expect(within(table).getByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(within(table).getByRole('button', { name: 'Venster' })).toBeInTheDocument()
    expect(subnavLink('Stops')).toHaveAttribute('aria-current', 'page')
    expect(subnavLink('Overzicht')).not.toHaveAttribute('aria-current')
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
    // The overview's card fetches only run on the overview.
    expect(api.listOrderDocuments).not.toHaveBeenCalled()
    expect(api.getTransportOrderTimeline).not.toHaveBeenCalled()
  })

  it('an unknown segment lands on the overview with a clean URL', async () => {
    const { router } = renderPage('/transport-orders/order-1/onbekend')
    await screen.findByRole('article', { name: 'Route / Stops' })

    await waitFor(() => expect(router.state.location.pathname).toBe(OVERVIEW))
    expect(subnavLink('Overzicht')).toHaveAttribute('aria-current', 'page')
    expect(api.getTransportOrder).toHaveBeenCalledTimes(1)
  })

  it('the commercial bar lives in the header and is present on the prijs tab as well', async () => {
    renderPage(PRICE_TAB)
    const bar = await screen.findByTestId('order-commercial-bar')

    expect(header()).toContainElement(bar)
    expect(bar).toHaveTextContent('Klant: Nexans NV')
    expect(bar).toHaveTextContent('Facturerende entiteit: Klantstandaard')
    expect(within(bar).getByRole('button', { name: 'Klant van deze opdracht wijzigen' })).toBeInTheDocument()
    expect(within(bar).getByRole('button', { name: 'Facturerende entiteit van deze opdracht wijzigen' })).toBeInTheDocument()
    expect(screen.getByText('Totaalprijs')).toBeInTheDocument()
    expect(subnavLink('Prijs')).toHaveAttribute('aria-current', 'page')
  })
})

describe('order detail — overview cards', () => {
  // BUG (component, not fixed here): OverviewCard uses id={`tod-${tab}-title`} for its h2, and both the
  // "Lading / Goederen" card and the "Foto's & documenten" card are rendered with tab="lading". The
  // DOM therefore carries two <h2 id="tod-lading-title">, and the documents card's aria-labelledby
  // resolves to the first one: its accessible name is "Lading / Goederen" instead of
  // "Foto's & documenten" (getByRole('article', { name: 'Lading / Goederen' }) finds two cards).
  // Fix in OrderOverview.tsx: derive the id from a per-card key (e.g. the title) rather than the tab.
  it('labels every overview card uniquely by its own heading', async () => {
    renderPage()
    await screen.findByRole('article', { name: 'Route / Stops' })

    expect(screen.getAllByRole('article', { name: 'Lading / Goederen' })).toHaveLength(1)
    expect(screen.getByRole('article', { name: "Foto's & documenten" })).toBeInTheDocument()
    const ids = Array.from(document.querySelectorAll('.tod-card h2')).map((h) => h.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('shows the route card with the stop type, location, address and planned time', async () => {
    renderPage()
    const route = await screen.findByRole('article', { name: 'Route / Stops' })

    expect(route).toHaveTextContent('1 stop')
    expect(within(route).getByText('1. Laden')).toBeInTheDocument()
    expect(within(route).getByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(within(route).getByText('Noorderlaan 10, 2030 Antwerpen')).toBeInTheDocument()
    expect(within(route).getByText(formatDateTime(STOP_PLANNED_FROM))).toBeInTheDocument()
    expect(within(route).getByRole('link', { name: 'Open stops' })).toHaveAttribute('href', STOPS_TAB)
  })

  it('shows the price card with the total, the price status badge and the order status', async () => {
    renderPage()
    const price = await screen.findByRole('article', { name: 'Verkoop & prijs' })

    expect(within(price).getByText('Totaalprijs')).toBeInTheDocument()
    // formatCurrency emits a non-breaking space; the DOM text normaliser collapses it to a plain one.
    expect(within(price).getByText('€ 428,50')).toBeInTheDocument()
    expect(within(price).getByText('Prijsstatus')).toBeInTheDocument()
    expect(within(price).getByText('Nog te bevestigen')).toBeInTheDocument()
    expect(within(price).getByText('Opdracht')).toBeInTheDocument()
    expect(within(price).getByText('Bevestigd')).toBeInTheDocument()
    expect(within(price).getByRole('link', { name: 'Open prijsdetails' })).toHaveAttribute('href', PRICE_TAB)
  })

  it('shows the confirmation stamp on the price card once the price is confirmed', async () => {
    api.getTransportOrder.mockResolvedValue(
      pricedOrder({ pricingSnapshot: snapshot({ status: 'Locked', confirmedAtUtc: '2026-08-12T10:00:00Z', confirmedByName: 'Dev Admin' }) }),
    )
    renderPage()
    const price = await screen.findByRole('article', { name: 'Verkoop & prijs' })

    expect(within(price).getByText(`Bevestigd op ${formatDateTime('2026-08-12T10:00:00Z')} door Dev Admin.`)).toBeInTheDocument()
    expect(within(price).getAllByText('Bevestigd').length).toBeGreaterThanOrEqual(1)
  })

  it('shows the lading card with the goods facts', async () => {
    api.getTransportOrder.mockResolvedValue(order({ weightKg: 850, palletCount: 2, adrRequired: true }))
    renderPage()
    await screen.findByRole('heading', { name: 'Lading / Goederen' })
    const lading = cardByHeading('Lading / Goederen')

    const fact = (label: string) => within(lading).getByText(label).nextElementSibling as HTMLElement
    expect(fact('Goederensoort')).toHaveTextContent('2 europallets')
    expect(fact('Aantal')).toHaveTextContent('2 Europallet')
    expect(fact('Gewicht')).toHaveTextContent('850 kg')
    expect(fact('Volume')).toHaveTextContent('—')
    expect(fact('Palletten')).toHaveTextContent('2')
    expect(fact('Kenmerken')).toHaveTextContent('ADR')
    expect(within(lading).getByRole('link', { name: 'Open lading' })).toHaveAttribute('href', '/transport-orders/order-1/lading')
  })

  it('shows the colli count from the packages list when packages.view is granted', async () => {
    api.listOrderPackages.mockResolvedValue([{ id: 'p-1' }, { id: 'p-2' }] as unknown as Package[])
    renderPage()
    const colli = await screen.findByRole('article', { name: 'Colli' })

    expect(await within(colli).findByText('2 colli')).toBeInTheDocument()
    expect(api.listOrderPackages).toHaveBeenCalledWith('order-1')
    expect(within(colli).getByRole('link', { name: 'Open colli' })).toHaveAttribute('href', '/transport-orders/order-1/colli')
  })

  it('does not fetch packages without packages.view and explains the Colli section instead', async () => {
    auth.permissions = new Set(FULL_RIGHTS.filter((p) => p !== 'packages.view'))
    renderPage()
    const colli = await screen.findByRole('article', { name: 'Colli' })

    expect(within(colli).getByText('Colli van deze opdracht worden in de Colli-sectie samengevat.')).toBeInTheDocument()
    expect(within(colli).queryByText(/\d+ colli/)).not.toBeInTheDocument()
    expect(api.listOrderPackages).not.toHaveBeenCalled()
  })

  it('shows the documents card with the count and the first titles from the documents list', async () => {
    api.listOrderDocuments.mockResolvedValue([
      orderDocument('doc-1', 'CMR 0001', 'Cmr'),
      orderDocument('doc-2', 'Leverbon', 'DeliveryNote'),
      orderDocument('doc-3', 'Foto laadplaats', 'Other', false),
    ])
    renderPage()
    await screen.findByRole('heading', { name: "Foto's & documenten" })
    const docs = cardByHeading("Foto's & documenten")

    expect(await within(docs).findByText('3 documenten')).toBeInTheDocument()
    expect(within(docs).getByText('CMR 0001')).toBeInTheDocument()
    expect(within(docs).getByText('Foto laadplaats')).toBeInTheDocument()
    expect(within(docs).getByText('Overig')).toBeInTheDocument() // type label via ORDER_DOCUMENT_TYPE_LABELS (kept real)
    expect(within(docs).getByText('2 met bestand')).toBeInTheDocument()
    expect(api.listOrderDocuments).toHaveBeenCalledWith('order-1')
  })

  it('shows the documents empty state when there are no photos or documents', async () => {
    renderPage()
    await screen.findByRole('heading', { name: "Foto's & documenten" })
    const docs = cardByHeading("Foto's & documenten")

    expect(await within(docs).findByText('0 documenten')).toBeInTheDocument()
    expect(within(docs).getByText("Nog geen foto's of documenten toegevoegd.")).toBeInTheDocument()
  })

  it('previews the first three timeline events on the history card', async () => {
    api.getTransportOrderTimeline.mockResolvedValue([
      event('Opdracht aangemaakt', '2026-08-12T09:00:00Z'),
      event('Status gewijzigd naar Bevestigd', '2026-08-12T09:30:00Z'),
      event('Prijs herberekend', '2026-08-12T10:00:00Z', null),
      event('Vierde gebeurtenis', '2026-08-12T11:00:00Z'),
    ])
    renderPage()
    const history = await screen.findByRole('article', { name: 'Historiek' })

    expect(await within(history).findByText('Opdracht aangemaakt')).toBeInTheDocument()
    expect(within(history).getByText('Status gewijzigd naar Bevestigd')).toBeInTheDocument()
    expect(within(history).getByText('Prijs herberekend')).toBeInTheDocument()
    expect(within(history).queryByText('Vierde gebeurtenis')).not.toBeInTheDocument()
    expect(within(history).getByText(formatDateTime('2026-08-12T09:00:00Z'))).toBeInTheDocument()
    expect(api.getTransportOrderTimeline).toHaveBeenCalledWith('order-1')
    expect(within(history).getByRole('link', { name: 'Open historiek' })).toHaveAttribute('href', '/transport-orders/order-1/historiek')
  })

  it('shows the empty conversation state on the messages card', async () => {
    renderPage()
    const messages = await screen.findByRole('article', { name: 'Berichten (klantportaal)' })

    expect(await within(messages).findByText('Nog geen berichten in dit gesprek.')).toBeInTheDocument()
    expect(api.listCustomerMessages).toHaveBeenCalledWith('c-1', 'order-1')
    expect(within(messages).getByRole('link', { name: 'Open berichten' })).toHaveAttribute('href', '/transport-orders/order-1/berichten')
  })

  it('carries no price-line or stop actions: the overview is read-only apart from the header', async () => {
    api.getTransportOrder.mockResolvedValue(pricedOrder())
    renderPage()
    await screen.findByRole('article', { name: 'Verkoop & prijs' })

    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Prijsregels' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Venster' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Vrije regel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Herberekenen' })).not.toBeInTheDocument()
    // The only Bewerken is the header's order action; none inside a price table.
    const editButtons = screen.getAllByRole('button', { name: 'Bewerken' })
    expect(editButtons).toHaveLength(1)
    expect(header()).toContainElement(editButtons[0])
  })
})

describe('order detail — overview links into the subsections', () => {
  it('"Open prijsdetails" navigates to the prijs tab and shows the price lines', async () => {
    const user = userEvent.setup()
    api.getTransportOrder.mockResolvedValue(pricedOrder())
    const { router } = renderPage()
    await screen.findByRole('article', { name: 'Verkoop & prijs' })
    expect(screen.queryByRole('heading', { name: /Prijsregels/ })).not.toBeInTheDocument()

    await user.click(within(card('Verkoop & prijs')).getByRole('link', { name: 'Open prijsdetails' }))
    expect(router.state.location.pathname).toBe(PRICE_TAB)
    expect(await screen.findByRole('heading', { name: /Prijsregels/ })).toBeInTheDocument()
    expect(screen.getByText('Basisregel')).toBeInTheDocument()
    const headers = within(screen.getByRole('table')).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers.slice(0, 4)).toEqual(['Omschrijving', 'Type', 'Berekening', 'Bedrag'])
    expect(subnavLink('Prijs')).toHaveAttribute('aria-current', 'page')
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
    // The order was loaded once by the shell; the tab switch loads nothing again.
    expect(api.getTransportOrder).toHaveBeenCalledTimes(1)
  })

  it('"Open stops" navigates to the stops tab with the stop table', async () => {
    const user = userEvent.setup()
    const { router } = renderPage()
    await screen.findByRole('article', { name: 'Route / Stops' })
    expect(screen.queryByRole('table')).not.toBeInTheDocument()

    await user.click(within(card('Route / Stops')).getByRole('link', { name: 'Open stops' }))
    expect(router.state.location.pathname).toBe(STOPS_TAB)
    const table = await screen.findByRole('table')
    expect(within(table).getByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(within(table).getByText('Noorderlaan 10, 2030 Antwerpen')).toBeInTheDocument()
    expect(within(table).getByRole('button', { name: 'Venster' })).toBeInTheDocument()
    expect(subnavLink('Stops')).toHaveAttribute('aria-current', 'page')
  })
})

describe('order detail — attention strip', () => {
  it('flags a price still to confirm (Draft snapshot) with a link to the price details', async () => {
    api.getTransportOrder.mockResolvedValue(pricedOrder())
    renderPage()
    await screen.findByText('ORD-0001 — Nexans NV')

    const strip = attention()
    expect(within(strip).getByText('Prijs nog te bevestigen')).toBeInTheDocument()
    expect(within(strip).getByRole('link', { name: 'Open prijsdetails' })).toHaveAttribute('href', PRICE_TAB)
    expect(within(strip).queryByText('Geannuleerd')).not.toBeInTheDocument()
  })

  it('shows "Geannuleerd" with the cancellation reason for a cancelled order, without price nagging', async () => {
    api.getTransportOrder.mockResolvedValue(
      pricedOrder({ status: 'Cancelled', cancellationReason: 'Klant heeft de rit afgezegd.' }),
    )
    renderPage()
    await screen.findByText('ORD-0001 — Nexans NV')

    const strip = attention()
    expect(within(strip).getByText('Geannuleerd')).toBeInTheDocument()
    expect(within(strip).getByText('Klant heeft de rit afgezegd.')).toBeInTheDocument()
    expect(within(strip).queryByText('Prijs nog te bevestigen')).not.toBeInTheDocument()
  })

  it('flags a missing route with "Open stops"', async () => {
    api.getTransportOrder.mockResolvedValue(order({ stops: [] }))
    renderPage()
    await screen.findByText('ORD-0001 — Nexans NV')

    const strip = attention()
    expect(within(strip).getByText('Nog geen stops')).toBeInTheDocument()
    expect(within(strip).getByRole('link', { name: 'Open stops' })).toHaveAttribute('href', STOPS_TAB)
  })
})

describe('order detail — header actions', () => {
  it('shows Bewerken, Verwijderen, CMR, Leveringsbon and Gebruik als sjabloon in the header with the permissions', async () => {
    api.getTransportOrder.mockResolvedValue(order({ status: 'Draft' }))
    renderPage()
    await screen.findByText('ORD-0001 — Nexans NV')

    const head = header()
    for (const name of ['Bewerken', 'Verwijderen', 'CMR', 'Leveringsbon', 'Gebruik als sjabloon']) {
      expect(within(head).getByRole('button', { name })).toBeInTheDocument()
    }
    // Once each: the former duplicate bottom bar is gone.
    expect(screen.getAllByRole('button', { name: 'Bewerken' })).toHaveLength(1)
    expect(screen.getAllByRole('button', { name: 'Verwijderen' })).toHaveLength(1)
  })

  it('hides the permission-gated actions without orders.edit/orders.delete/orders.create', async () => {
    auth.permissions = new Set(['orders.view'])
    api.getTransportOrder.mockResolvedValue(order({ status: 'Draft' }))
    renderPage()
    await screen.findByText('ORD-0001 — Nexans NV')

    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Gebruik als sjabloon' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Klant van deze opdracht wijzigen' })).not.toBeInTheDocument()
    // Document downloads are not permission-gated (unchanged from before the redesign).
    expect(within(header()).getByRole('button', { name: 'CMR' })).toBeInTheDocument()
    expect(within(header()).getByRole('button', { name: 'Leveringsbon' })).toBeInTheDocument()
  })
})
