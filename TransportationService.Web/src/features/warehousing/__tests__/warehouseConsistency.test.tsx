import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../api/apiClient'
import { WarehousesPage } from '../pages/WarehousesPage'
import { DockPlanningPage } from '../pages/DockPlanningPage'
import { WarehouseDayToggle } from '../components/WarehouseDayToggle'
import { localIsoDate } from '../warehouseDay'
import type { DockAppointment, DockBoard, Warehouse } from '../types'

/** B47: every destructive warehouse action asks first; Vandaag/Morgen is one shared control. */

const api = vi.hoisted(() => ({
  listWarehouses: vi.fn(),
  createWarehouse: vi.fn(),
  updateWarehouse: vi.fn(),
  createDock: vi.fn(),
  updateDock: vi.fn(),
  deleteDock: vi.fn(),
  listWarehouseLocations: vi.fn(),
  createWarehouseLocation: vi.fn(),
  deleteWarehouseLocation: vi.fn(),
  getDockBoard: vi.fn(),
  getWarehouseDashboard: vi.fn(),
  createDockAppointment: vi.fn(),
  updateDockAppointment: vi.fn(),
  deleteDockAppointment: vi.fn(),
  changeDockAppointmentStatus: vi.fn(),
}))
vi.mock('../api/warehousingApi', () => api)
vi.mock('../../locations/api/locationsApi', () => ({ getLocationOptions: vi.fn().mockResolvedValue([]) }))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

const warehouse = {
  id: 'wh-1', name: 'Magazijn Antwerpen', locationId: 'loc-1', locationLabel: 'Antwerpen',
  isActive: true, opensAt: '06:00', closesAt: '20:00',
  contactName: null, contactPhone: null, contactEmail: null, notes: null,
  docks: [{
    id: 'dock-1', code: 'D1', name: null, isActive: true, allowsLoading: true, allowsUnloading: true,
    allowsAdr: false, refrigerated: false, maxVehicleLengthM: null, maxVehicleHeightM: null, notes: null,
  }],
} as unknown as Warehouse

function appointment(): DockAppointment {
  return {
    id: 'app-1', warehouseId: 'wh-1', dockId: 'dock-1', dockCode: 'D1',
    operationType: 'Loading', status: 'Planned',
    plannedStart: '2026-07-15T08:00:00Z', plannedEnd: '2026-07-15T09:00:00Z',
    arrivedAt: null, startedAt: null, completedAt: null, priority: 'Normal',
    tripId: null, tripNumber: null, transportOrderId: 'ord-1', orderNumber: 'ORD-1',
    customerName: null, vehicleId: null, vehicleNumber: null, trailerId: null, trailerNumber: null,
    driverId: null, driverName: null, reference: null, remarks: null,
    packageCount: 0, packagesHandled: 0, hasOpenDiscrepancies: false,
    allowedTransitions: [], version: 'v1',
  } as unknown as DockAppointment
}

beforeEach(() => {
  vi.clearAllMocks()
  api.listWarehouses.mockResolvedValue([warehouse])
  api.listWarehouseLocations.mockResolvedValue([])
  api.deleteDock.mockResolvedValue(undefined)
  api.deleteDockAppointment.mockResolvedValue(undefined)
  api.getDockBoard.mockResolvedValue({
    warehouseId: 'wh-1', date: '2026-07-15', opensAt: '06:00', closesAt: '20:00',
    docks: [{ id: 'dock-1', code: 'D1', isActive: true }], appointments: [appointment()], queue: [],
  } as unknown as DockBoard)
  api.getWarehouseDashboard.mockResolvedValue(null)
})

describe('Magazijnen — dock delete', () => {
  it('asks for confirmation before deleting a dock, and cancelling deletes nothing', async () => {
    render(<WarehousesPage />)
    await screen.findByText('D1')

    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    const dialog = await screen.findByRole('dialog', { name: 'Dock verwijderen' })
    expect(api.deleteDock).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Annuleren' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(api.deleteDock).not.toHaveBeenCalled()
  })

  it('deletes the dock once confirmed', async () => {
    render(<WarehousesPage />)
    await screen.findByText('D1')

    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    const dialog = await screen.findByRole('dialog', { name: 'Dock verwijderen' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Verwijderen' }))

    await waitFor(() => expect(api.deleteDock).toHaveBeenCalledWith('wh-1', 'dock-1'))
  })

  it('explains a refused delete (409 record_referenced) with the server message and the counts', async () => {
    const message =
      'Deze dock kan niet worden verwijderd omdat er historische of operationele gegevens aan gekoppeld zijn: 7 dockafspraken. Zet de dock inactief.'
    api.deleteDock.mockRejectedValue(
      new ApiError(message, 409, {
        code: 'common.record_referenced',
        message,
        references: [{ key: 'appointments', count: 7, label: '7 dockafspraken' }],
        canDeactivate: true,
      }),
    )
    render(<WarehousesPage />)
    await screen.findByText('D1')

    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Dock verwijderen' })).getByRole('button', { name: 'Verwijderen' }))

    const refused = await screen.findByRole('dialog', { name: 'Dock kan niet worden verwijderd' })
    expect(within(refused).getByText(message)).toBeInTheDocument()
    expect(within(refused).getByRole('listitem')).toHaveTextContent('7 dockafspraken')
    expect(screen.queryByRole('dialog', { name: 'Dock verwijderen' })).not.toBeInTheDocument()
  })
})

describe('Dockplanning — appointment delete', () => {
  it('confirms before deleting an appointment', async () => {
    render(<DockPlanningPage />)
    await userEvent.click(await screen.findByRole('button', { name: /ORD-1/ }))
    const detail = await screen.findByRole('dialog')
    await userEvent.click(within(detail).getByRole('button', { name: 'Verwijderen' }))

    const confirm = await screen.findByRole('dialog', { name: 'Afspraak verwijderen' })
    expect(api.deleteDockAppointment).not.toHaveBeenCalled()
    await userEvent.click(within(confirm).getByRole('button', { name: 'Verwijderen' }))

    await waitFor(() => expect(api.deleteDockAppointment).toHaveBeenCalledWith('app-1', 'v1'))
  })

  it('offers the shared Vandaag/Morgen control next to the warehouse select', async () => {
    render(<DockPlanningPage />)
    const group = await screen.findByRole('radiogroup', { name: 'Dag' })
    expect(within(group).getByRole('radio', { name: 'Vandaag' })).toHaveAttribute('aria-checked', 'true')

    await userEvent.click(within(group).getByRole('radio', { name: 'Morgen' }))
    await waitFor(() => expect(api.getDockBoard).toHaveBeenLastCalledWith('wh-1', localIsoDate(1)))
  })
})

describe('WarehouseDayToggle', () => {
  it('marks neither day when the date field points elsewhere', () => {
    render(<WarehouseDayToggle date="2001-01-01" onChange={() => {}} withDatePicker />)
    for (const radio of screen.getAllByRole('radio')) expect(radio).toHaveAttribute('aria-checked', 'false')
    expect(screen.getByLabelText('Andere datum')).toHaveValue('2001-01-01')
  })

  it('computes the local calendar day, not the UTC one', () => {
    // 00:30 local on 15 October: toISOString() could still say the 14th east of UTC.
    expect(localIsoDate(0, new Date(2026, 9, 15, 0, 30))).toBe('2026-10-15')
    expect(localIsoDate(1, new Date(2026, 9, 31, 23, 59))).toBe('2026-11-01')
  })
})
