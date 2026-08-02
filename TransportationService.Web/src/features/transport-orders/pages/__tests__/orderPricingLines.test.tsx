import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TransportOrderDetailPage } from '../TransportOrderDetailPage'
import type { TransportOrderDetail } from '../../types'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['orders.view', 'orders.override_price', 'orders.edit']) }))

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

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: toast.success, showError: toast.error }),
}))

vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [
      { id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' },
      { id: 'u-colli', code: 'COLLI', name: 'Colli' },
      { id: 'u-kg', code: 'KG', name: 'Kilogram' },
    ],
    isLoading: false,
    error: null,
  }),
}))
vi.mock('../../components/OrderDocumentsPanel', () => ({ OrderDocumentsPanel: () => <div /> }))
vi.mock('../../components/OrderTimelinePanel', () => ({ OrderTimelinePanel: () => <div /> }))
vi.mock('../../components/StopExecutionPlanDialog', () => ({ StopExecutionPlanDialog: () => <div /> }))
vi.mock('../../../packages/components/OrderPackagesPanel', () => ({ OrderPackagesPanel: () => <div /> }))
vi.mock('../../../packages/components/CustomerPackagesSummary', () => ({ CustomerPackagesSummary: () => <div /> }))

const api = vi.hoisted(() => ({
  getTransportOrder: vi.fn(),
  saveOrderPriceLines: vi.fn(),
  recalculateOrderPricing: vi.fn(),
  setOrderPricingStatus: vi.fn(),
  confirmOrderPriceLine: vi.fn(),
}))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getTransportOrder: api.getTransportOrder,
  saveOrderPriceLines: api.saveOrderPriceLines,
  recalculateOrderPricing: api.recalculateOrderPricing,
  setOrderPricingStatus: api.setOrderPricingStatus,
  confirmOrderPriceLine: api.confirmOrderPriceLine,
}))

function baseOrder(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return {
    id: 'order-1',
    orderNumber: 'ORD-0001',
    orderDate: '2026-07-27',
    customerId: 'cust-1',
    customerName: 'Klant X',
    customerReference: null,
    status: 'Confirmed',
    goodsDescription: 'Pallets',
    quantity: 3,
    quantityUnit: null,
    quantityUnitCode: 'EUROPALLET',
    weightKg: null,
    volumeM3: null,
    palletCount: null,
    adrRequired: false,
    craneRequired: false,
    agreedPrice: 100,
    notes: null,
    cancellationReason: null,
    stops: [],
    cargoItems: [],
    allowedTransitions: [],
    allowedCorrections: [],
    canCancel: false,
    priority: 'Normal',
    legalEntityId: null,
    dieselSurchargeOverride: false,
    dieselSurchargePercentOverride: null,
    dieselSurchargeOverrideReason: null,
    calculatedPrice: 100,
    priceIsManual: false,
    priceOverrideReason: null,
    pricingLines: [
      { id: 'line-auto', label: 'Basisregel', amount: 90, source: 'Regel X', informational: false, kind: 'Auto', lineKey: 'rule:r1', quantity: 3, unitPrice: 30 },
      { id: 'line-adjusted', label: 'Picking (8 pallets)', amount: 7.5, source: 'Automatisch (contract)', informational: false, kind: 'AutoAdjusted', lineKey: 'service:s1', quantity: 6, unitPrice: 1.25, originalQuantity: 8, originalUnitPrice: 1.25, originalAmount: 10, adjustReason: 'Hertelling' },
      { id: 'line-manual', label: 'Extra behandeling', amount: 35, source: 'Manueel', informational: false, kind: 'Manual', lineKey: 'manual:m1' },
      { id: 'line-proposed', label: 'Extra laadtijd', amount: 12.5, source: 'Extra tijd', informational: false, kind: 'Proposed', proposed: true, lineKey: 'extratime:loading' },
    ],
    serviceLines: [],
    pricingSnapshot: {
      tariffDate: '2026-07-27',
      currency: 'EUR',
      zoneCode: null,
      zoneName: null,
      agreementNames: null,
      unitSummary: null,
      calculatedTotal: 90,
      overrideAmount: null,
      overrideReason: null,
      overriddenByUserId: null,
      overriddenAtUtc: null,
      explanation: 'Basisregel: 90.00 EUR (Regel X)',
      status: 'Draft',
      linesTotal: 132.5,
    },
    pricingSource: 'Contract',
    oneOffFixedAmount: null,
    oneOffIncludedLoadingMinutes: null,
    oneOffIncludedUnloadingMinutes: null,
    oneOffIncludedCombinedMinutes: null,
    oneOffExtraHourlyRate: null,
    oneOffNotes: null,
    totalWithProposed: 145,
    includedLoadingMinutesOverride: null,
    includedUnloadingMinutesOverride: null,
    extraTimeHourlyRateOverride: null,
    extraTimeRoundingStepMinutes: null,
    extraTimeMinimumBillableMinutes: null,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/transport-orders/order-1']}>
      <Routes>
        <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = new Set(['orders.view', 'orders.override_price', 'orders.edit'])
  api.getTransportOrder.mockReset()
  api.saveOrderPriceLines.mockReset()
  api.recalculateOrderPricing.mockReset()
  api.setOrderPricingStatus.mockReset()
  api.confirmOrderPriceLine.mockReset()
  api.getTransportOrder.mockResolvedValue(baseOrder())
})

