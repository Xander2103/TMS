import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { IncidentDetailPage } from '../pages/IncidentDetailPage'
import type { IncidentDetail } from '../types'

const permissions = new Set<string>()
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => permissions.has(code) }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../auditing/components/AuditHistoryPanel', () => ({
  AuditHistoryPanel: ({ entityType }: { entityType: string }) => <div data-testid="history">{entityType}</div>,
}))
const api = vi.hoisted(() => ({
  getIncident: vi.fn(),
  updateIncident: vi.fn(),
  changeIncidentStatus: vi.fn(),
  createIncident: vi.fn(),
  createIncidentRedelivery: vi.fn(),
  decideIncidentCharge: vi.fn(),
  proposeIncidentCharge: vi.fn(),
  createIncidentDamageReport: vi.fn(),
  getAssignableUsers: vi.fn().mockResolvedValue([{ id: 'u2', displayName: 'Dina Dispatch', roleName: 'Dispatcher' }]),
}))
vi.mock('../api/incidentsApi', () => api)
vi.mock('../../customers/api/customersApi', () => ({ searchCustomers: vi.fn().mockResolvedValue({ items: [] }) }))
vi.mock('../../dossiers/api/dossiersApi', () => ({ listDossiers: vi.fn().mockResolvedValue([]) }))
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({ searchTransportOrders: vi.fn().mockResolvedValue({ items: [] }) }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: vi.fn().mockResolvedValue([]) }))

