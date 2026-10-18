import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DriverProfilePanel } from '../components/DriverProfilePanel'
import type { DriverDetail } from '../types'

// Master-data remediation (2026-10-18): DELETE /api/drivers/{id} answers 409
// common.record_referenced when trips/POD/damage reference the driver. The panel explains why
// (server message + reference labels) and offers deactivating instead of a generic toast.

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['drivers.edit', 'drivers.delete']) }))
const api = vi.hoisted(() => ({ updateDriver: vi.fn(), deleteDriver: vi.fn() }))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.has(c)),
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), ...toast }),
}))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../fleet-assignment/AssignmentSlot', () => ({ AssignmentSlot: () => null }))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: () => Promise.resolve([]) }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: () => Promise.resolve([]) }))

function driverDetail(overrides: Partial<DriverDetail> = {}): DriverDetail {
  return {
    id: 'drv-1',
    driverNumber: 'CH-0001',
    employeeId: 'emp-1',
    fullName: 'Jan Peeters',
    employeeNumber: 'MED-0001',
    categoryId: null,
    categoryName: null,
    categoryIds: [],
    categoryNames: [],
    availabilityStatus: 'Available',
    isActive: true,
    isBlocked: false,
    blockReason: null,
    fixedVehicle: null,
    currentVehicle: null,
    fixedTrailerId: null,
    fixedTrailerLabel: null,
    notes: null,
    readiness: { status: 'Ready', blockingReasons: [], warnings: [] },
    qualifications: [],
    ...overrides,
  }
}

vi.mock('../api/driversApi', () => ({
  getDriver: () => Promise.resolve(driverDetail()),
  updateDriver: (...args: unknown[]) => api.updateDriver(...args),
  setDriverBlocked: vi.fn(),
  setDriverVehicle: vi.fn(),
  deleteDriver: (...args: unknown[]) => api.deleteDriver(...args),
}))

const REFUSED_MESSAGE =
  'Deze chauffeur kan niet worden verwijderd omdat er historische of operationele gegevens aan gekoppeld zijn: 12 ritten en 3 afleverbewijzen. Zet de chauffeur inactief.'

function referencedError(canDeactivate = true) {
  return new ApiError(REFUSED_MESSAGE, 409, {
    code: 'common.record_referenced',
    message: REFUSED_MESSAGE,
    references: [
      { key: 'trips', count: 12, label: '12 ritten' },
      { key: 'pods', count: 3, label: '3 afleverbewijzen' },
    ],
    canDeactivate,
  })
}

async function requestDelete() {
  render(
    <MemoryRouter>
      <DriverProfilePanel driverId="drv-1" />
    </MemoryRouter>,
  )
  await userEvent.click(await screen.findByRole('button', { name: 'Verwijderen' }))
  const confirm = await screen.findByRole('dialog', { name: 'Chauffeur verwijderen' })
  await userEvent.click(within(confirm).getByRole('button', { name: 'Verwijderen' }))
}

describe('DriverProfilePanel — refused delete', () => {
  beforeEach(() => {
    auth.permissions = new Set(['drivers.edit', 'drivers.delete'])
    api.updateDriver.mockReset()
    api.deleteDriver.mockReset()
    toast.showError.mockReset()
    toast.showSuccess.mockReset()
  })

  it('shows the server message and the reference labels as bullets', async () => {
    api.deleteDriver.mockRejectedValue(referencedError())
    await requestDelete()

    const dialog = await screen.findByRole('dialog', { name: 'Chauffeur kan niet worden verwijderd' })
    expect(within(dialog).getByText(REFUSED_MESSAGE)).toBeInTheDocument()
    const bullets = within(dialog).getAllByRole('listitem').map((item) => item.textContent)
    expect(bullets).toEqual(['12 ritten', '3 afleverbewijzen'])
    expect(toast.showError).not.toHaveBeenCalled()
  })

  it('offers deactivating the driver instead, keeping the history', async () => {
    api.deleteDriver.mockRejectedValue(referencedError())
    api.updateDriver.mockResolvedValue(driverDetail({ isActive: false }))
    await requestDelete()

    const dialog = await screen.findByRole('dialog', { name: 'Chauffeur kan niet worden verwijderd' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Chauffeur inactief zetten' }))

    await waitFor(() => expect(api.updateDriver).toHaveBeenCalledTimes(1))
    expect(api.updateDriver).toHaveBeenCalledWith('drv-1', expect.objectContaining({ isActive: false, driverCategoryIds: null }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Chauffeur kan niet worden verwijderd' })).not.toBeInTheDocument())
    expect(toast.showSuccess).toHaveBeenCalledWith('Chauffeur inactief gezet. De historiek blijft bewaard.')
  })

  it('only shows the message when the user may not edit the driver', async () => {
    auth.permissions = new Set(['drivers.delete'])
    api.deleteDriver.mockRejectedValue(referencedError())
    await requestDelete()

    const dialog = await screen.findByRole('dialog', { name: 'Chauffeur kan niet worden verwijderd' })
    expect(within(dialog).queryByRole('button', { name: 'Chauffeur inactief zetten' })).not.toBeInTheDocument()
    // The footer's "Sluiten" (next to the modal's own × button, which shares the name).
    expect(within(dialog).getAllByRole('button', { name: 'Sluiten' }).length).toBeGreaterThan(0)
  })

  it('keeps a toast for other failures, with the server message when there is one', async () => {
    api.deleteDriver.mockRejectedValue(new ApiError('Request to /api/drivers/drv-1 failed with status 500', 500))
    await requestDelete()

    await waitFor(() => expect(toast.showError).toHaveBeenCalledWith('Chauffeur kon niet worden verwijderd.'))
    expect(screen.queryByRole('dialog', { name: 'Chauffeur kan niet worden verwijderd' })).not.toBeInTheDocument()
  })
})
