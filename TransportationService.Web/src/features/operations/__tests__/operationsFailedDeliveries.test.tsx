import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { OperationsPage } from '../pages/OperationsPage'

// UX-008/UX-009: signal -> affected order and trip -> context -> action, without leaving for three modules.

const api = vi.hoisted(() => ({
  getOperationsOverview: vi.fn(),
  listAlerts: vi.fn(),
  acknowledgeAlert: vi.fn(),
  resolveAlert: vi.fn(),
}))
vi.mock('../api/operationsApi', () => api)
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => ({ showError: vi.fn(), showSuccess: vi.fn() }) }))

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const position = { source: 'Unavailable', latitude: null, longitude: null, timestamp: null, description: null }
const trip = (over: object) => ({
  id: 't-1', tripNumber: 'RIT-1', tripDate: '2026-10-30', status: 'InProgress', driverName: 'An', vehicleNumber: null, trailerNumber: null,
  stopCount: 2, completedStopCount: 0, currentStop: null, nextStop: null, etaStatus: null, etaSource: null, delayMinutes: null,
  position, lastScanAt: null, lastScanResult: null, openExceptionCount: 0, missingPodCount: 0, orders: [], ...over,
})
const failed = {
  incidentId: 'i-1', orderId: 'o-1', orderNumber: 'ORD-0544', tripId: 't-9', tripNumber: 'RIT-0239', customerName: 'Klant A',
  dossierId: 'd-1', dossierNumber: 'DOS-0790', description: 'Levering mislukt: poort gesloten', occurredAt: '2026-10-30T10:00:00Z',
  redeliveryOrderId: null, redeliveryOrderNumber: null,
}
const overview = {
  generatedAt: '2026-10-30T10:00:00Z',
  counters: { activeTrips: 2, delayedTrips: 1, openExceptions: 3, openCriticalIncidents: 0, missingPods: 0, activeAlerts: 0, criticalAlerts: 0, failedDeliveries: 1 },
  trips: [trip({ id: 't-1', tripNumber: 'RIT-1' }), trip({ id: 't-2', tripNumber: 'RIT-2', etaStatus: 'Late', delayMinutes: 15 })],
  failedDeliveries: [failed],
}

const renderPage = () => render(<MemoryRouter><OperationsPage /></MemoryRouter>)

describe('OperationsPage failed deliveries and signals', () => {
  beforeEach(() => {
    api.getOperationsOverview.mockResolvedValue(overview)
    api.listAlerts.mockResolvedValue([])
    auth.permissions = ['incidents.manage', 'dossiers.view', 'planning.view', 'exceptions.view']
  })

  it('keeps a failed delivery on screen with order, trip, customer and the way to act', async () => {
    renderPage()
    const section = within(await screen.findByRole('region', { name: 'Mislukte leveringen' }))
    expect(section.getByText('ORD-0544 · RIT-0239')).toBeInTheDocument()
    expect(section.getByText(/Klant A/)).toBeInTheDocument()
    expect(section.getByText(/poort gesloten/)).toBeInTheDocument()
    expect(section.getByRole('link', { name: 'Opvolgen / herlevering' })).toHaveAttribute('href', '/incidents/i-1')
    expect(section.getByRole('link', { name: 'DOS-0790' })).toHaveAttribute('href', '/dossiers/d-1')
    expect(section.getByRole('link', { name: 'RIT-0239' })).toHaveAttribute('href', '/planning/t-9')
  })

  it('offers no links the user cannot open', async () => {
    auth.permissions = []
    renderPage()
    const section = within(await screen.findByRole('region', { name: 'Mislukte leveringen' }))
    expect(section.getByText('ORD-0544 · RIT-0239')).toBeInTheDocument()
    expect(section.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Open afwijkingen/ })).not.toBeInTheDocument()
  })

  it('names the redelivery once it exists, and shows nothing when no delivery failed', async () => {
    api.getOperationsOverview.mockResolvedValue({ ...overview, failedDeliveries: [{ ...failed, redeliveryOrderId: 'o-2', redeliveryOrderNumber: 'ORD-0600' }] })
    const { unmount } = renderPage()
    expect(await screen.findByRole('link', { name: 'Herlevering ORD-0600' })).toHaveAttribute('href', '/transport-orders/o-2')
    expect(screen.getByRole('link', { name: 'Incident opvolgen' })).toBeInTheDocument()
    unmount()

    api.getOperationsOverview.mockResolvedValue({ ...overview, failedDeliveries: [], counters: { ...overview.counters, failedDeliveries: 0 } })
    renderPage()
    await screen.findByText('RIT-1')
    expect(screen.queryByRole('region', { name: 'Mislukte leveringen' })).not.toBeInTheDocument()
  })

  it('shows the newest five and the rest on request, so the trip table stays in view', async () => {
    const user = userEvent.setup()
    const many = Array.from({ length: 7 }, (_, i) => ({ ...failed, incidentId: `i-${i}`, orderNumber: `ORD-${i}` }))
    api.getOperationsOverview.mockResolvedValue({ ...overview, failedDeliveries: many, counters: { ...overview.counters, failedDeliveries: 7 } })
    renderPage()
    expect(await screen.findByText(/^ORD-4 /)).toBeInTheDocument()
    expect(screen.queryByText(/^ORD-5 /)).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Alle 7 tonen' }))
    expect(screen.getByText(/^ORD-6 /)).toBeInTheDocument()
  })

  it('puts signals and ETA next to the trip, and the delay tile filters the table', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('RIT-1')
    expect(screen.getAllByRole('columnheader').map((h) => h.textContent).slice(0, 3)).toEqual(['Rit', 'Signalen', 'ETA'])

    await user.click(screen.getByRole('button', { name: /Vertraagd/ }))
    expect(screen.queryByText('RIT-1')).not.toBeInTheDocument()
    expect(screen.getByText('RIT-2')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Vertraagd/ }))
    expect(screen.getByText('RIT-1')).toBeInTheDocument()
  })
})