function money(amount: number): string {
  return amount.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
}

describe('TransportOrderDetailPage pricing lines', () => {
  it('renders a badge per line kind', async () => {
    renderPage()
    await screen.findByText('Basisregel')

    expect(screen.getByText('AUTO')).toBeInTheDocument()
    expect(screen.getByText('OVERRIDE')).toBeInTheDocument()
    expect(screen.getByText('MANUEEL')).toBeInTheDocument()
    expect(screen.getByText('VOORSTEL')).toBeInTheDocument()
  })

  it('renders the price table headers exactly: Omschrijving, Type, Berekening, Bedrag, Acties', async () => {
    renderPage()
    await screen.findByText('Basisregel')

    const table = screen.getByText('Basisregel').closest('table')!
    const thead = table.querySelector('thead')!
    const headers = within(thead).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers).toEqual(['Omschrijving', 'Type', 'Berekening', 'Bedrag', 'Acties'])
  })

  it('shows DIENST for an Auto line with a serviceOptionId, AUTO for one without', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        pricingLines: [
          { id: 'l1', label: 'Diensttarief', amount: 20, source: 'Dienst', informational: false, kind: 'Auto', lineKey: 'service:s2', serviceOptionId: 'svc-1' },
          { id: 'l2', label: 'Basistarief', amount: 30, source: 'Regel', informational: false, kind: 'Auto', lineKey: 'rule:r2' },
        ],
      }),
    )
    renderPage()
    await screen.findByText('Diensttarief')

    const dienstRow = screen.getByText('Diensttarief').closest('tr')!
    expect(within(dienstRow).getByText('DIENST')).toBeInTheDocument()
    const autoRow = screen.getByText('Basistarief').closest('tr')!
    expect(within(autoRow).getByText('AUTO')).toBeInTheDocument()
  })

  it('renders the Berekening cell: quantity x unit x unitPrice, "Vast bedrag" for manual-amount-only, "—" for auto without quantity/unitPrice', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        pricingLines: [
          { id: 'l1', label: 'Picking', amount: 3.75, source: 'Regel', informational: false, kind: 'Auto', lineKey: 'rule:r3', quantity: 3, unit: 'COLLI', unitPrice: 1.25 },
          { id: 'l2', label: 'Vaste kost', amount: 10, source: 'Manueel', informational: false, kind: 'Manual', lineKey: 'manual:m2' },
          { id: 'l3', label: 'Onbekende berekening', amount: 5, source: 'Regel', informational: false, kind: 'Auto', lineKey: 'rule:r4' },
        ],
      }),
    )
    renderPage()
    await screen.findByText('Picking')

    const cell = (label: string) => {
      const row = screen.getByText(label).closest('tr')!
      return within(row).getAllByRole('cell')[2]
    }
    expect(cell('Picking').textContent).toBe(`3 COLLI × ${money(1.25)}`)
    expect(cell('Vaste kost').textContent).toBe('Vast bedrag')
    expect(cell('Onbekende berekening').textContent).toBe('—')
  })

  it('shows "—" in Berekening (not a contradicting formula) when a stored quantity/unitPrice no longer reproduces the amount', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        pricingLines: [
          {
            id: 'l1',
            label: 'Auto line met bedrag-only aanpassing',
            amount: 50,
            source: 'Automatisch',
            informational: false,
            kind: 'AutoAdjusted',
            lineKey: 'service:s3',
            quantity: 3,
            unitPrice: 1.25,
          },
        ],
      }),
    )
    renderPage()
    await screen.findByText('Auto line met bedrag-only aanpassing')

    const row = screen.getByText('Auto line met bedrag-only aanpassing').closest('tr')!
    const cell = within(row).getAllByRole('cell')[2]
    expect(cell.textContent).toBe('—')
  })

  it('excludes zero-amount informational lines from the price table and lists them under "Niet toegepast"', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        pricingLines: [
          { id: 'l1', label: 'Basisregel', amount: 90, source: 'Regel X', informational: false, kind: 'Auto', lineKey: 'rule:r1', quantity: 3, unitPrice: 30 },
          { id: 'l2', label: 'Pipeline picking: geen Colli op deze order', amount: 0, source: 'Regel', informational: true, kind: 'Auto', lineKey: 'rule:r5' },
        ],
      }),
    )
    renderPage()
    await screen.findByText('Basisregel')

    expect(screen.queryByText('Pipeline picking: geen Colli op deze order')?.closest('tr')).toBeFalsy()
    const heading = screen.getByRole('heading', { name: 'Niet toegepast' })
    const list = heading.closest('div')!
    expect(within(list).getByText('Pipeline picking: geen Colli op deze order').tagName).toBe('LI')
  })

  it('renders an amount-bearing informational line (e.g. diesel surcharge) as a dimmed table row with its amount, not under "Niet toegepast"', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        pricingLines: [
          { id: 'l1', label: 'Basisregel', amount: 90, source: 'Regel X', informational: false, kind: 'Auto', lineKey: 'rule:r1', quantity: 3, unitPrice: 30 },
          {
            id: 'l2',
            label: 'Dieseltoeslag 8% (wordt bij facturatie toegevoegd)',
            amount: 7.2,
            source: 'Dieseltoeslag',
            informational: true,
            kind: 'Auto',
            lineKey: 'diesel',
          },
        ],
      }),
    )
    renderPage()
    await screen.findByText('Basisregel')

    const row = screen.getByText('Dieseltoeslag 8% (wordt bij facturatie toegevoegd)').closest('tr')!
    expect(row).toBeTruthy()
    expect(row.className).toContain('tof-price-informational')
    expect(within(row).getByText(`€ ${(7.2).toFixed(2)}`)).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Niet toegepast' })).not.toBeInTheDocument()
  })

  it('posts the adjusted quantity and reason when editing an auto line', async () => {
    api.saveOrderPriceLines.mockResolvedValue(baseOrder())
    renderPage()
    await screen.findByText('Basisregel')

    const row = screen.getByText('Basisregel').closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Bewerken' }))

    const qtyInput = screen.getByLabelText('Aantal') as HTMLInputElement
    await userEvent.clear(qtyInput)
    await userEvent.type(qtyInput, '2')
    await userEvent.type(screen.getByLabelText('Reden', { exact: false }), 'Correctie')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.saveOrderPriceLines).toHaveBeenCalledTimes(1))
    const [orderId, lines] = api.saveOrderPriceLines.mock.calls[0]
    expect(orderId).toBe('order-1')
    expect(lines).toEqual([
      expect.objectContaining({ lineKey: 'rule:r1', quantity: 2, adjustReason: 'Correctie' }),
    ])
  })

  it('confirms a proposed line via the dedicated endpoint', async () => {
    api.confirmOrderPriceLine.mockResolvedValue(baseOrder())
    renderPage()
    await screen.findByText('Extra laadtijd')

    const row = screen.getByText('Extra laadtijd').closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Bevestigen' }))

    await waitFor(() => expect(api.confirmOrderPriceLine).toHaveBeenCalledWith('order-1', 'line-proposed'))
  })

  it('hides the lock action without orders.lock_price, shows it and calls the status endpoint when granted', async () => {
    const { rerender } = renderPage()
    await screen.findByText('Basisregel')
    expect(screen.queryByRole('button', { name: 'Vergrendel prijs' })).not.toBeInTheDocument()

    auth.permissions = new Set(['orders.view', 'orders.override_price', 'orders.edit', 'orders.lock_price'])
    api.setOrderPricingStatus.mockResolvedValue(baseOrder({ pricingSnapshot: { ...baseOrder().pricingSnapshot!, status: 'Locked' } }))
    rerender(
      <MemoryRouter initialEntries={['/transport-orders/order-1']}>
        <Routes>
          <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
        </Routes>
      </MemoryRouter>,
    )
    await screen.findByRole('button', { name: 'Vergrendel prijs' })
    await userEvent.click(screen.getByRole('button', { name: 'Vergrendel prijs' }))

    await waitFor(() => expect(api.setOrderPricingStatus).toHaveBeenCalledWith('order-1', 'Locked'))
  })

  it('hides the recalculate action without orders.edit/orders.manage, shows it once granted', async () => {
    auth.permissions = new Set(['orders.view', 'orders.override_price'])
    const { rerender } = renderPage()
    await screen.findByText('Basisregel')
    expect(screen.queryByRole('button', { name: 'Herberekenen' })).not.toBeInTheDocument()

    auth.permissions = new Set(['orders.view', 'orders.override_price', 'orders.edit'])
    rerender(
      <MemoryRouter initialEntries={['/transport-orders/order-1']}>
        <Routes>
          <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
        </Routes>
      </MemoryRouter>,
    )
    await screen.findByRole('button', { name: 'Herberekenen' })
  })

  it('warns before recalculating when the price was already reviewed', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({ pricingSnapshot: { ...baseOrder().pricingSnapshot!, status: 'Reviewed' } }),
    )
    renderPage()
    await screen.findByText('Basisregel')

    await userEvent.click(screen.getByRole('button', { name: 'Herberekenen' }))
    expect(screen.getByText('De prijs is al gecontroleerd. Toch herberekenen?')).toBeInTheDocument()
    expect(api.recalculateOrderPricing).not.toHaveBeenCalled()

    api.recalculateOrderPricing.mockResolvedValue(baseOrder())
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Herberekenen' }))
    await waitFor(() => expect(api.recalculateOrderPricing).toHaveBeenCalledWith('order-1'))
  })
})

