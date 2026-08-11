import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TransportOrderDetailPage } from '../TransportOrderDetailPage'
import type { CargoItem, TransportOrderDetail } from '../../types'

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
  useLookupOptions: () => ({
    options: [
      { id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' },
      { id: 'u-colli', code: 'COLLI', name: 'Colli' },
      { id: 'u-box', code: 'BOX', name: 'Box' },
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
}))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getTransportOrder: api.getTransportOrder,
}))

function cargoItem(overrides: Partial<CargoItem>): CargoItem {
  return {
    id: 'cargo-1',
    sequence: 1,
    description: null,
    barcode: null,
    expectedQuantity: 1,
    quantityUnit: null,
    quantityUnitCode: null,
    notes: null,
    unitType: null,
    unitTypeLabel: null,
    totalWeightKg: null,
    weightPerUnitKg: null,
    lengthMeters: null,
    widthMeters: null,
    heightMeters: null,
    volumeM3: null,
    volumeIsManual: false,
    adrRequired: false,
    adrDetails: null,
    stackable: true,
    reference: null,
    loadingStopId: null,
    unloadingStopId: null,
    palletCount: null,
    ...overrides,
  }
}

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
})

describe('TransportOrderDetailPage Lading aggregation', () => {
  it('aggregates cargo lines by unit into a "Lading" list when cargo lines exist', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        cargoItems: [
          cargoItem({ id: 'c1', sequence: 1, expectedQuantity: 2, quantityUnitCode: 'EUROPALLET', palletCount: 2 }),
          cargoItem({ id: 'c2', sequence: 2, expectedQuantity: 4, quantityUnitCode: 'COLLI' }),
          cargoItem({ id: 'c3', sequence: 3, expectedQuantity: 1, quantityUnitCode: 'BOX' }),
        ],
      }),
    )
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    expect(screen.getAllByText('Lading').length).toBeGreaterThanOrEqual(2) // section heading + <dt>
    const list = document.querySelector('.to-lading-list') as HTMLElement
    expect(list).toBeInTheDocument()
    expect(within(list).getByText('2 Europallet')).toBeInTheDocument()
    expect(within(list).getByText('4 Colli')).toBeInTheDocument()
    expect(within(list).getByText('1 Box')).toBeInTheDocument()
    expect(screen.queryByText('Aantal')).not.toBeInTheDocument()
  })

  it('sums multiple lines sharing the same unit into a single aggregated entry', async () => {
    api.getTransportOrder.mockResolvedValue(
      baseOrder({
        cargoItems: [
          cargoItem({ id: 'c1', sequence: 1, expectedQuantity: 2, quantityUnitCode: 'EUROPALLET' }),
          cargoItem({ id: 'c2', sequence: 2, expectedQuantity: 3, quantityUnitCode: 'EUROPALLET' }),
        ],
      }),
    )
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    const list = document.querySelector('.to-lading-list') as HTMLElement
    expect(within(list).getByText('5 Europallet')).toBeInTheDocument()
  })

  it('falls back to the order-level "Aantal" row when there are no cargo lines', async () => {
    api.getTransportOrder.mockResolvedValue(baseOrder({ cargoItems: [] }))
    renderPage()
    await screen.findByText('ORD-0001 — Klant X')

    expect(screen.getByText('Aantal')).toBeInTheDocument()
    expect(document.querySelector('.to-lading-list')).not.toBeInTheDocument()
  })
})
