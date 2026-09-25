import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossierActivityPricePanel } from '../components/DossierActivityPricePanel'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import { PriceStatusBadge } from '../pricing/PriceStatusBadge'
import type { DossierActivityPriceLine } from '../pricing/types'
import type { DossierActivity, DossierDetail } from '../types'
import type { CargoItem, TransportOrderDetail } from '../../transport-orders/types'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'

/**
 * Master sprint 2026-09-21 (spec §15, contract 4.4) — price per activity on the dossier price tab:
 * the always-visible activity picker, sales lines of a standalone activity (server amounts win,
 * free / remove, 409 keeps the typed rows), the shared order line editor with the goods link, the
 * commercial coverage table, and the workspace sync after every save.
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

const lines = vi.hoisted(() => ({ save: vi.fn() }))
vi.mock('../pricing/activityPriceLinesApi', () => ({ saveActivityPriceLines: lines.save }))

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
  useLookupOptions: () => ({ options: [{ id: 'u-1', code: 'EUROPALLET', name: 'Europallet' }], isLoading: false, error: null }),
}))

function renderPage(initialPath = '/dossiers/d-1/prijs') {
  const router = createMemoryRouter([{ path: '/dossiers/:id/:section?', element: <DossierDetailPage /> }], {
    initialEntries: [initialPath],
  })
  return { ...render(<RouterProvider router={router} />), router }
}

const priceSection = () => document.getElementById('sectie-prijs')!
const picker = () => screen.getByRole('group', { name: 'Eenheid' })

const noFinancials = { agreedOrderTotal: 0, invoicedTotal: 0, estimatedIncidentCost: 0, actualIncidentCost: 0 }

function plateau(overrides: Partial<DossierActivity> = {}): DossierActivity {
  return dossierActivity({
    id: 'a-p', sequence: 2, activityTypeCode: 'PLATEAU', activityTypeName: 'Plateau', hasStops: false, supportsGoods: false,
    priceStatus: 'NotPriced', priceLines: [], freeConfirmed: false, ...overrides,
  })
}

/** A dossier whose ONLY commercial activity is a standalone Plateau — the case that used to hide the picker. */
function plateauOnly(activity: DossierActivity = plateau(), total: number | null = null): DossierDetail {
  return dossierDetail({
    financials: { ...noFinancials, agreedOrderTotal: total ?? 0, billableActivityCount: 1, pricedActivityCount: total === null ? 0 : 1, zeroPricedActivityCount: 0 },
    activities: [activity],
  })
}

const line = (id: string, sequence: number, label: string, quantity: number, unitPrice: number, amount: number): DossierActivityPriceLine => ({
  id, sequence, label, quantity, unit: null, unitPrice, amount, salesCategoryId: null,
})

/** ORD-0005 (Direct transport, € 102,37) + ORD-0006 (Distributie, unpriced) + Plateau (unpriced). */
function mixedDossier(): DossierDetail {
  return dossierDetail({
    orders: [
      { linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0005', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: 102.37, isPriced: true },
      { linkId: 'l-2', orderId: 'o-2', orderNumber: 'ORD-0006', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: 0, isPriced: false },
    ],
    financials: { ...noFinancials, agreedOrderTotal: 102.37, pricedOrderCount: 1, billableActivityCount: 3, pricedActivityCount: 1, zeroPricedActivityCount: 0 },
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0005', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 102.37, priceStatus: 'Priced' }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'DISTRIBUTIE', activityTypeName: 'Distributie', linkedTransportOrderId: 'o-2', linkedOrderNumber: 'ORD-0006', linkedOrderStatus: 'Draft', agreedPrice: 0, priceStatus: 'NotPriced' }),
      plateau({ sequence: 3 }),
    ],
  })
}

