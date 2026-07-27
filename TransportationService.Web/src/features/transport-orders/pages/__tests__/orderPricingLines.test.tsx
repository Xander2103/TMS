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
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
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

describe('TransportOrderDetailPage pricing lines', () => {
  it('renders a badge per line kind', async () => {
    renderPage()
    await screen.findByText('Basisregel')

    expect(screen.getByText('AUTO')).toBeInTheDocument()
    expect(screen.getByText('AANGEPAST')).toBeInTheDocument()
    expect(screen.getByText('MANUEEL')).toBeInTheDocument()
    expect(screen.getByText('VOORSTEL')).toBeInTheDocument()
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