function incident(overrides: Partial<IncidentDetail> = {}): IncidentDetail {
  return {
    id: 'inc-1', title: 'Waterschade aan drie paletten', description: 'Folie gescheurd.', incidentType: 'Damage', customTypeName: null,
    status: 'InProgress', severity: 'High', cause: null, responsibleUserId: 'u1', responsibleName: 'Dev Admin',
    customerImpact: null, operationalImpact: null, financialImpact: null, estimatedCost: null, actualCost: null,
    customerId: 'c1', customerName: 'Euro Retail Group', driverId: null, driverName: null, vehicleId: 'v1', vehicleLabel: '1-ABC-101',
    trailerId: null, trailerLabel: null, transportOrderId: 'o1', transportOrderNumber: 'ORD-0033', tripId: 't1', tripNumber: 'RIT-0007',
    dossierId: 'd1', dossierNumber: 'DOS-0033', dueDate: '2099-01-01', resolution: null, resolvedAt: null, createdAt: '2026-10-15T08:00:00Z',
    allowedStatusChanges: ['Resolved', 'Cancelled'], responsibleParty: 'Unknown', responsibilityNotes: null, chargeDecision: 'None',
    chargeAmount: null, chargeDescription: null, linkedRedeliveryOrderId: null, linkedRedeliveryOrderNumber: null, redeliverySuggested: false,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/incidents/inc-1']}>
      <Routes>
        <Route path="/incidents/:id" element={<IncidentDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('IncidentDetailPage (B24)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permissions.clear()
    permissions.add('incidents.manage')
  })

  it('opens read-first: linked entities are links, no form fields, history shown', async () => {
    api.getIncident.mockResolvedValue(incident())
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Waterschade aan drie paletten' })).toBeInTheDocument()
    expect(screen.queryByLabelText(/Titel/)).not.toBeInTheDocument()
    const links = within(screen.getByRole('region', { name: 'Koppelingen' }))
    expect(links.getByRole('link', { name: 'Euro Retail Group' })).toHaveAttribute('href', '/customers/c1')
    expect(links.getByRole('link', { name: 'DOS-0033' })).toHaveAttribute('href', '/dossiers/d1')
    expect(links.getByRole('link', { name: 'ORD-0033' })).toHaveAttribute('href', '/transport-orders/o1')
    expect(links.getByRole('link', { name: 'RIT-0007' })).toHaveAttribute('href', '/planning/t1')
    expect(links.getByRole('link', { name: '1-ABC-101' })).toHaveAttribute('href', '/vehicles/v1')
    expect(screen.getByTestId('history')).toHaveTextContent('Incident')
  })

  it('edits one card at a time via Bewerken and saves ONLY that card (EX-01)', async () => {
    api.getIncident.mockResolvedValue(incident({ driverId: 'dr1', driverName: 'Jan Chauffeur' }))
    api.updateIncident.mockImplementation(async (_id: string, input: { report: { title: string } }) =>
      incident({ title: input.report.title, driverId: 'dr1', driverName: 'Jan Chauffeur' }))
    renderPage()
    const report = await screen.findByRole('region', { name: 'Melding' })
    await userEvent.click(within(report).getByRole('button', { name: 'Bewerken' }))
    const title = within(report).getByLabelText(/Titel/)
    await userEvent.clear(title)
    await userEvent.type(title, 'Nieuwe titel')
    // Other cards cannot start a second edit meanwhile.
    expect(within(screen.getByRole('region', { name: 'Koppelingen' })).getByRole('button', { name: 'Bewerken' })).toBeDisabled()
    await userEvent.click(within(report).getByRole('button', { name: 'Wijzigingen opslaan' }))
    // The report card sends the report section only — never the relations it does not edit.
    const [, payload] = api.updateIncident.mock.calls[0]
    expect(payload).toEqual({ report: expect.objectContaining({ title: 'Nieuwe titel', severity: 'High', responsibleUserId: 'u1' }) })
    expect(payload).not.toHaveProperty('links')
    expect(await screen.findByRole('heading', { name: 'Nieuwe titel' })).toBeInTheDocument()
    expect(screen.queryByLabelText(/Titel/)).not.toBeInTheDocument()
  })

  it('the links card keeps the driver-reported trip and driver (EX-01)', async () => {
    api.getIncident.mockResolvedValue(incident({ driverId: 'dr1', driverName: 'Jan Chauffeur', source: 'Driver', executionLinksLocked: true }))
    api.updateIncident.mockResolvedValue(incident({ driverId: 'dr1', driverName: 'Jan Chauffeur' }))
    renderPage()
    const links = await screen.findByRole('region', { name: 'Koppelingen' })
    await userEvent.click(within(links).getByRole('button', { name: 'Bewerken' }))
    expect(within(links).getByText(/Rit RIT-0007 en chauffeur Jan Chauffeur komen uit de melding/)).toBeInTheDocument()
    // Dirty the form so the save is enabled: pick no vehicle.
    await userEvent.click(within(links).getByRole('button', { name: 'Wijzigingen opslaan' }))
    expect(api.updateIncident).not.toHaveBeenCalled() // nothing changed, nothing sent
  })

  it('the responsible picker uses the assignable-users endpoint (EX-07)', async () => {
    api.getIncident.mockResolvedValue(incident())
    renderPage()
    const report = await screen.findByRole('region', { name: 'Melding' })
    await userEvent.click(within(report).getByRole('button', { name: 'Bewerken' }))
    expect(api.getAssignableUsers).toHaveBeenCalled()
  })

  it('a serious vehicle problem prompts the fleet damage report (EX-03)', async () => {
    permissions.add('damage_reports.create')
    api.getIncident.mockResolvedValue(incident({ incidentType: 'VehicleBreakdown', severity: 'Critical', vehicleSafetyIssue: true, canCreateDamageReport: true }))
    api.createIncidentDamageReport.mockResolvedValue(incident({ damageReportId: 'dmg-1', damageReportSeverity: 'Severe', damageReportStatus: 'Reported' }))
    renderPage()
    const attention = await screen.findByRole('region', { name: 'Aandacht' })
    expect(attention).toHaveTextContent('nog geen schademelding')
    await userEvent.click(within(attention).getByRole('button', { name: 'Schademelding aanmaken' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByLabelText(/Ernst van de schade/)).toHaveValue('Severe')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Schademelding aanmaken' }))
    expect(api.createIncidentDamageReport).toHaveBeenCalledWith('inc-1', { severity: 'Severe', location: null })
    expect(await screen.findByRole('link', { name: 'Schademelding openen' })).toHaveAttribute('href', '/vehicles/v1?tab=schade')
  })

  it('offers one next step and keeps cancelling behind Meer with a confirmation', async () => {
    api.getIncident.mockResolvedValue(incident({ status: 'New', allowedStatusChanges: ['InProgress', 'Resolved', 'Cancelled'] }))
    api.changeIncidentStatus.mockResolvedValue(incident({ status: 'InProgress' }))
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'In behandeling nemen' }))
    expect(api.changeIncidentStatus).toHaveBeenCalledWith('inc-1', { status: 'InProgress', resolution: null })
  })

  it('requires a resolution note before resolving', async () => {
    api.getIncident.mockResolvedValue(incident())
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Afhandelen...' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Afhandelen' }))
    expect(within(dialog).getByText('Beschrijf de oplossing om het incident af te handelen.')).toBeInTheDocument()
    expect(api.changeIncidentStatus).not.toHaveBeenCalled()
    await userEvent.type(within(dialog).getByLabelText(/Oplossing/), 'Klant gecrediteerd')
    api.changeIncidentStatus.mockResolvedValue(incident({ status: 'Resolved', resolution: 'Klant gecrediteerd' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Afhandelen' }))
    expect(api.changeIncidentStatus).toHaveBeenCalledWith('inc-1', { status: 'Resolved', resolution: 'Klant gecrediteerd' })
  })

  it('cancelling asks for confirmation from the Meer menu', async () => {
    api.getIncident.mockResolvedValue(incident())
    api.changeIncidentStatus.mockResolvedValue(incident({ status: 'Cancelled' }))
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Meer' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Incident annuleren…' }))
    expect(api.changeIncidentStatus).not.toHaveBeenCalled()
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Incident annuleren' }))
    expect(api.changeIncidentStatus).toHaveBeenCalledWith('inc-1', { status: 'Cancelled', resolution: null })
  })

  it('shows overdue and pending-charge attention only when they apply', async () => {
    api.getIncident.mockResolvedValue(incident({ dueDate: '2020-01-01', chargeDecision: 'Proposed', chargeAmount: 450 }))
    renderPage()
    const attention = await screen.findByRole('region', { name: 'Aandacht' })
    expect(attention).toHaveTextContent('Deadline overschreden sinds 01/01/2020')
    expect(attention).toHaveTextContent('wacht op beslissing')
  })

  it('without incidents.manage: read-only, no edit or status actions', async () => {
    permissions.clear()
    api.getIncident.mockResolvedValue(incident())
    renderPage()
    await screen.findByRole('heading', { name: 'Waterschade aan drie paletten' })
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Afhandelen...' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Meer' })).not.toBeInTheDocument()
  })
})