describe('Price per activity — the picker is always there', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.price', 'orders.edit', 'orders.override_price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    orders.get.mockImplementation((id: string) =>
      Promise.resolve(orderDetail({ id, orderNumber: id === 'o-2' ? 'ORD-0006' : 'ORD-0005', status: 'Draft', agreedPrice: id === 'o-2' ? 0 : 102.37 })),
    )
  })

  it('shows the picker with a single commercial activity, its price text and the server status badge', async () => {
    api.getDossier.mockResolvedValue(plateauOnly())
    renderPage()
    const group = await screen.findByRole('group', { name: 'Eenheid' })
    const buttons = within(group).getAllByRole('button')
    expect(buttons).toHaveLength(1)
    expect(buttons[0]).toHaveAttribute('aria-pressed', 'true')
    expect(buttons[0]).toHaveTextContent('Plateau · Nog niet geprijsd')
    expect(within(buttons[0]).getByText('Niet geprijsd')).toBeInTheDocument()
    expect(within(priceSection()).queryByText(/€\s0,00/)).not.toBeInTheDocument()
  })

  it('lists every commercial activity as "number — type · price" and selecting Plateau offers a price and "+ Verkooplijn toevoegen"', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(mixedDossier())
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    const buttons = within(picker()).getAllByRole('button')
    expect(buttons.map((b) => b.textContent)).toEqual([
      expect.stringMatching(/^ORD-0005 — Direct transport · €\s102,37Geprijsd$/),
      'ORD-0006 — Distributie · Nog niet geprijsdNiet geprijsd',
      'Plateau · Nog niet geprijsdNiet geprijsd',
    ])

    await user.click(within(picker()).getByRole('button', { name: /Plateau/ }))
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByText('Activiteit Plateau')).toBeInTheDocument()
    expect(screen.getByText('Nog geen verkoopprijs ingesteld.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' })).toBeInTheDocument()
    // The quick flat price stays available next to the lines.
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toHaveValue('')
  })

  it('the amount block shows the server total, "1 van 3 activiteiten geprijsd" and the unpriced activities as links that select them', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(mixedDossier())
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    const total = priceSection().querySelector<HTMLElement>('.dossier-price-total')!
    expect(within(total).getByText(/€\s102,37/)).toBeInTheDocument()
    expect(within(total).getByText('1 van 3 activiteiten geprijsd')).toBeInTheDocument()

    const toPrice = priceSection().querySelector<HTMLElement>('.dossier-price-toprice')!
    expect(within(toPrice).getAllByRole('button').map((b) => b.textContent)).toEqual(['ORD-0006 — Distributie', 'Plateau'])
    await user.click(within(toPrice).getByRole('button', { name: 'Plateau' }))
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByText('Activiteit Plateau')).toBeInTheDocument()
  })

  it('a deep link ?activiteit=<id> lands on that activity', async () => {
    api.getDossier.mockResolvedValue(mixedDossier())
    renderPage('/dossiers/d-1/prijs?activiteit=a-p')
    expect(await screen.findByText('Activiteit Plateau')).toBeInTheDocument()
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveAttribute('aria-pressed', 'true')
  })

  /** ORD-0005 (transport, priced) + a standalone Plateau: the Plateau is the ONLY standalone priceable unit. */
  function transportPlusPlateau(plateauOverrides: Partial<DossierActivity> = {}): DossierDetail {
    const plateauActivity = plateau({ sequence: 2, allowsDuration: true, linkedTransportOrderId: null, ...plateauOverrides })
    const plateauBillable = plateauActivity.isBillable !== false
    return dossierDetail({
      orders: [
        { linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0005', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: 102.37, isPriced: true },
      ],
      financials: {
        ...noFinancials, agreedOrderTotal: 102.37, pricedOrderCount: 1,
        billableActivityCount: plateauBillable ? 2 : 1, pricedActivityCount: 1, zeroPricedActivityCount: 0,
      },
      activities: [
        dossierActivity({ id: 'a-1', sequence: 1, hasStops: true, isBillable: true, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0005', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 102.37, priceStatus: 'Priced' }),
        plateauActivity,
      ],
    })
  }

  it('a billable Plateau next to ONE transport activity is offered in the picker with its NotPriced badge and prices as a standalone activity', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(transportPlusPlateau())
    renderPage()
    await screen.findByText('Opdracht ORD-0005')

    // Both commercial units are listed; the route target (the order) is pre-selected today.
    const buttons = within(picker()).getAllByRole('button')
    expect(buttons.map((b) => b.textContent)).toEqual([
      expect.stringMatching(/^ORD-0005 — Direct transport · €\s102,37Geprijsd$/),
      'Plateau · Nog niet geprijsdNiet geprijsd',
    ])
    const plateauButton = within(picker()).getByRole('button', { name: /Plateau/ })
    expect(within(plateauButton).getByText('Niet geprijsd')).toBeInTheDocument()
    expect(within(picker()).getByRole('button', { name: /ORD-0005/ })).toHaveAttribute('aria-pressed', 'true')
    expect(plateauButton).toHaveAttribute('aria-pressed', 'false')

    // Selecting it swaps the order editor for the standalone activity price panel.
    await user.click(plateauButton)
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByText('Activiteit Plateau')).toBeInTheDocument()
    expect(screen.getByText('Nog geen verkoopprijs ingesteld.')).toBeInTheDocument()
    expect(screen.queryByText('Opdracht ORD-0005')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Deze activiteit is gratis (€ 0) — bevestigen' })).toBeInTheDocument()
  })

  it('a NON-billable Plateau next to the transport activity is not offered at all', async () => {
    api.getDossier.mockResolvedValue(transportPlusPlateau({ isBillable: false, priceStatus: null }))
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    const buttons = within(picker()).getAllByRole('button')
    expect(buttons).toHaveLength(1)
    expect(buttons[0]).toHaveTextContent(/^ORD-0005 — Direct transport/)
    expect(within(picker()).queryByRole('button', { name: /Plateau/ })).not.toBeInTheDocument()
    expect(within(priceSection()).queryByText('Activiteit Plateau')).not.toBeInTheDocument()
  })
})

