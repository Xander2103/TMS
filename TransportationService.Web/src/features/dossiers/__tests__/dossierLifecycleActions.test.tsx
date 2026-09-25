import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import type { DossierConfirmationEvaluation } from '../types'
import { formatDateTime } from '../../../utils/dates'
import { dossierDetail } from './fixtures'

/**
 * Confirmation sprint 2026-09-23 — the dossier lifecycle on the detail page: "Bevestigen" as the
 * primary header action of an open dossier (through DossierConfirmDialog), the confirmed/cancelled
 * metadata under the badge, and the reason-gated "Dossier heropenen" / "Annuleren" menu items.
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
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: vi.fn(),
  updateTransportOrder: vi.fn(),
  saveOrderPriceLines: vi.fn(),
  setOrderOneOffPrice: vi.fn(),
  searchTransportOrders: () => Promise.resolve({ items: [], totalCount: 0 }),
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [], totalCount: 0 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../users/api/usersApi', () => ({ getUsers: () => Promise.resolve([]) }))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
// Historiek hosts the notes panel and the audit trail; neither is under test here.
vi.mock('../notes/DossierNotesPanel', () => ({ DossierNotesPanel: () => <div data-testid="notes-panel" /> }))
vi.mock('../../auditing/components/AuditHistoryPanel', () => ({ AuditHistoryPanel: () => null }))

function renderPage(initialPath = '/dossiers/d-1') {
  const router = createMemoryRouter([{ path: '/dossiers/:id/:section?', element: <DossierDetailPage /> }], {
    initialEntries: [initialPath],
  })
  return { ...render(<RouterProvider router={router} />), router }
}

function evaluation(overrides: Partial<DossierConfirmationEvaluation> = {}): DossierConfirmationEvaluation {
  return {
    dossierId: 'd-1', status: 'Open', canConfirmManually: true, canAutoConfirm: true,
    blockers: [], warnings: [], autoBlockers: [], relevantOrderIds: [], relevantActivityIds: [],
    summary: {
      ordersTotal: 0, ordersCompleted: 0, ordersCancelled: 0, ordersOpen: 0, deliveriesFailed: 0, podMissing: 0,
      activitiesExecutable: 0, activitiesExecuted: 0, billableUnits: 0, pricedUnits: 0, documents: 0, hasCmr: false, openIncidents: 0,
    },
    ...overrides,
  }
}

const CONFIRMED_AT = '2026-09-23T08:15:00Z'

function confirmedDossier(overrides: Parameters<typeof dossierDetail>[0] = {}) {
  return dossierDetail({
    status: 'Closed',
    closedAt: CONFIRMED_AT,
    confirmedAt: CONFIRMED_AT,
    confirmedByUserId: 'u-1',
    confirmedByName: 'An Peeters',
    confirmationSource: 'Manual',
    confirmationReason: null,
    ...overrides,
  })
}

/** The status badge next to the dossier number in the header. */
function headerBadge() {
  return within(screen.getByRole('heading', { level: 1 })).getByText(/^(Open|Bevestigd|Geannuleerd)$/)
}

async function headerMenuItem(name: string) {
  return screen.findByRole('menuitem', { name, hidden: true })
}

