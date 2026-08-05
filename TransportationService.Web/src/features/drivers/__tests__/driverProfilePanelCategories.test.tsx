import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DriverProfilePanel } from '../components/DriverProfilePanel'
import type { DriverDetail } from '../types'

// Driver multi-category edit parity (master-data wave phase 8): the panel shows every
// category, and editing offers an ordered multi-select where the FIRST ticked category is
// the primary (mirroring the create form's driverCategoryIds semantics).

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['drivers.edit']) }))
const api = vi.hoisted(() => ({
  updateDriver: vi.fn(),
}))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.has(c)),
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [
      { id: 'cat-nat', code: 'NAT', name: 'Nationaal' },
      { id: 'cat-int', code: 'INT', name: 'Internationaal' },
      { id: 'cat-nacht', code: 'NACHT', name: 'Nachtritten' },
    ],
    isLoading: false,
    error: null,
  }),
}))
vi.mock('../../fleet-assignment/AssignmentSlot', () => ({
  AssignmentSlot: () => null,
}))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: () => Promise.resolve([]) }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: () => Promise.resolve([]) }))

function driverDetail(overrides: Partial<DriverDetail> = {}): DriverDetail {
  return {
    id: 'drv-1',
    driverNumber: 'CH-0001',
    employeeId: 'emp-1',
    fullName: 'Jan Peeters',
    employeeNumber: 'MED-0001',
    categoryId: 'cat-nat',
    categoryName: 'Nationaal',
    categoryIds: ['cat-nat', 'cat-int'],
    categoryNames: ['Nationaal', 'Internationaal'],
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
  deleteDriver: vi.fn(),
}))

function renderPanel() {
  render(
    <MemoryRouter>
      <DriverProfilePanel driverId="drv-1" />
    </MemoryRouter>,
  )
}

describe('DriverProfilePanel — multi-category editing', () => {
  beforeEach(() => {
    auth.permissions = new Set(['drivers.edit'])
    api.updateDriver.mockReset()
    api.updateDriver.mockResolvedValue(driverDetail())
  })

  it('shows all category names read-only, without an edit button when drivers.edit is missing', async () => {
    auth.permissions = new Set()
    renderPanel()

    expect(await screen.findByText('Nationaal, Internationaal')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })

  it('offers a multi-select in edit mode and marks the first selected category as primary', async () => {
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: 'Bewerken' }))

    expect(screen.getByRole('checkbox', { name: 'Nationaal (primair)' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Internationaal' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Nachtritten' })).not.toBeChecked()
  })

  it('sends driverCategoryIds in selection order with the first entry as primary', async () => {
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: 'Bewerken' }))

    // Drop the current primary and add a new category: Internationaal moves to the front.
    await userEvent.click(screen.getByRole('checkbox', { name: 'Nationaal (primair)' }))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Nachtritten' }))
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.updateDriver).toHaveBeenCalledTimes(1))
    expect(api.updateDriver).toHaveBeenCalledWith(
      'drv-1',
      expect.objectContaining({
        driverCategoryIds: ['cat-int', 'cat-nacht'],
        driverCategoryId: 'cat-int',
      }),
    )
  })

  it('can clear every category (empty list, primary null)', async () => {
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: 'Bewerken' }))

    await userEvent.click(screen.getByRole('checkbox', { name: 'Nationaal (primair)' }))
    // Internationaal is now the primary, so its accessible name gained the suffix.
    await userEvent.click(screen.getByRole('checkbox', { name: 'Internationaal (primair)' }))
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.updateDriver).toHaveBeenCalledTimes(1))
    expect(api.updateDriver).toHaveBeenCalledWith(
      'drv-1',
      expect.objectContaining({ driverCategoryIds: [], driverCategoryId: null }),
    )
  })
})