describe('Price per activity — sales lines of a standalone activity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  async function addRow(user: ReturnType<typeof userEvent.setup>, index: number, label: string, unitPrice: string, quantity?: string) {
    await user.click(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' }))
    await user.type(screen.getByLabelText(`Omschrijving lijn ${index}`), label)
    if (quantity !== undefined) {
      await user.clear(screen.getByLabelText(`Aantal lijn ${index}`))
      await user.type(screen.getByLabelText(`Aantal lijn ${index}`), quantity)
    }
    await user.type(screen.getByLabelText(`Eenheidsprijs lijn ${index}`), unitPrice)
  }

  it('Transport € 100 + Wachttijd € 25: sends both lines with the version, then shows the SERVER total € 125,00 everywhere without a refetch', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(plateauOnly(plateau({ pricingVersion: 'pv-1' })))
    const saved = plateauOnly(
      plateau({
        pricingSource: 'Lines', isPriced: true, agreedPrice: 125, pricingStatus: 'Draft', pricingVersion: 'pv-2', priceStatus: 'Priced',
        priceLines: [line('pl-1', 1, 'Transport', 1, 100, 100), line('pl-2', 2, 'Wachttijd', 1, 25, 25)],
      }),
      125,
    )
    lines.save.mockResolvedValue(saved)
    renderPage()
    await screen.findByText('Nog geen verkoopprijs ingesteld.')

    await addRow(user, 1, 'Transport', '100')
    await addRow(user, 2, 'Wachttijd', '25')
    // While editing the amounts are a preview.
    expect(screen.getByText('Voorlopig subtotaal (voorbeeld)')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Verkooplijnen opslaan' }))

    await waitFor(() => expect(lines.save).toHaveBeenCalledTimes(1))
    expect(lines.save).toHaveBeenCalledWith('d-1', 'a-p', {
      version: 'pv-1',
      lines: [
        { label: 'Transport', quantity: 1, unit: null, unitPrice: 100 },
        { label: 'Wachttijd', quantity: 1, unit: null, unitPrice: 25 },
      ],
      freeConfirmed: false,
    })

    // The returned dossier replaced the workspace state: total block, picker and editor agree.
    const total = priceSection().querySelector<HTMLElement>('.dossier-price-total')!
    expect(await within(total).findByText(/€\s125,00/)).toBeInTheDocument()
    expect(within(total).getByText('1 van 1 activiteiten geprijsd')).toBeInTheDocument()
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveTextContent(/Plateau · €\s125,00Geprijsd/)
    expect(screen.getByText('Subtotaal')).toBeInTheDocument()
    expect(screen.queryByText('Voorlopig subtotaal (voorbeeld)')).not.toBeInTheDocument()
    expect(screen.queryByText('Nog geen verkoopprijs ingesteld.')).not.toBeInTheDocument()
    expect(api.getDossier).toHaveBeenCalledTimes(1)
  })

  it('the client-side amount is only a preview: after saving the server amounts and total win', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(plateauOnly())
    // The server is the only authority on amounts: it answers 99,50 where the client previewed 3 × 33 = 99,00.
    lines.save.mockResolvedValue(
      plateauOnly(plateau({ pricingSource: 'Lines', isPriced: true, agreedPrice: 99.5, pricingVersion: 'pv-2', priceStatus: 'Priced', priceLines: [line('pl-1', 1, 'Uren', 3, 33, 99.5)] }), 99.5),
    )
    renderPage()
    await screen.findByText('Nog geen verkoopprijs ingesteld.')
    await addRow(user, 1, 'Uren', '33,00', '3')
    const table = priceSection().querySelector<HTMLElement>('.dossier-activity-lines-table')!
    expect(within(table).getAllByText(/€\s99,00/)).toHaveLength(2) // preview: line amount + provisional subtotal

    await user.click(screen.getByRole('button', { name: 'Verkooplijnen opslaan' }))
    await waitFor(() => expect(lines.save).toHaveBeenCalledTimes(1))
    // The amount itself is never sent — only quantity and unit price.
    expect(lines.save.mock.calls[0][2].lines).toEqual([{ label: 'Uren', quantity: 3, unit: null, unitPrice: 33 }])
    await waitFor(() => expect(within(table).getAllByText(/€\s99,50/)).toHaveLength(2)) // line amount + subtotal, both from the server
    expect(within(table).queryByText(/€\s99,00/)).not.toBeInTheDocument()
  })

  it('an invalid row is refused inline and nothing is sent', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(plateauOnly())
    renderPage()
    await screen.findByText('Nog geen verkoopprijs ingesteld.')
    await addRow(user, 1, 'Transport', 'abc')
    await user.click(screen.getByRole('button', { name: 'Verkooplijnen opslaan' }))
    expect(await screen.findByText('Lijn 1: geef een geldige eenheidsprijs op (0 of meer).')).toBeInTheDocument()
    expect(lines.save).not.toHaveBeenCalled()
  })

  it('free confirmation sends an empty list with freeConfirmed: true and shows "Gratis"', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(plateauOnly(plateau({ pricingVersion: 'pv-1' })))
    lines.save.mockResolvedValue(
      plateauOnly(plateau({ isPriced: true, agreedPrice: 0, pricingVersion: 'pv-2', priceStatus: 'Free', freeConfirmed: true }), 0),
    )
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Deze activiteit is gratis (€ 0) — bevestigen' }))

    await waitFor(() => expect(lines.save).toHaveBeenCalledWith('d-1', 'a-p', { version: 'pv-1', lines: [], freeConfirmed: true }))
    expect(await screen.findByText('Deze activiteit is bevestigd als gratis (€ 0).')).toBeInTheDocument()
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveTextContent('Plateau · GratisGratis')
    // A confirmed free activity is settled: no "is € 0 deliberate?" warning.
    expect(within(priceSection()).queryByRole('note')).not.toBeInTheDocument()
  })

  it('"Prijs verwijderen" asks first, sends freeConfirmed: false and shows "Nog niet geprijsd" — never € 0,00', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(
      plateauOnly(
        plateau({ pricingSource: 'Lines', isPriced: true, agreedPrice: 125, pricingVersion: 'pv-2', priceStatus: 'Priced', priceLines: [line('pl-1', 1, 'Transport', 1, 125, 125)] }),
        125,
      ),
    )
    lines.save.mockResolvedValue(plateauOnly(plateau({ pricingVersion: 'pv-3' })))
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Prijs verwijderen' }))
    expect(lines.save).not.toHaveBeenCalled()
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Prijs verwijderen' }))

    await waitFor(() => expect(lines.save).toHaveBeenCalledWith('d-1', 'a-p', { version: 'pv-2', lines: [], freeConfirmed: false }))
    expect(await screen.findByText('Nog geen verkoopprijs ingesteld.')).toBeInTheDocument()
    expect(within(picker()).getByRole('button', { name: /Plateau/ })).toHaveTextContent('Plateau · Nog niet geprijsd')
    expect(screen.getByText('Nog geen prijs')).toBeInTheDocument()
    expect(within(priceSection()).queryByText(/€\s0,00/)).not.toBeInTheDocument()
  })

  it('409 keeps the typed rows, reloads the dossier (fresh version) and shows the conflict; the retry uses the new version', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(plateauOnly(plateau({ pricingVersion: 'pv-1' })))
    const colleague = plateauOnly(plateau({ pricingSource: 'OneOff', isPriced: true, agreedPrice: 99, pricingVersion: 'pv-9', priceStatus: 'Priced' }), 99)
    lines.save.mockRejectedValueOnce(new ApiError('Conflict', 409, colleague))
    renderPage()
    await screen.findByText('Nog geen verkoopprijs ingesteld.')
    await addRow(user, 1, 'Transport', '100')
    await user.click(screen.getByRole('button', { name: 'Verkooplijnen opslaan' }))

    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((a) => /intussen gewijzigd door een collega/.test(a.textContent ?? ''))).toBe(true)
    // Nothing the planner typed is gone …
    expect(screen.getByLabelText('Omschrijving lijn 1')).toHaveValue('Transport')
    expect(screen.getByLabelText('Eenheidsprijs lijn 1')).toHaveValue('100')
    // … and the colleague's state is on screen around it.
    const total = priceSection().querySelector<HTMLElement>('.dossier-price-total')!
    expect(within(total).getByText(/€\s99,00/)).toBeInTheDocument()

    lines.save.mockResolvedValueOnce(plateauOnly(plateau({ pricingVersion: 'pv-10' })))
    await user.click(screen.getByRole('button', { name: 'Verkooplijnen opslaan' }))
    await waitFor(() => expect(lines.save).toHaveBeenCalledTimes(2))
    expect(lines.save.mock.calls[1][2].version).toBe('pv-9')
  })

  it('locks the picker while sales lines are unsaved, and releases it after "Wijzigingen ongedaan maken"', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.price', 'orders.edit'])
    orders.get.mockResolvedValue(orderDetail({ status: 'Draft' }))
    api.getDossier.mockResolvedValue(mixedDossier())
    renderPage('/dossiers/d-1/prijs?activiteit=a-p')
    await screen.findByText('Activiteit Plateau')
    await addRow(user, 1, 'Transport', '100')
    expect(within(picker()).getByRole('button', { name: /ORD-0005/ })).toBeDisabled()
    expect(screen.getByText('Sla de verkooplijnen op of maak de wijzigingen ongedaan om van activiteit te wisselen.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Wijzigingen ongedaan maken' }))
    expect(within(picker()).getByRole('button', { name: /ORD-0005/ })).toBeEnabled()
  })

  it('is read-only without dossiers.price, with a short explanation', async () => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    api.getDossier.mockResolvedValue(
      plateauOnly(plateau({ pricingSource: 'Lines', isPriced: true, agreedPrice: 125, priceStatus: 'Priced', priceLines: [line('pl-1', 1, 'Transport', 1, 125, 125)] }), 125),
    )
    renderPage()
    expect(await screen.findByText('Je hebt geen recht om activiteitprijzen te wijzigen.')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Transport' })).toBeInTheDocument()
    expect(within(priceSection()).queryAllByRole('textbox')).toHaveLength(0)
    for (const name of ['+ Verkooplijn toevoegen', 'Prijs verwijderen', 'Deze activiteit is gratis (€ 0) — bevestigen', 'Verkooplijnen opslaan']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
    }
  })

  it('a Locked price is read-only with the reason shown', async () => {
    api.getDossier.mockResolvedValue(
      plateauOnly(
        plateau({ pricingSource: 'Lines', isPriced: true, agreedPrice: 125, pricingStatus: 'Locked', priceStatus: 'Priced', priceLines: [line('pl-1', 1, 'Transport', 1, 125, 125)] }),
        125,
      ),
    )
    renderPage()
    expect(await screen.findByText(/De prijs van deze activiteit is vergrendeld/)).toBeInTheDocument()
    expect(within(priceSection()).queryAllByRole('textbox')).toHaveLength(0)
    expect(screen.queryByRole('button', { name: '+ Verkooplijn toevoegen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Prijs verwijderen' })).not.toBeInTheDocument()
  })

  it('hands the saved dossier to the workspace (applyDossier is called with the response)', async () => {
    const user = userEvent.setup()
    const activity = plateau({ pricingVersion: 'pv-1' })
    const response = plateauOnly(plateau({ isPriced: true, agreedPrice: 0, pricingVersion: 'pv-2', priceStatus: 'Free', freeConfirmed: true }), 0)
    lines.save.mockResolvedValue(response)
    const applyDossier = vi.fn()
    render(<DossierActivityPricePanel dossier={plateauOnly(activity)} activity={activity} canEdit onDossierUpdated={applyDossier} onConflict={() => false} />)
    await user.click(screen.getByRole('button', { name: 'Deze activiteit is gratis (€ 0) — bevestigen' }))
    await waitFor(() => expect(applyDossier).toHaveBeenCalledTimes(1))
    expect(applyDossier).toHaveBeenCalledWith(response)
  })
})