describe('Dossier lifecycle — confirm', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('offers "Bevestigen" as the primary action of an open dossier and renders the manual confirmation afterwards', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(dossierDetail())
    api.getDossierConfirmation.mockResolvedValue(evaluation())
    api.confirmDossier.mockResolvedValue(confirmedDossier({ version: 'v-2', confirmationReason: 'Administratief nagemaakt' }))
    renderPage()

    const confirm = await screen.findByRole('button', { name: 'Bevestigen' })
    expect(headerBadge()).toHaveTextContent('Open')
    expect(screen.queryByText(/Bevestigd op/)).not.toBeInTheDocument()

    await user.click(confirm)
    const dialog = within(await screen.findByRole('dialog', { name: /Dossier bevestigen/ }))
    expect(await dialog.findByText('Alle operationele onderdelen zijn afgerond.')).toBeInTheDocument()
    await user.click(dialog.getByRole('button', { name: 'Bevestigen' }))

    await waitFor(() => expect(api.confirmDossier).toHaveBeenCalledWith('d-1', { reason: null, acknowledgeWarnings: false, version: 'v-1' }))
    await waitFor(() => expect(headerBadge()).toHaveTextContent('Bevestigd'))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalledWith('Dossier bevestigd.')
    expect(screen.getByText(`Bevestigd op ${formatDateTime(CONFIRMED_AT)}`)).toBeInTheDocument()
    expect(screen.getByText('handmatig')).toBeInTheDocument()
    expect(screen.getByText('door An Peeters')).toBeInTheDocument()
    expect(screen.getByText('Administratief nagemaakt')).toBeInTheDocument()
    // A confirmed dossier is read-only: no primary actions any more.
    expect(screen.queryByRole('button', { name: 'Bevestigen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Activiteit' })).not.toBeInTheDocument()
  })

  it('hides "Bevestigen" without dossiers.manage', async () => {
    auth.permissions = new Set(['dossiers.view'])
    api.getDossier.mockResolvedValue(dossierDetail())
    renderPage()
    await screen.findByRole('heading', { level: 1 })
    expect(screen.queryByRole('button', { name: 'Bevestigen' })).not.toBeInTheDocument()
  })

  it('renders an automatic confirmation as "automatisch" and a legacy closed dossier as source unknown', async () => {
    api.getDossier.mockResolvedValue(confirmedDossier({ confirmationSource: 'Automatic', confirmedByName: null }))
    const automatic = renderPage()
    await screen.findByText(`Bevestigd op ${formatDateTime(CONFIRMED_AT)}`)
    expect(screen.getByText('automatisch')).toBeInTheDocument()
    expect(screen.queryByText(/^door /)).not.toBeInTheDocument()
    automatic.unmount()

    api.getDossier.mockResolvedValue(confirmedDossier({ confirmationSource: null, confirmedByName: null, confirmedByUserId: null }))
    renderPage()
    await screen.findByText(`Bevestigd op ${formatDateTime(CONFIRMED_AT)}`)
    expect(screen.getByText(/bron onbekend/)).toBeInTheDocument()
    expect(screen.queryByText(/Gesloten/)).not.toBeInTheDocument()
  })

  it('shows the confirmation facts on the Historiek tab instead of the old "Gesloten op" line', async () => {
    api.getDossier.mockResolvedValue(confirmedDossier())
    renderPage('/dossiers/d-1/historiek')
    await screen.findByTestId('notes-panel')
    expect(screen.getAllByText(new RegExp(`Bevestigd op ${formatDateTime(CONFIRMED_AT)}`)).length).toBeGreaterThanOrEqual(2)
    expect(screen.queryByText(/Gesloten op/)).not.toBeInTheDocument()
  })
})

