import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import type { DossierOrder } from '../types'
import { dossierDetail } from './fixtures'

/**
 * M4 — a portal-submitted order can be reviewed from the dossier workspace (the SAME review panel
 * and endpoint as the order detail), and the dossier's own "Bevestigen" is blocked meanwhile.
 * M14 — a dossier that does not exist says so instead of offering "Opnieuw proberen".
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
  getDossierConfirmation: vi.fn(),
  confirmDossier: vi.fn(),
  reopenDossier: vi.fn(),
  cancelDossier: vi.fn(),
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
const orders = vi.hoisted(() => ({ reviewPortalOrder: vi.fn() }))
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: vi.fn(),
  updateTransportOrder: vi.fn(),
  saveOrderPriceLines: vi.fn(),
  setOrderOneOffPrice: vi.fn(),
  searchTransportOrders: () => Promise.resolve({ items: [], totalCount: 0 }),
  reviewPortalOrder: orders.reviewPortalOrder,
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [], totalCount: 0 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../users/api/usersApi', () => ({ getUsers: () => Promise.resolve([]) }))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
vi.mock('../notes/DossierNotesPanel', () => ({ DossierNotesPanel: () => <div data-testid="notes-panel" /> }))
vi.mock('../../auditing/components/AuditHistoryPanel', () => ({ AuditHistoryPanel: () => null }))

function renderPage(initialPath = '/dossiers/d-1') {
  const router = createMemoryRouter([{ path: '/dossiers/:id/:section?', element: <DossierDetailPage /> }], {
    initialEntries: [initialPath],
  })
  return { ...render(<RouterProvider router={router} />), router }
}

function order(overrides: Partial<DossierOrder> = {}): DossierOrder {
  return {
    linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0042', orderDate: '2026-08-12', status: 'Submitted',
    goodsDescription: 'Europalletten', agreedPrice: null, isPriced: false, ...overrides,
  }
}

describe('Dossier workspace — portal review (M4)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'orders.change_status'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('offers Accepteren / Info opvragen / Afwijzen for the submitted order and blocks Bevestigen with the reason', async () => {
    api.getDossier.mockResolvedValue(dossierDetail({ orders: [order()] }))
    renderPage()

    const panel = await screen.findByRole('region', { name: 'Klantportaal-beoordeling' })
    expect(panel).toHaveTextContent('ORD-0042 · ingediend via het klantportaal')
    expect(screen.getByRole('button', { name: 'Accepteren' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Info opvragen' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Afwijzen' })).toBeEnabled()

    const confirm = screen.getByRole('button', { name: 'Bevestigen' })
    expect(confirm).toBeDisabled()
    expect(confirm).toHaveAccessibleDescription(/Beoordeel eerst de klantportaal-aanvraag \(ORD-0042\)/)
  })

  it('accepts through the shared portal-review endpoint and reloads the dossier', async () => {
    const user = userEvent.setup()
    api.getDossier
      .mockResolvedValueOnce(dossierDetail({ orders: [order()] }))
      .mockResolvedValue(dossierDetail({ orders: [order({ status: 'Confirmed' })] }))
    orders.reviewPortalOrder.mockResolvedValue({ id: 'o-1', status: 'Confirmed' })
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Accepteren' }))

    expect(orders.reviewPortalOrder).toHaveBeenCalledWith('o-1', 'Accept', null)
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Klantportaal-beoordeling' })).not.toBeInTheDocument())
    expect(api.getDossier).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: 'Bevestigen' })).toBeEnabled()
  })

  it('shows no review actions to a user who may not change order statuses', async () => {
    auth.permissions = new Set(['dossiers.view'])
    api.getDossier.mockResolvedValue(dossierDetail({ orders: [order()] }))
    renderPage()

    expect(await screen.findByRole('heading', { level: 1 })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Klantportaal-beoordeling' })).not.toBeInTheDocument()
  })

  it('keeps Bevestigen as the plain primary action without submitted orders', async () => {
    api.getDossier.mockResolvedValue(dossierDetail({ orders: [order({ status: 'Confirmed' })] }))
    renderPage()

    expect(await screen.findByRole('button', { name: 'Bevestigen' })).toBeEnabled()
    expect(screen.queryByRole('region', { name: 'Klantportaal-beoordeling' })).not.toBeInTheDocument()
  })
})

describe('Dossier detail — unknown record (M14)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view'])
  })

  it('says "Dossier niet gevonden" with a way back instead of a retry', async () => {
    api.getDossier.mockRejectedValue(new ApiError('Not found', 404))
    renderPage('/dossiers/does-not-exist')

    expect(await screen.findByText('Dossier niet gevonden.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Terug naar de lijst' })).toHaveAttribute('href', '/dossiers')
    expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
  })

  it('keeps the retry for any other load failure', async () => {
    api.getDossier.mockRejectedValue(new ApiError('Boom', 500))
    renderPage()

    expect(await screen.findByText('Het dossier kon niet worden geladen.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Opnieuw proberen' })).toBeInTheDocument()
  })
})
