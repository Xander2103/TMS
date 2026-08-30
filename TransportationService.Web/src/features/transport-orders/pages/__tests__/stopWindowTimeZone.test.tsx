import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TransportOrderDetailPage } from '../TransportOrderDetailPage'
import { DossierRouteSummary } from '../../../dossiers/components/DossierRouteSummary'
import type { TransportOrderDetail, TransportOrderStop } from '../../types'

/**
 * C-03 — a stop window is rendered in the TENANT zone, on every surface, whatever zone the
 * machine runs in. The process zone is forced to America/New_York: a browser-zone regression
 * would print 02:00 here, a UTC one 06:00, and only the tenant reading gives 08:00.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['orders.view']) }))

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
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../components/OrderDocumentsPanel', () => ({ OrderDocumentsPanel: () => <div /> }))
vi.mock('../../components/OrderTimelinePanel', () => ({ OrderTimelinePanel: () => <div /> }))
vi.mock('../../components/StopExecutionPlanDialog', () => ({ StopExecutionPlanDialog: () => <div /> }))
vi.mock('../../../packages/components/OrderPackagesPanel', () => ({ OrderPackagesPanel: () => <div /> }))
vi.mock('../../../packages/components/CustomerPackagesSummary', () => ({ CustomerPackagesSummary: () => <div /> }))

const api = vi.hoisted(() => ({ getTransportOrder: vi.fn() }))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getTransportOrder: api.getTransportOrder,
}))

/** 08:00–10:00 Europe/Amsterdam on a summer day, as the API stores and serves it. */
const SUMMER_FROM = '2026-07-15T06:00:00Z'
const SUMMER_TO = '2026-07-15T08:00:00Z'

function stop(overrides: Partial<TransportOrderStop> = {}): TransportOrderStop {
  return {
    id: 'stop-1', sequence: 1, stopType: 'Loading',
    locationId: null, locationCode: null, locationName: 'Magazijn Antwerpen',
    address: 'Noorderlaan 10', postalCode: '2030', city: 'Antwerpen', countryCode: 'BE',
    plannedFrom: SUMMER_FROM, plannedTo: SUMMER_TO,
    requestedFrom: null, requestedTo: null, confirmedFrom: null, confirmedTo: null,
    earliestAllowed: null, latestAllowed: null,
    appointmentRequired: false, appointmentReference: null,
    reference: null, instructions: null,
    accessInstructions: null, loadingInstructions: null, unloadingInstructions: null,
    timeRequirement: 'None', timeRequirementFrom: null, timeRequirementTo: null,
    includedTimeMinutesOverride: null,
    ...overrides,
  }
}

function order(stops: TransportOrderStop[]): TransportOrderDetail {
  return {
    id: 'order-1', orderNumber: 'ORD-0001', orderDate: '2026-07-15',
    customerId: 'cust-1', customerName: 'Klant X', customerReference: null,
    status: 'Confirmed', goodsDescription: 'Pallets',
    quantity: null, quantityUnit: null, quantityUnitCode: null,
    weightKg: null, volumeM3: null, palletCount: null,
    adrRequired: false, craneRequired: false,
    agreedPrice: null, notes: null, cancellationReason: null,
    stops, cargoItems: [],
    allowedTransitions: [], allowedCorrections: [], canCancel: false,
    priority: 'Normal', legalEntityId: null,
    dieselSurchargeOverride: false, dieselSurchargePercentOverride: null,
    dieselSurchargeOverrideReason: null,
    calculatedPrice: null, priceIsManual: false, priceOverrideReason: null,
    pricingLines: [], serviceLines: [], pricingSnapshot: null,
    pricingSource: 'Contract',
    oneOffFixedAmount: null, oneOffIncludedLoadingMinutes: null,
    oneOffIncludedUnloadingMinutes: null, oneOffIncludedCombinedMinutes: null,
    oneOffExtraHourlyRate: null, oneOffNotes: null,
    totalWithProposed: null,
    includedLoadingMinutesOverride: null, includedUnloadingMinutesOverride: null,
    extraTimeHourlyRateOverride: null, extraTimeRoundingStepMinutes: null,
    extraTimeMinimumBillableMinutes: null,
    version: 'v1',
  } as unknown as TransportOrderDetail
}

function renderDetailPage() {
  return render(
    <MemoryRouter initialEntries={['/transport-orders/order-1']}>
      <Routes>
        <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = new Set(['orders.view'])
  api.getTransportOrder.mockReset()
})

describe('stop windows render in the tenant zone', () => {
  it('shows 08:00–10:00 for a 06:00Z–08:00Z summer window on the order detail page', async () => {
    api.getTransportOrder.mockResolvedValue(order([stop()]))
    renderDetailPage()

    expect(await screen.findByText('15/07/2026 08:00 – 15/07/2026 10:00')).toBeInTheDocument()
    // The browser zone would have produced these — they must be nowhere on the page.
    expect(screen.queryByText(/02:00/)).not.toBeInTheDocument()
    expect(screen.queryByText(/15\/07\/2026 06:00/)).not.toBeInTheDocument()
  })

  it('shows 08:00 for a 07:00Z winter window (the offset differs, the wall clock does not)', async () => {
    api.getTransportOrder.mockResolvedValue(order([
      stop({ plannedFrom: '2026-01-15T07:00:00Z', plannedTo: '2026-01-15T09:00:00Z' }),
    ]))
    renderDetailPage()

    expect(await screen.findByText('15/01/2026 08:00 – 15/01/2026 10:00')).toBeInTheDocument()
  })

  it('gives the dossier route summary the SAME hours as the order detail table', () => {
    render(
      <DossierRouteSummary order={order([stop()])} loading={false} canEdit={false} onEdit={() => {}} />,
    )

    expect(screen.getByText('15-07 · 08:00–10:00')).toBeInTheDocument()
  })

  it('renders the dossier summary of a winter window in the tenant zone too', () => {
    render(
      <DossierRouteSummary
        order={order([stop({ plannedFrom: '2026-01-15T07:00:00Z', plannedTo: '2026-01-15T09:00:00Z' })])}
        loading={false}
        canEdit={false}
        onEdit={() => {}}
      />,
    )

    expect(screen.getByText('15-01 · 08:00–10:00')).toBeInTheDocument()
  })

  it('hides the midnight marker of a date-only stop in the dossier summary', () => {
    render(
      <DossierRouteSummary
        order={order([stop({ plannedFrom: '2026-07-14T22:00:00Z', plannedTo: null })])}
        loading={false}
        canEdit={false}
        onEdit={() => {}}
      />,
    )

    expect(screen.getByText('15-07')).toBeInTheDocument()
  })
})
