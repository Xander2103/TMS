import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { MaintenancePoliciesPage } from '../MaintenancePoliciesPage'
import type { MaintenancePolicy } from '../../types'

// MD-M11 (master-data remediation 2026-10-18): the "Geldt voor" column follows the explicit
// `scope` (a rule of a deleted vehicle is no longer shown as "Bedrijfsstandaard"), the dialog
// offers the vehicle/trailer-specific level its help text promises, a second active rule at the
// same level shows the server's refusal, and closing a dirty dialog asks first.

const api = vi.hoisted(() => ({
  list: vi.fn(),
  create: vi.fn(),
  update: vi.fn(),
  remove: vi.fn(),
}))

vi.mock('../../api/maintenancePoliciesApi', () => ({
  listMaintenancePolicies: () => api.list(),
  createMaintenancePolicy: (...args: unknown[]) => api.create(...args),
  updateMaintenancePolicy: (...args: unknown[]) => api.update(...args),
  deleteMaintenancePolicy: (...args: unknown[]) => api.remove(...args),
}))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [{ id: 'cat-1', code: 'BAK', name: 'Bakwagen' }], isLoading: false, error: null }),
}))
vi.mock('../../../vehicles/api/vehiclesApi', () => ({
  getVehicleOptions: () =>
    Promise.resolve([{ id: 'veh-1', internalNumber: 'V-001', licensePlate: '1-ABC-123', brand: null, model: null }]),
}))
vi.mock('../../../trailers/api/trailersApi', () => ({
  getTrailerOptions: () =>
    Promise.resolve([{ id: 'tr-1', internalNumber: 'O-001', licensePlate: 'Q-AAA-111', brand: null, model: null }]),
}))

function policy(overrides: Partial<MaintenancePolicy>): MaintenancePolicy {
  return {
    id: 'p-1',
    kind: 'Maintenance',
    assetKind: 'Vehicle',
    categoryId: null,
    categoryName: null,
    vehicleId: null,
    vehicleNumber: null,
    trailerId: null,
    trailerNumber: null,
    intervalMonths: 12,
    intervalKm: null,
    warningDays: 30,
    description: null,
    isActive: true,
    scope: 'CompanyDefault',
    assetDeleted: false,
    ...overrides,
  }
}

function renderPage() {
  render(
    <MemoryRouter>
      <MaintenancePoliciesPage />
    </MemoryRouter>,
  )
}

describe('MaintenancePoliciesPage', () => {
  beforeEach(() => {
    api.list.mockReset()
    api.create.mockReset()
    api.list.mockResolvedValue([])
  })

  it('labels each rule by its scope and flags a rule of a deleted vehicle', async () => {
    api.list.mockResolvedValue([
      policy({ id: 'p-company' }),
      policy({ id: 'p-cat', scope: 'Category', categoryId: 'cat-1', categoryName: 'Bakwagen' }),
      policy({ id: 'p-veh', scope: 'Vehicle', vehicleId: 'veh-9', vehicleNumber: 'V-009', assetDeleted: true }),
      policy({ id: 'p-tr', assetKind: 'Trailer', scope: 'Trailer', trailerId: 'tr-1', trailerNumber: 'O-001' }),
    ])
    renderPage()

    expect(await screen.findByText('Bedrijfsstandaard')).toBeInTheDocument()
    expect(screen.getByText('Categorie: Bakwagen')).toBeInTheDocument()
    expect(screen.getByText('Voertuig: V-009 (voertuig verwijderd — regel is niet meer van toepassing)')).toBeInTheDocument()
    expect(screen.getByText('Oplegger: O-001')).toBeInTheDocument()
  })

  it('creates a rule for one specific vehicle', async () => {
    api.create.mockResolvedValue(policy({}))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Nieuwe regel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe regel' })

    await user.selectOptions(within(dialog).getByLabelText('Geldt voor'), 'Specifiek voertuig')
    await user.click(within(dialog).getByRole('combobox', { name: /Voertuig/ }))
    await user.click(await screen.findByRole('option', { name: /V-001/ }))
    await user.type(within(dialog).getByLabelText('Interval (maanden)'), '6')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1))
    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({ assetKind: 'Vehicle', vehicleId: 'veh-1', trailerId: null, categoryId: null, intervalMonths: 6 }),
    )
  })

  it('offers "Specifieke oplegger" for trailer rules', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Nieuwe regel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe regel' })

    await user.selectOptions(within(dialog).getByLabelText('Activatype'), 'Oplegger')
    const level = within(dialog).getByLabelText('Geldt voor')
    expect(within(level).getByRole('option', { name: 'Specifieke oplegger' })).toBeInTheDocument()
    expect(within(level).queryByRole('option', { name: 'Specifiek voertuig' })).not.toBeInTheDocument()
  })

  it('asks for the target of a category rule before saving', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Nieuwe regel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe regel' })

    await user.selectOptions(within(dialog).getByLabelText('Geldt voor'), 'Categorie')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    expect((await within(dialog).findAllByText('Kies een categorie.')).length).toBeGreaterThan(0)
    expect(api.create).not.toHaveBeenCalled()
  })

  it('shows the server refusal of a second active rule at the same level', async () => {
    const message = 'Er bestaat al een actieve onderhoudsregel voor de bedrijfsstandaard. Pas die regel aan of zet ze eerst inactief.'
    api.create.mockRejectedValue(new ApiError(message, 400, { detail: message, code: 'common.validation_failed' }))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Nieuwe regel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe regel' })

    await user.type(within(dialog).getByLabelText('Interval (maanden)'), '12')
    await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    expect(await within(dialog).findByText(message)).toBeInTheDocument()
  })

  it('asks before discarding unsaved changes', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Nieuwe regel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe regel' })

    await user.type(within(dialog).getByLabelText('Interval (maanden)'), '12')
    await user.click(within(dialog).getByRole('button', { name: 'Annuleren' }))

    const confirm = await screen.findByRole('dialog', { name: 'Wijzigingen verwerpen?' })
    await user.click(within(confirm).getByRole('button', { name: 'Sluiten zonder opslaan' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nieuwe regel' })).not.toBeInTheDocument())
  })

  it('closes an untouched dialog without asking', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Nieuwe regel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nieuwe regel' })

    await user.click(within(dialog).getByRole('button', { name: 'Annuleren' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nieuwe regel' })).not.toBeInTheDocument())
    expect(screen.queryByRole('dialog', { name: 'Wijzigingen verwerpen?' })).not.toBeInTheDocument()
  })
})
