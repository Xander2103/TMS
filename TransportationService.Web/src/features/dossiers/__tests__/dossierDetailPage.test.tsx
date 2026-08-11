import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'
import type { DossierDetail } from '../types'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['dossiers.view', 'dossiers.manage']) }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

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

vi.mock('../api/activityTypesApi', () => ({
  listActivityTypes: () =>
    Promise.resolve([
      {
        id: 'at-1', code: 'DIRECT_TRANSPORT', name: 'Direct transport', isActive: true, sortOrder: 1,
        icon: 'truck', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: false, isQuickStart: true, quickStartOrder: 1,
        isSystemDefaultTransport: true,
      },
    ]),
}))

const getOrder = vi.hoisted(() => vi.fn())
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: getOrder,
  searchTransportOrders: () => Promise.resolve({ items: [], totalCount: 0 }),
}))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [], totalCount: 0 }),
}))
vi.mock('../../users/api/usersApi', () => ({
  getUsers: () => Promise.resolve([]),
}))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: () => Promise.resolve([]),
}))

function renderPage() {
  const router = createMemoryRouter([{ path: '/dossiers/:id', element: <DossierDetailPage /> }], {
    initialEntries: ['/dossiers/d-1'],
  })
  return render(<RouterProvider router={router} />)
}

/** Transportdossier: transportactiviteit met gekoppelde opdracht + kraanwerk met duur en begeleiding. */
function transportDossier(): DossierDetail {
  return dossierDetail({
    orders: [
      {
        linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-08-12',
        status: 'Confirmed', goodsDescription: '2 europallets', agreedPrice: 428.5,
      },
    ],
    activities: [
      dossierActivity({
        id: 'a-1', linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Confirmed',
      }),
      dossierActivity({
        id: 'a-2', activityTypeId: 'at-2', activityTypeCode: 'KRAANWERK', activityTypeName: 'Kraanwerk ter plaatse',
        icon: 'crane', hasStops: false, supportsGoods: false, allowsDuration: true, sequence: 2,
        durationHours: 2.5, linkedActivityId: 'a-1',
      }),
    ],
    readiness: [
      { code: 'route.unloading_missing', severity: 'Warning', message: 'Loslocatie is nog onbekend', section: 'route', field: null, stage: 'Planning' },
    ],
  })
}

describe('DossierDetailPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    getOrder.mockResolvedValue(orderDetail())
    api.listDossiers.mockResolvedValue([])
  })

  it('shows activity cards with order status, duration and accompaniment; route/goods sections follow capabilities', async () => {
    api.getDossier.mockResolvedValue(transportDossier())
    renderPage()

    expect(await screen.findByText('Direct transport')).toBeInTheDocument()
    // Ordernummer + status op de kaart (ook elders getoond: prijslijst / operationele chip).
    expect(screen.getAllByText('ORD-0001').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('Bevestigd').length).toBeGreaterThanOrEqual(1)
    // Standalone crane card: duration + accompaniment on the contextual line.
    expect(screen.getByText(/2,5 u/)).toBeInTheDocument()
    expect(screen.getByText(/Gekoppeld aan Direct transport/)).toBeInTheDocument()

    // Capability-driven sections (any hasStops → Route, any supportsGoods → Goederen).
    expect(screen.getByRole('heading', { name: 'Route' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Goederen' })).toBeInTheDocument()
    // Route summary from the linked order's stops.
    expect(await screen.findByText('Nexans site Antwerpen')).toBeInTheDocument()
    expect(screen.getByText('Nog te bepalen')).toBeInTheDocument() // geen losstop
  })

  it('renders no Route section for a storage-only dossier', async () => {
    api.getDossier.mockResolvedValue(
      dossierDetail({
        activities: [
          dossierActivity({
            id: 'a-1', activityTypeCode: 'OPSLAG', activityTypeName: 'Opslag', hasStops: false,
            supportsGoods: true, icon: 'warehouse',
          }),
        ],
      }),
    )
    renderPage()

    expect(await screen.findByText('Opslag')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Route' })).not.toBeInTheDocument()
    expect(getOrder).not.toHaveBeenCalled()
  })

  it('still lists direct-linked orders of a legacy dossier (no activities)', async () => {
    api.getDossier.mockResolvedValue(
      dossierDetail({
        activities: [],
        orders: [
          {
            linkId: 'l-9', orderId: 'o-9', orderNumber: 'ORD-0099', orderDate: '2026-05-01',
            status: 'Completed', goodsDescription: 'Historische opdracht', agreedPrice: 100,
          },
        ],
      }),
    )
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Gekoppelde opdrachten' })).toBeInTheDocument()
    expect(screen.getAllByText('ORD-0099').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByRole('button', { name: 'Ontkoppelen' })).toBeInTheDocument()
  })

  it('scrolls to the named section from the attention panel', async () => {
    const scrollSpy = vi.fn()
    window.HTMLElement.prototype.scrollIntoView = scrollSpy
    api.getDossier.mockResolvedValue(transportDossier())
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Ga naar route' }))
    expect(scrollSpy).toHaveBeenCalled()
  })

  it('shows the 409 banner on a conflicting mutation and Herladen adopts the fresh state', async () => {
    api.getDossier.mockResolvedValue(transportDossier())
    const fresh = transportDossier()
    fresh.customerName = 'Aangepast NV'
    fresh.version = 'v-2'
    api.addDossierActivity.mockRejectedValue(new ApiError('conflict', 409, fresh))
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Activiteit' }))
    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    await user.click(screen.getByRole('button', { name: 'Toevoegen' }))

    expect(
      await screen.findByText(/Dit dossier is intussen gewijzigd door een collega/),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Herladen' }))
    await waitFor(() => expect(screen.getByText(/Aangepast NV/)).toBeInTheDocument())
    expect(screen.queryByText(/Dit dossier is intussen gewijzigd/)).not.toBeInTheDocument()
  })
})
