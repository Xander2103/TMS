import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TransportOrderDetailPage } from '../TransportOrderDetailPage'
import type { TransportOrderDetail, TransportOrderStatus } from '../../types'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['orders.edit', 'orders.delete']) }))

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
  deleteTransportOrder: vi.fn(),
}))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getTransportOrder: api.getTransportOrder,
  deleteTransportOrder: api.deleteTransportOrder,
}))

function baseOrder(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return {
    id: 'order-1',
    orderNumber: 'ORD-0001',
    orderDate: '2026-07-27',
    customerId: 'cust-1',
    customerName: 'Klant X',
    customerReference: null,
    status: 'Draft',
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
    pricingLines: [],
    serviceLines: [],
    pricingSnapshot: null,
    pricingSource: 'Contract',
    oneOffFixedAmount: null,
    oneOffIncludedLoadingMinutes: null,
    oneOffIncludedUnloadingMinutes: null,
    oneOffIncludedCombinedMinutes: null,
    oneOffExtraHourlyRate: null,
    oneOffNotes: null,
    totalWithProposed: 100,
    includedLoadingMinutesOverride: null,
    includedUnloadingMinutesOverride: null,
    extraTimeHourlyRateOverride: null,
    extraTimeRoundingStepMinutes: null,
    extraTimeMinimumBillableMinutes: null,
    version: 'v1',
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
  auth.permissions = new Set(['orders.edit', 'orders.delete'])
  api.getTransportOrder.mockReset()
  api.deleteTransportOrder.mockReset()
  api.getTransportOrder.mockResolvedValue(baseOrder())
})

describe('TransportOrderDetailPage top-level actions', () => {
  it('renders Bewerken and Verwijderen twice (header + bottom) for a Draft order with both permissions', async () => {
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    expect(screen.getAllByRole('button', { name: 'Bewerken' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Verwijderen' })).toHaveLength(2)
  })

  it('hides both header buttons without orders.edit/orders.delete permissions', async () => {
    auth.permissions = new Set<string>()
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
  })

  it('hides Bewerken for status Completed (not editable)', async () => {
    api.getTransportOrder.mockResolvedValue(baseOrder({ status: 'Completed' as TransportOrderStatus }))
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })

  it('clicking the header Verwijderen shows a dialog containing the order number and customer name', async () => {
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    const [headerDelete] = screen.getAllByRole('button', { name: 'Verwijderen' })
    await userEvent.click(headerDelete)

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('ORD-0001')
    expect(dialog).toHaveTextContent('Klant X')
  })
})