describe('Price per activity — the server status is rendered, never derived', () => {
  it.each([
    ['NotPriced', 'Niet geprijsd'],
    ['PartiallyPriced', 'Gedeeltelijk geprijsd'],
    ['Priced', 'Geprijsd'],
    ['Free', 'Gratis'],
  ] as const)('%s → "%s"', (status, label) => {
    render(<PriceStatusBadge status={status} />)
    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('renders nothing for null (non-billable), absent (older payload) or an unknown value', () => {
    const { container, rerender } = render(<PriceStatusBadge status={null} />)
    expect(container).toBeEmptyDOMElement()
    rerender(<PriceStatusBadge status={undefined} />)
    expect(container).toBeEmptyDOMElement()
    rerender(<PriceStatusBadge status={'Future' as never} />)
    expect(container).toBeEmptyDOMElement()
  })
})

// ---------------------------------------------------------------- order-backed activities

const cargo = (id: string, sequence: number, description: string, extra: Partial<CargoItem> = {}): CargoItem => ({
  ...orderDetail().cargoItems[0], id, sequence, description, ...extra,
})

function transportDossier(): DossierDetail {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0005', orderDate: '2026-08-12', status: 'Draft', goodsDescription: null, agreedPrice: 100, isPriced: true }],
    financials: { ...noFinancials, agreedOrderTotal: 100, pricedOrderCount: 1, billableActivityCount: 1, pricedActivityCount: 1, zeroPricedActivityCount: 0 },
    activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0005', linkedOrderStatus: 'Draft', isPriced: true, agreedPrice: 100, priceStatus: 'Priced' })],
  })
}

