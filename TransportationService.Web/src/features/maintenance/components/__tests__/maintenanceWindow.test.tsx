import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MaintenancePanel } from '../MaintenancePanel'
import type { MaintenanceRecord } from '../../types'

// FL-M14/H2/H4: a maintenance job has an availability window and can be started.

vi.mock('../../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: () => true }) }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))
const api = vi.hoisted(() => ({
  listMaintenance: vi.fn(),
  createMaintenance: vi.fn(),
  updateMaintenance: vi.fn(),
  completeMaintenance: vi.fn(),
  deleteMaintenance: vi.fn(),
}))
vi.mock('../../api/maintenanceApi', () => api)

const record = (overrides: Partial<MaintenanceRecord> = {}): MaintenanceRecord => ({
  id: 'm1', vehicleId: 'v1', trailerId: null, maintenanceType: 'PeriodicService', customTypeName: null, status: 'Planned',
  isOverdue: false, description: 'Grote beurt', scheduledDate: '2026-10-22', scheduledEndDate: '2026-10-23', odometerTriggerKm: null,
  completedDate: null, completedOdometerKm: null, workPerformed: null, provider: 'Garage Peeters', cost: null, nextServiceDate: null,
  nextServiceOdometerKm: null, intervalMonths: null, intervalKm: null, hasAttachment: false, notes: null, ...overrides,
})

beforeEach(() => {
  vi.clearAllMocks()
  api.listMaintenance.mockResolvedValue([record()])
  api.createMaintenance.mockResolvedValue(record())
  api.updateMaintenance.mockResolvedValue(record({ status: 'InProgress' }))
})

describe('MaintenancePanel — availability window', () => {
  it('lists the window and starts a planned job', async () => {
    render(<MaintenancePanel ownerType="vehicle" ownerId="v1" />)
    const row = (await screen.findByText('Grote beurt')).closest('tr')!
    expect(row).toHaveTextContent('22/10/2026 – 23/10/2026')
    await userEvent.click(within(row).getByRole('button', { name: 'Starten' }))
    await waitFor(() => expect(api.updateMaintenance).toHaveBeenCalledWith('m1', expect.objectContaining({ status: 'InProgress', scheduledEndDate: '2026-10-23' })))
    expect(toast.showSuccess).toHaveBeenCalled()
  })

  it('the end date follows the start date and may not lie before it', async () => {
    api.listMaintenance.mockResolvedValue([])
    render(<MaintenancePanel ownerType="vehicle" ownerId="v1" />)
    await userEvent.click(await screen.findByRole('button', { name: 'Onderhoud plannen' }))
    await userEvent.type(screen.getByLabelText(/Omschrijving/), 'Remmen')
    const start = screen.getByLabelText('Geplande datum')
    await userEvent.type(start, '2026-10-22')
    const end = screen.getByLabelText('Tot en met') as HTMLInputElement
    expect(end.value).toBe('2026-10-22')
    await userEvent.clear(end)
    await userEvent.type(end, '2026-10-20')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('De einddatum moet op of na de startdatum liggen.')
    expect(api.createMaintenance).not.toHaveBeenCalled()

    await userEvent.clear(end)
    await userEvent.type(end, '2026-10-24')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))
    await waitFor(() =>
      expect(api.createMaintenance).toHaveBeenCalledWith('vehicle', 'v1', expect.objectContaining({ scheduledDate: '2026-10-22', scheduledEndDate: '2026-10-24' })),
    )
  })
})