describe('TransportOrderDetailPage add price line modal — berekeningswijze', () => {
  async function openAddModal() {
    renderPage()
    await screen.findByText('Basisregel')
    await userEvent.click(screen.getByRole('button', { name: '+ Vrije regel' }))
  }

  it('shows a Berekeningswijze radio group defaulting to per-unit mode with the per-unit fields', async () => {
    await openAddModal()

    const perUnitRadio = screen.getByRole('radio', { name: 'Berekenen op basis van aantal en eenheidsprijs' })
    const fixedRadio = screen.getByRole('radio', { name: 'Vast bedrag' })
    expect(perUnitRadio).toBeChecked()
    expect(fixedRadio).not.toBeChecked()

    expect(screen.getByLabelText('Omschrijving', { exact: false })).toBeInTheDocument()
    expect(screen.getByLabelText('Aantal')).toBeInTheDocument()
    expect(screen.getByLabelText('Eenheid')).toBeInTheDocument()
    expect(screen.getByLabelText('Eenheidsprijs (€)')).toBeInTheDocument()
    expect(screen.getByLabelText('Totaalbedrag')).toBeInTheDocument()
    expect(screen.getByLabelText('Totaalbedrag')).toHaveValue('—')
  })

  it('computes a read-only total in per-unit mode and submits {quantity, unitPrice, unit, amount: null}', async () => {
    api.saveOrderPriceLines.mockResolvedValue(baseOrder())
    await openAddModal()

    await userEvent.type(screen.getByLabelText('Omschrijving', { exact: false }), 'Extra stop')
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'COLLI')
    await userEvent.type(screen.getByLabelText('Eenheidsprijs (€)'), '1.25')

    const expectedTotal = (3 * 1.25).toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
    const totalField = screen.getByLabelText('Totaalbedrag')
    expect(totalField).toHaveValue(expectedTotal)
    expect(totalField).toHaveAttribute('readonly')

    // Reden left empty on purpose: it is optional.
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))

    await waitFor(() => expect(api.saveOrderPriceLines).toHaveBeenCalledTimes(1))
    expect(toast.error).not.toHaveBeenCalled()
    const [orderId, lines] = api.saveOrderPriceLines.mock.calls[0]
    expect(orderId).toBe('order-1')
    expect(lines).toEqual([
      expect.objectContaining({ label: 'Extra stop', quantity: 3, unitPrice: 1.25, unit: 'COLLI', amount: null }),
    ])
  })

  it('hides Aantal/Eenheid/Eenheidsprijs in fixed mode, keeps Totaalbedrag editable, and submits {amount, quantity: null, unitPrice: null}', async () => {
    api.saveOrderPriceLines.mockResolvedValue(baseOrder())
    await openAddModal()

    await userEvent.type(screen.getByLabelText('Omschrijving', { exact: false }), 'Vaste kost')
    await userEvent.click(screen.getByRole('radio', { name: 'Vast bedrag' }))

    expect(screen.queryByLabelText('Aantal')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Eenheid')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Eenheidsprijs (€)')).not.toBeInTheDocument()

    const totalField = screen.getByLabelText('Totaalbedrag (€)')
    expect(totalField).not.toHaveAttribute('readonly')
    await userEvent.type(totalField, '10')

    // Reden left empty on purpose: it is optional in both modes.
    await userEvent.click(screen.getByRole('button', { name: 'Toevoegen' }))

    await waitFor(() => expect(api.saveOrderPriceLines).toHaveBeenCalledTimes(1))
    expect(toast.error).not.toHaveBeenCalled()
    const [, lines] = api.saveOrderPriceLines.mock.calls[0]
    expect(lines).toEqual([
      expect.objectContaining({ label: 'Vaste kost', amount: 10, quantity: null, unitPrice: null, unit: null }),
    ])
  })
})
