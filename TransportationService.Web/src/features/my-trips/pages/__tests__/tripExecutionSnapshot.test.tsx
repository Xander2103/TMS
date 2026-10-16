import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TripExecutionPage } from '../TripExecutionPage'
import type { ExecutionStop, TripExecution } from '../../types'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))
vi.mock('../../../scanning/components/ScanPanel', () => ({ ScanPanel: () => null }))
vi.mock('../../../exceptions/components/ReportExceptionDialog', () => ({ ReportExceptionDialog: () => null }))
vi.mock('../../../pod/components/PodDialog', () => ({ PodDialog: () => null }))

const api = vi.hoisted(() => ({
  getTripExecution: vi.fn(),
  getStopHistory: vi.fn(),
  transitionStop: vi.fn(),
  completeStop: vi.fn(),
}))
vi.mock('../../api/myTripsApi', () => api)

function stop(overrides: Partial<ExecutionStop> = {}): ExecutionStop {
  return {
    transportOrderStopId: 'stop-1',
    transportOrderId: 'order-1',
    orderNumber: 'ORD-1',
    customerName: 'Klant X',
    orderSequence: 1,
    stopSequence: 1,
    stopType: 'Loading',
    locationName: 'Magazijn Antwerpen',
    address: 'Noorderlaan 10',
    postalCode: '2030',
    city: 'Antwerpen',
    plannedFrom: null,
    plannedTo: null,
    requestedFrom: null,
    requestedTo: null,
    confirmedFrom: null,
    confirmedTo: null,
    earliestAllowed: null,
    latestAllowed: null,
    appointmentRequired: false,
    appointmentReference: null,
    instructions: null,
    accessInstructions: null,
    loadingInstructions: null,
    unloadingInstructions: null,
    status: 'Planned',
    arrivedAt: null,
    departedAt: null,
    completedAt: null,
    waitingMinutes: null,
    lateArrivalReason: null,
    statusReason: null,
    allowedTransitions: [],
    hasPod: false,
    podSignedBy: null,
    remarks: null,
    ...overrides,
  }
}

function execution(stops: ExecutionStop[]): TripExecution {
  return {
    tripId: 'trip-1',
    tripNumber: 'RIT-1',
    tripDate: '2026-07-20',
    tripStatus: 'InProgress',
    driverName: 'Jan',
    vehicleNumber: 'V-1',
    vehicleLicensePlate: '1-ABC-123',
    stops,
    completedCount: 0,
    totalCount: stops.length,
  }
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/my-trips/trip-1']}>
      <Routes>
        <Route path="/my-trips/:id" element={<TripExecutionPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  api.getTripExecution.mockReset()
})

describe('TripExecutionPage location snapshot (Phase 7)', () => {
  it('renders contact with tel: link, gate/dock, access code, route and opening hours when present', async () => {
    api.getTripExecution.mockResolvedValue(
      execution([
        stop({
          contactName: 'Magazijnier Piet',
          contactPhone: '+32 3 123 45 67',
          gate: 'Poort B',
          dock: 'Kade 7',
          accessCode: '1234#',
          routeDescription: 'Via de Noorderlaan, tweede poort',
          openingHoursSummary: 'Ma 07:00–17:00',
        }),
      ]),
    )
    renderPage()

    expect(await screen.findByText(/Magazijnier Piet/)).toBeInTheDocument()
    const phoneLink = screen.getByRole('link', { name: '+32 3 123 45 67' })
    expect(phoneLink).toHaveAttribute('href', 'tel:+3231234567')
    expect(screen.getByText(/Poort: Poort B/)).toBeInTheDocument()
    expect(screen.getByText(/Kade\/dok: Kade 7/)).toBeInTheDocument()
    expect(screen.getByText(/Toegangscode: 1234#/)).toBeInTheDocument()
    expect(screen.getByText(/Via de Noorderlaan, tweede poort/)).toBeInTheDocument()
    expect(screen.getByText(/Ma 07:00–17:00/)).toBeInTheDocument()
  })

  it('renders none of the snapshot lines when the stop has no snapshot data', async () => {
    api.getTripExecution.mockResolvedValue(execution([stop()]))
    renderPage()

    expect(await screen.findByText(/Magazijn Antwerpen/)).toBeInTheDocument()
    expect(screen.queryByText(/Toegangscode:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Poort:/)).not.toBeInTheDocument()
    // No contact → no tel: link (breadcrumb links are unrelated).
    expect(document.querySelector('a[href^="tel:"]')).toBeNull()
  })
})

describe('TripExecutionPage work description and goods (M7)', () => {
  it('shows "Omschrijving werkzaamheden" with the lift data on a crane site stop', async () => {
    api.getTripExecution.mockResolvedValue(
      execution([
        stop({
          stopType: 'Site',
          locationName: 'Werf Gent',
          work: {
            craneJobKind: 'OnSiteLifting',
            workDescription: 'Airco-unit op het dak plaatsen',
            liftLoadWeightKg: 850,
            liftLoadDimensions: null,
            liftRadiusMeters: null,
            liftHeightMeters: 18,
            liftEquipment: 'Hijsbalk 2 t',
            liftConditions: null,
          },
          goods: [],
        }),
      ]),
    )
    renderPage()

    const work = await screen.findByRole('region', { name: 'Omschrijving werkzaamheden' })
    expect(work).toHaveTextContent('Airco-unit op het dak plaatsen')
    expect(work).toHaveTextContent('Gewicht last: 850 kg · Hijshoogte: 18 m · Hijsmateriaal: Hijsbalk 2 t')
    expect(screen.queryByRole('region', { name: 'Goederen' })).not.toBeInTheDocument()
  })

  it('shows the goods of a loading stop as quantity × unit, description and weight — never a price', async () => {
    api.getTripExecution.mockResolvedValue(
      execution([
        stop({
          goods: [
            { description: 'Kabelhaspels', quantity: 4, unit: 'Europallet', weightKg: 3200 },
            { description: 'Klein materiaal', quantity: 2, unit: null, weightKg: null },
          ],
          goodsTotalWeightKg: 3200,
        }),
      ]),
    )
    renderPage()

    const goods = await screen.findByRole('region', { name: 'Goederen' })
    expect(goods).toHaveTextContent('4 × Europallet Kabelhaspels · 3.200 kg')
    expect(goods).toHaveTextContent('2 Klein materiaal')
    expect(goods).toHaveTextContent('Totaal gewicht: 3.200 kg')
    expect(goods).not.toHaveTextContent('€')
    expect(screen.queryByRole('region', { name: 'Omschrijving werkzaamheden' })).not.toBeInTheDocument()
  })
})