function pricedOrder(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return orderDetail({
    orderNumber: 'ORD-0005',
    status: 'Draft',
    agreedPrice: 100,
    cargoItems: [cargo('ci-1', 1, 'Kabelhaspels', { totalWeightKg: 480 }), cargo('ci-2', 2, 'Kisten', { expectedQuantity: 4 })],
    pricingLines: [{ id: 'pl-1', label: 'Transport', amount: 100, source: 'Manueel', informational: false, kind: 'Manual', quantity: 1, unitPrice: 100, lineKey: 'manual:1' }],
    ...overrides,
  })
}

describe('Price per activity — an order-backed activity uses THE order price line editor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.edit', 'orders.override_price'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(transportDossier())
    orders.get.mockResolvedValue(pricedOrder())
  })

  it('renders the shared editor (total, price lines, line actions) inside the dossier tab', async () => {
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    expect(screen.getByText('Totaalprijs')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Prijsregels/ })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Transport' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Bewerken' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Verwijderen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' })).toBeInTheDocument()
    // The agreed (one-off) price stays available as the manual price.
    expect(screen.getByLabelText('Afgesproken prijs (€)')).toBeInTheDocument()
  })

  it('a line without a lineKey (the engine\'s "geen tarief" diagnostic) offers no edit/remove — the API could only read it as a new line', async () => {
    // Found in the browser: ticking goods on such a line was sent with lineKey null and refused
    // with "Geef een bedrag op, of een aantal en eenheidsprijs."
    orders.get.mockResolvedValue(
      pricedOrder({
        pricingLines: [{ id: 'pl-9', label: 'Geen tarief geconfigureerd voor Europallet', amount: 0, source: 'Ontbrekend', informational: false, kind: 'Auto', lineKey: null }],
      }),
    )
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    expect(screen.getByRole('cell', { name: 'Geen tarief geconfigureerd voor Europallet' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' })).toBeInTheDocument()
  })

  it('a new sales line can be linked to goods lines: the selection is sent as cargoItemIds, and the order save syncs the dossier', async () => {
    const user = userEvent.setup()
    orders.saveLines.mockResolvedValue(
      pricedOrder({
        agreedPrice: 125,
        version: 'ov-2',
        pricingLines: [
          ...pricedOrder().pricingLines!,
          { id: 'pl-2', label: 'Wachttijd', amount: 25, source: 'Manueel', informational: false, kind: 'Manual', quantity: 1, unitPrice: 25, lineKey: 'manual:2', cargoItemIds: ['ci-2'] },
        ],
      }),
    )
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    await user.click(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' }))
    const dialog = screen.getByRole('dialog')
    const goods = within(dialog).getByRole('group', { name: 'Heeft betrekking op goederen' })
    expect(within(goods).getByLabelText('Kabelhaspels · 2 Europallet · 480 kg')).not.toBeChecked()
    await user.type(within(dialog).getByLabelText(/Omschrijving/), 'Wachttijd')
    await user.type(within(dialog).getByLabelText('Aantal'), '1')
    await user.type(within(dialog).getByLabelText('Eenheidsprijs (€)'), '25')
    await user.click(within(goods).getByLabelText('Kisten · 4 Europallet'))
    await user.click(within(dialog).getByRole('button', { name: 'Toevoegen' }))

    await waitFor(() => expect(orders.saveLines).toHaveBeenCalledTimes(1))
    expect(orders.saveLines).toHaveBeenCalledWith('o-1', [
      { lineKey: null, label: 'Wachttijd', quantity: 1, unitPrice: 25, unit: null, amount: null, adjustReason: null, cargoItemIds: ['ci-2'] },
    ])
    // handleOrderSaved: the fresh order is shown and the dossier is re-read for totals/readiness.
    expect(await screen.findByText('Goederen: Kisten · 4 Europallet')).toBeInTheDocument()
    await waitFor(() => expect(api.getDossier).toHaveBeenCalledTimes(2))
  })

  it('a line without goods ticked sends no cargoItemIds (= the whole trip/activity)', async () => {
    const user = userEvent.setup()
    orders.saveLines.mockResolvedValue(pricedOrder({ version: 'ov-2' }))
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    await user.click(screen.getByRole('button', { name: '+ Verkooplijn toevoegen' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Omschrijving/), 'Toeslag')
    await user.type(within(dialog).getByLabelText('Aantal'), '1')
    await user.type(within(dialog).getByLabelText('Eenheidsprijs (€)'), '10')
    await user.click(within(dialog).getByRole('button', { name: 'Toevoegen' }))
    await waitFor(() => expect(orders.saveLines).toHaveBeenCalledTimes(1))
    expect(orders.saveLines.mock.calls[0][1][0]).not.toHaveProperty('cargoItemIds')
  })

  it('changing ONLY the goods link of an existing line needs no reason and sends no price fields', async () => {
    const user = userEvent.setup()
    orders.saveLines.mockResolvedValue(pricedOrder({ version: 'ov-2' }))
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    await user.click(screen.getByRole('button', { name: 'Bewerken' }))
    const dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByLabelText('Kabelhaspels · 2 Europallet · 480 kg'))
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(orders.saveLines).toHaveBeenCalledTimes(1))
    expect(orders.saveLines).toHaveBeenCalledWith('o-1', [
      { lineKey: 'manual:1', label: 'Transport', quantity: null, unitPrice: null, amount: null, adjustReason: null, cargoItemIds: ['ci-1'] },
    ])
    expect(toast.showError).not.toHaveBeenCalled()
  })

  it('"Prijsdekking goederen" shows the three server badges and the explanation', async () => {
    orders.get.mockResolvedValue(
      pricedOrder({
        cargoItems: [
          cargo('ci-1', 1, 'Kabelhaspels', { commercialCoverage: 'SeparatelyPriced' }),
          cargo('ci-2', 2, 'Kisten', { commercialCoverage: 'Included' }),
          cargo('ci-3', 3, 'Rollen', { commercialCoverage: 'ToReview' }),
        ],
      }),
    )
    renderPage()
    const heading = await screen.findByRole('heading', { name: 'Prijsdekking goederen' })
    const block = heading.closest<HTMLElement>('.order-cargo-coverage')!
    const rows = within(block).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent('Kabelhaspels')
    expect(within(rows[0]).getByText('Afzonderlijk geprijsd')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Inbegrepen in rit- of activiteitprijs')).toBeInTheDocument()
    expect(within(rows[2]).getByText('Nog te controleren')).toBeInTheDocument()
    expect(within(block).getByText(/wordt niet afzonderlijk gefactureerd/)).toBeInTheDocument()
  })

  it('renders no coverage table — and guesses nothing — when the server sends no coverage', async () => {
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    expect(screen.queryByRole('heading', { name: 'Prijsdekking goederen' })).not.toBeInTheDocument()
    for (const label of ['Afzonderlijk geprijsd', 'Inbegrepen in rit- of activiteitprijs', 'Nog te controleren']) {
      expect(screen.queryByText(label)).not.toBeInTheDocument()
    }
  })

  it('an unpriced order never reads as € 0,00 in the shared editor', async () => {
    const dossier = transportDossier()
    dossier.activities[0] = { ...dossier.activities[0], isPriced: false, agreedPrice: 0, priceStatus: 'NotPriced' }
    dossier.orders[0] = { ...dossier.orders[0], agreedPrice: 0, isPriced: false }
    dossier.financials = { ...dossier.financials, agreedOrderTotal: 0, pricedOrderCount: 0, pricedActivityCount: 0 }
    api.getDossier.mockResolvedValue(dossier)
    orders.get.mockResolvedValue(pricedOrder({ agreedPrice: 0, pricingLines: null }))
    renderPage()
    await screen.findByText('Opdracht ORD-0005')
    expect(screen.getByText('Nog geen verkoopprijs ingesteld.')).toBeInTheDocument()
    expect(within(priceSection()).queryByText(/€\s0,00/)).not.toBeInTheDocument()
  })

  it('without an order the "Transportopdracht aanmaken" path stays', async () => {
    api.getDossier.mockResolvedValue(
      dossierDetail({
        financials: { ...noFinancials, billableActivityCount: 1, pricedActivityCount: 0 },
        activities: [dossierActivity({ id: 'a-1', linkedTransportOrderId: null, priceStatus: 'NotPriced' })],
      }),
    )
    renderPage()
    expect(await screen.findByRole('button', { name: 'Transportopdracht aanmaken' })).toBeInTheDocument()
    expect(within(picker()).getByRole('button', { name: /Direct transport/ })).toHaveTextContent('Direct transport (nog geen opdracht) · Nog niet geprijsd')
  })
})
