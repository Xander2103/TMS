import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ExceptionDetailPage } from '../pages/ExceptionDetailPage'
import type { ExceptionDetail } from '../types'
import { ApiError } from '../../../api/apiClient'

const permissions = new Set<string>()
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions.has(code), user: { id: 'me' } }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))
vi.mock('../../auditing/components/AuditHistoryPanel', () => ({
  AuditHistoryPanel: ({ entityType }: { entityType: string }) => <div data-testid="history">{entityType}</div>,
}))
const api = vi.hoisted(() => ({
  getException: vi.fn(),
  changeExceptionStatus: vi.fn(),
  assignException: vi.fn(),
  updateException: vi.fn(),
  deleteExceptionPhoto: vi.fn(),
  fetchExceptionPhotoUrl: vi.fn(),
}))
vi.mock('../api/exceptionsApi', () => api)

function detail(overrides: Partial<ExceptionDetail> = {}): ExceptionDetail {
  return {
    id: 'ex-1', type: 'RejectedDelivery', severity: 'High', status: 'Open', description: 'Ontvanger weigert levering.',
    quantity: 2, tripId: 't1', tripNumber: 'RIT-0001', transportOrderId: 'o1', orderNumber: 'ORD-0001',
    transportOrderStopId: 's1', stopLabel: 'Düsseldorf site', cargoItemId: null, cargoDescription: null,
    packageId: null, packageNumber: null, packageStatus: null, assignedToUserId: null, assignedToName: null,
    reportedByName: 'Dev Admin', driverName: 'Rik Claes', occurredAt: '2026-10-15T07:51:00Z', latitude: null, longitude: null,
    dispatcherNotes: null, customerVisible: false, resolutionNote: null, resolvedByName: null, resolvedAt: null, photos: [],
    allowedTransitions: ['Investigating', 'Resolved', 'Rejected', 'CustomerActionRequired'],
    dossierId: 'd1', dossierNumber: 'DOS-0001', customerId: 'c1', customerName: 'Construct & Co',
    ...overrides,
  } as ExceptionDetail
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/exceptions/ex-1']}>
      <Routes>
        <Route path="/exceptions/:id" element={<ExceptionDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ExceptionDetailPage (B24)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permissions.clear()
    permissions.add('exceptions.resolve')
  })

  it('an unknown exception (404) says "niet gevonden" with the way back, not a load error (M14)', async () => {
    api.getException.mockRejectedValue(new ApiError('Not found', 404))
    renderPage()
    expect(await screen.findByText('Afwijking niet gevonden.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /terug/i })).toHaveAttribute('href', '/exceptions')
    expect(screen.queryByText('De afwijking kon niet worden geladen.')).not.toBeInTheDocument()
  })

  it('shows the whole context as compact facts with clickable links', async () => {
    api.getException.mockResolvedValue(detail())
    renderPage()
    const report = await screen.findByRole('region', { name: 'Melding' })
    expect(within(report).getByRole('link', { name: 'RIT-0001' })).toHaveAttribute('href', '/planning/t1')
    expect(within(report).getByRole('link', { name: 'ORD-0001' })).toHaveAttribute('href', '/transport-orders/o1')
    expect(within(report).getByRole('link', { name: 'DOS-0001' })).toHaveAttribute('href', '/dossiers/d1')
    expect(within(report).getByRole('link', { name: 'Construct & Co' })).toHaveAttribute('href', '/customers/c1')
    // Absent optional context is left out instead of a column of dashes.
    expect(within(report).queryByText('Goederenlijn')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Dispatchernotities')).not.toBeInTheDocument()
  })

  it('offers one next step; the other moves and rejecting sit in Meer', async () => {
    api.getException.mockResolvedValue(detail())
    renderPage()
    const header = (await screen.findByRole('button', { name: 'In onderzoek nemen' })).closest('.page-header') as HTMLElement
    expect(within(header).getByRole('button', { name: 'Aan mij toewijzen' })).toBeInTheDocument()
    expect(within(header).queryByRole('button', { name: /Oplossen/ })).not.toBeInTheDocument()
    await userEvent.click(within(header).getByRole('button', { name: 'Meer' }))
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Oplossen…', 'Actie klant vereist', 'Verantwoordelijke kiezen…', 'Afwijzen…'])
  })

  it('requires a resolution note to resolve', async () => {
    api.getException.mockResolvedValue(detail({ status: 'Investigating', allowedTransitions: ['Resolved', 'Rejected', 'CustomerActionRequired'] }))
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Oplossen…' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Bevestigen' }))
    expect(toast.showError).toHaveBeenCalledWith('Een afhandelingsnotitie is verplicht bij het afsluiten.')
    expect(api.changeExceptionStatus).not.toHaveBeenCalled()
  })

  it('edits handling inline only after Bewerken', async () => {
    api.getException.mockResolvedValue(detail())
    api.updateException.mockResolvedValue(detail({ dispatcherNotes: 'Klant gebeld' }))
    renderPage()
    const handling = await screen.findByRole('region', { name: 'Afhandeling' })
    await userEvent.click(within(handling).getByRole('button', { name: 'Bewerken' }))
    await userEvent.type(within(handling).getByLabelText('Dispatchernotities'), 'Klant gebeld')
    await userEvent.click(within(handling).getByRole('button', { name: 'Opslaan' }))
    expect(api.updateException).toHaveBeenCalledWith('ex-1', { severity: 'High', dispatcherNotes: 'Klant gebeld', customerVisible: false })
  })

  it('without exceptions.resolve: no assignment, transitions or edit', async () => {
    permissions.clear()
    api.getException.mockResolvedValue(detail())
    renderPage()
    await screen.findByRole('region', { name: 'Melding' })
    expect(screen.queryByRole('button', { name: 'Aan mij toewijzen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'In onderzoek nemen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Meer' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })
})