describe('Dossier lifecycle — reopen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
    api.getDossier.mockResolvedValue(confirmedDossier())
  })

  it('offers no reopen item without dossiers.reopen', async () => {
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage'])
    renderPage()
    await screen.findByText('handmatig')
    expect(screen.queryByRole('menuitem', { name: 'Dossier heropenen', hidden: true })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Heropenen', hidden: true })).not.toBeInTheDocument()
  })

  it('asks a reason, refuses an empty one and reopens with the dossier version', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['dossiers.view', 'dossiers.reopen'])
    api.reopenDossier.mockResolvedValue(dossierDetail({ version: 'v-2' }))
    renderPage()

    await user.click(await headerMenuItem('Dossier heropenen'))
    const dialog = within(await screen.findByRole('dialog', { name: 'Dossier heropenen' }))
    expect(dialog.getByText(/eerdere bevestiging blijft in de historiek/)).toBeInTheDocument()

    await user.click(dialog.getByRole('button', { name: 'Dossier heropenen' }))
    expect(await dialog.findByRole('alert')).toHaveTextContent('Geef een reden op.')
    expect(api.reopenDossier).not.toHaveBeenCalled()

    await user.type(dialog.getByLabelText(/Reden voor heropenen/), 'x')
    await user.click(dialog.getByRole('button', { name: 'Dossier heropenen' }))
    await waitFor(() => expect(api.reopenDossier).toHaveBeenCalledWith('d-1', 'x', 'v-1'))
    await waitFor(() => expect(headerBadge()).toHaveTextContent('Open'))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalledWith('Dossier heropend.')
    expect(screen.queryByText(/Bevestigd op/)).not.toBeInTheDocument()
  })

  it('reports a failed reopen inside the dialog and keeps it open', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['dossiers.view', 'dossiers.reopen'])
    // A server message (e.g. "planned orders block") is shown as-is; without one the lifecycle fallback.
    api.reopenDossier.mockRejectedValue(new ApiError('', 500))
    renderPage()

    await user.click(await headerMenuItem('Dossier heropenen'))
    const dialog = within(await screen.findByRole('dialog', { name: 'Dossier heropenen' }))
    await user.type(dialog.getByLabelText(/Reden voor heropenen/), 'x')
    await user.click(dialog.getByRole('button', { name: 'Dossier heropenen' }))
    expect(await dialog.findByRole('alert')).toHaveTextContent('Het dossier kon niet worden heropend.')
    expect(screen.getByRole('dialog', { name: 'Dossier heropenen' })).toBeInTheDocument()
  })
})

describe('Dossier lifecycle — cancel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['dossiers.view', 'dossiers.manage', 'dossiers.reopen'])
    window.HTMLElement.prototype.scrollIntoView = vi.fn()
  })

  it('cancels an open dossier with a reason and renders the cancellation', async () => {
    const user = userEvent.setup()
    api.getDossier.mockResolvedValue(dossierDetail())
    api.cancelDossier.mockResolvedValue(
      dossierDetail({ status: 'Cancelled', version: 'v-2', cancelledAt: CONFIRMED_AT, cancellationReason: 'Klant heeft afgezegd' }),
    )
    renderPage()
    await screen.findByRole('button', { name: 'Bevestigen' })
    // No old "Dossier sluiten" item any more; the cancel item is the destructive one.
    expect(screen.queryByRole('menuitem', { name: 'Dossier sluiten', hidden: true })).not.toBeInTheDocument()
    const cancelItem = await headerMenuItem('Annuleren')
    expect(cancelItem).toHaveClass('dossier-more-danger')

    await user.click(cancelItem)
    const dialog = within(await screen.findByRole('dialog', { name: 'Dossier annuleren' }))
    await user.click(dialog.getByRole('button', { name: 'Dossier annuleren' }))
    expect(await dialog.findByRole('alert')).toHaveTextContent('Geef een reden op.')
    expect(api.cancelDossier).not.toHaveBeenCalled()

    await user.type(dialog.getByLabelText(/Reden voor annulering/), 'Klant heeft afgezegd')
    await user.click(dialog.getByRole('button', { name: 'Dossier annuleren' }))
    await waitFor(() => expect(api.cancelDossier).toHaveBeenCalledWith('d-1', 'Klant heeft afgezegd', 'v-1'))
    await waitFor(() => expect(headerBadge()).toHaveTextContent('Geannuleerd'))
    expect(toast.showSuccess).toHaveBeenCalledWith('Dossier geannuleerd.')
    expect(screen.getByText(`Geannuleerd op ${formatDateTime(CONFIRMED_AT)}`)).toBeInTheDocument()
    expect(screen.getByText('Reden: Klant heeft afgezegd')).toBeInTheDocument()
    // Read-only like a confirmed dossier; only reopen remains.
    expect(screen.queryByRole('button', { name: 'Bevestigen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Activiteit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Bewerken', hidden: true })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Annuleren', hidden: true })).not.toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Dossier heropenen', hidden: true })).toBeInTheDocument()
  })

  it('offers no cancel item on a confirmed dossier', async () => {
    api.getDossier.mockResolvedValue(confirmedDossier())
    renderPage()
    await screen.findByText('handmatig')
    expect(screen.queryByRole('menuitem', { name: 'Annuleren', hidden: true })).not.toBeInTheDocument()
  })
})
