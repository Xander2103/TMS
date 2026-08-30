import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { TripExecutionPage } from '../TripExecutionPage'
import { resetTimeZonePreference, setTimeZonePreference } from '../../../../utils/dates'
import type { ExecutionStop, TripExecution } from '../../types'

/**
 * Wave 1 fix A (A11) — the one screen that decides whether the truck arrives on time.
 *
 * Before C-03 the form wrote the typed wall clock TAGGED with a "Z", so this page's local
 * `slice(11, 16)` accidentally printed back the 08:00 the planner typed. C-03 made the wire value
 * a real instant while this page kept slicing it, so on 15 July a confirmed 08:00–10:00 window
 * read "06:00–08:00" to the driver: two hours early, or "klant gesloten". The page now uses the
 * shared tenant-zone `formatTime`, like every other surface.
 *
 * The process zone is forced to Asia/Tokyo so nothing here can pass by browser-zone luck: Tokyo is
 * UTC+9 and Europe/Amsterdam is UTC+2 on this date, so a browser-zone render would say 15:00.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'Asia/Tokyo'
  // Self-check: if the TZ knob ever stops working, fail loudly instead of silently passing.
  expect(new Date('2026-07-15T06:00:00Z').getHours()).toBe(15)
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

afterEach(() => resetTimeZonePreference())

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))
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
    transportOrderStopId: 'stop-1', transportOrderId: 'order-1', orderNumber: 'ORD-1',
    customerName: 'Klant X', orderSequence: 1, stopSequence: 1, stopType: 'Unloading',
    locationName: 'Bouwwerf', address: 'Veldstraat 1', postalCode: '9000', city: 'Gent',
    plannedFrom: null, plannedTo: null,
    requestedFrom: null, requestedTo: null,
    confirmedFrom: null, confirmedTo: null,
    earliestAllowed: null, latestAllowed: null,
    appointmentRequired: false, appointmentReference: null,
    instructions: null, accessInstructions: null, loadingInstructions: null, unloadingInstructions: null,
    status: 'Planned', arrivedAt: null, departedAt: null, completedAt: null,
    waitingMinutes: null, lateArrivalReason: null, statusReason: null,
    allowedTransitions: [], hasPod: false, podSignedBy: null, remarks: null,
    ...overrides,
  }
}

function execution(stops: ExecutionStop[]): TripExecution {
  return {
    tripId: 'trip-1', tripNumber: 'RIT-1', tripDate: '2026-07-15', tripStatus: 'InProgress',
    driverName: 'Jan', vehicleNumber: 'V-1', vehicleLicensePlate: '1-ABC-123',
    stops, completedCount: 0, totalCount: stops.length,
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

describe('TripExecutionPage — stop windows in the tenant zone (C-03 / A11)', () => {
  it('shows the confirmed window as the planner typed it, not as raw UTC', async () => {
    // The planner typed 08:00–10:00 Amsterdam on 15 July; the wire carries the instant.
    api.getTripExecution.mockResolvedValue(
      execution([stop({ confirmedFrom: '2026-07-15T06:00:00Z', confirmedTo: '2026-07-15T08:00:00Z' })]),
    )
    renderPage()

    expect(await screen.findByText(/08:00–10:00/)).toBeInTheDocument()
    expect(screen.queryByText(/06:00–08:00/)).not.toBeInTheDocument() // the raw-UTC slice
    expect(screen.queryByText(/15:00/)).not.toBeInTheDocument() // the browser (Tokyo) zone
  })

  it('follows a reconfigured tenant zone for the arrival stamp', async () => {
    setTimeZonePreference('Europe/Lisbon') // UTC+1 in July
    api.getTripExecution.mockResolvedValue(
      execution([stop({ status: 'Arrived', arrivedAt: '2026-07-15T06:00:00Z' })]),
    )
    renderPage()

    expect(await screen.findByText(/07:00/)).toBeInTheDocument()
  })
})
