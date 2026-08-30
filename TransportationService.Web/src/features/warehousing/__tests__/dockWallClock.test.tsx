import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DockPlanningPage } from '../pages/DockPlanningPage'
import { dockBlockPosition, type DockAppointment, type DockBoard, type Warehouse, type WarehouseDashboard } from '../types'

/**
 * Wave 1 fix A (A13) — dock planning shows THREE clocks for one appointment: the block you drag
 * (`getHours()`, browser zone), the label you read (`formatTime`, which moved to the tenant zone in
 * this wave) and the field you type into (`slice(0, 16)`, the raw wire text). A warehouse manager
 * on a laptop still set to UK time saw the modal say 10:00, the field say 08:00 and the block sit
 * at 09:00 — and "correcting" the field moved the appointment two hours.
 *
 * Ruling for this wave: make the screen internally consistent WITHOUT re-encoding its data.
 * `dock_appointments` store the typed wall clock stamped as UTC (`DockPlanningService.AsUtc` turns
 * an Unspecified DateTime into a UTC one), so the wall clock is what the string itself says. All
 * three halves now read exactly that. Wave 2 migrates this screen to the tenant-zone helpers
 * together with a data re-encoding of `dock_appointments`.
 *
 * The runner zone is forced to Asia/Tokyo so a browser-zone render cannot pass by accident.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'Asia/Tokyo'
  expect(new Date('2026-07-15T08:00:00Z').getHours()).toBe(17)
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const api = vi.hoisted(() => ({
  listWarehouses: vi.fn(),
  getDockBoard: vi.fn(),
  getWarehouseDashboard: vi.fn(),
  createDockAppointment: vi.fn(),
  updateDockAppointment: vi.fn(),
  deleteDockAppointment: vi.fn(),
  changeDockAppointmentStatus: vi.fn(),
}))
vi.mock('../api/warehousingApi', () => api)
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

function appointment(): DockAppointment {
  return {
    id: 'app-1', warehouseId: 'wh-1', dockId: 'dock-1', dockCode: 'D1',
    operationType: 'Loading', status: 'Planned',
    // The typed 08:00–09:00, stored (and returned) as the wall clock stamped UTC.
    plannedStart: '2026-07-15T08:00:00Z', plannedEnd: '2026-07-15T09:00:00Z',
    arrivedAt: null, startedAt: null, completedAt: null,
    priority: 'Normal',
    tripId: null, tripNumber: null, transportOrderId: 'ord-1', orderNumber: 'ORD-1',
    customerName: 'Haven BV',
    vehicleId: null, vehicleNumber: null, trailerId: null, trailerNumber: null,
    driverId: null, driverName: null, reference: null, remarks: null,
    packageCount: 0, packagesHandled: 0, hasOpenDiscrepancies: false,
    allowedTransitions: [], version: 'v1',
  } as unknown as DockAppointment
}

const warehouse = {
  id: 'wh-1', name: 'Magazijn Antwerpen', locationId: 'loc-1', locationLabel: 'Antwerpen',
  isActive: true, opensAt: '06:00', closesAt: '20:00',
  contactName: null, contactPhone: null, contactEmail: null, notes: null,
  docks: [{ id: 'dock-1', code: 'D1', isActive: true }],
} as unknown as Warehouse

const board = {
  warehouseId: 'wh-1', date: '2026-07-15', opensAt: '06:00', closesAt: '20:00',
  docks: [{ id: 'dock-1', code: 'D1', isActive: true }],
  appointments: [appointment()],
  queue: [],
} as unknown as DockBoard

const dashboard = {
  warehouseId: 'wh-1', date: '2026-07-15',
  expectedToday: 1, waiting: 0, inProgress: 0, completed: 0, delayed: 0, noShows: 0, utilization: [],
} as unknown as WarehouseDashboard

beforeEach(() => {
  vi.clearAllMocks()
  api.listWarehouses.mockResolvedValue([warehouse])
  api.getDockBoard.mockResolvedValue(board)
  api.getWarehouseDashboard.mockResolvedValue(dashboard)
})

describe('dockBlockPosition', () => {
  it('positions the block on the stored wall clock, not on the browser clock', () => {
    // 08:00 in a 06:00–20:00 window (840 min) → 120/840 of the width.
    const { leftPct } = dockBlockPosition('2026-07-15T08:00:00Z', '2026-07-15T09:00:00Z', '06:00', '20:00')
    expect(leftPct).toBeCloseTo((120 / 840) * 100, 5)
  })

  it('gives the same position whatever the browser zone would have said', () => {
    const naive = dockBlockPosition('2026-07-15T08:00:00', '2026-07-15T09:00:00', '06:00', '20:00')
    const stamped = dockBlockPosition('2026-07-15T08:00:00Z', '2026-07-15T09:00:00Z', '06:00', '20:00')
    expect(stamped).toEqual(naive)
  })
})

describe('DockPlanningPage — one clock per screen (A13)', () => {
  it('shows the same wall clock in the detail label and in the edit field', async () => {
    render(<DockPlanningPage />)

    const block = await screen.findByRole('button', { name: /ORD-1/ })
    await userEvent.click(block)

    // The label the planner reads… (built from three text nodes, so match on the dialog)
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('08:00–09:00')
    expect(dialog).not.toHaveTextContent('17:00') // the browser (Tokyo) clock
    expect(dialog).not.toHaveTextContent('10:00') // the tenant-zone projection of a stored wall clock

    // …and the field she types into, after "Bewerken".
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    const start = document.querySelector('input[type="datetime-local"]') as HTMLInputElement
    expect(start.value).toBe('2026-07-15T08:00')
  })
})
