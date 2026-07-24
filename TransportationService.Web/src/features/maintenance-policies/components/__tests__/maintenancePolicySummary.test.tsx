import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MaintenancePolicySummary } from '../MaintenancePolicySummary'
import type { EffectivePolicies } from '../../types'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['maintenance_policies.manage']) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const effective = vi.hoisted(() => ({ value: null as unknown as EffectivePolicies }))
const deleteMock = vi.hoisted(() => vi.fn(() => Promise.resolve()))
const createMock = vi.hoisted(() => vi.fn(() => Promise.resolve({})))

vi.mock('../../api/maintenancePoliciesApi', () => ({
  getEffectivePolicies: () => Promise.resolve(effective.value),
  deleteMaintenancePolicy: deleteMock,
  createMaintenancePolicy: createMock,
}))

describe('MaintenancePolicySummary', () => {
  beforeEach(() => {
    deleteMock.mockClear()
    createMock.mockClear()
    auth.permissions = new Set(['maintenance_policies.manage'])
    effective.value = {
      maintenance: {
        policyId: 'p-asset',
        level: 'Asset',
        sourceLabel: 'Specifieke regel voor voertuig',
        intervalMonths: 6,
        intervalKm: null,
        warningDays: 30,
        description: null,
      },
      inspection: {
        policyId: 'p-default',
        level: 'CompanyDefault',
        sourceLabel: 'Bedrijfsstandaard',
        intervalMonths: 12,
        intervalKm: null,
        warningDays: 30,
        description: null,
      },
    }
  })

  it('shows the source of each effective rule', async () => {
    render(<MaintenancePolicySummary assetKind="Vehicle" assetId="veh-1" />)
    await waitFor(() => expect(screen.getByText('Specifieke regel voor voertuig')).toBeInTheDocument())
    expect(screen.getByText('Bedrijfsstandaard')).toBeInTheDocument()
    expect(screen.getByText('elke 6 maanden')).toBeInTheDocument()
  })

  it('offers reset-to-inherited only for asset-level rules and deletes on confirm', async () => {
    render(<MaintenancePolicySummary assetKind="Vehicle" assetId="veh-1" />)
    await waitFor(() => expect(screen.getByText('Specifieke regel voor voertuig')).toBeInTheDocument())

    const resetButtons = screen.getAllByRole('button', { name: 'Gebruik opnieuw categorie-/bedrijfsstandaard' })
    expect(resetButtons).toHaveLength(1) // only the maintenance rule is asset-level

    await userEvent.click(resetButtons[0])
    await userEvent.click(screen.getByRole('button', { name: 'Verwijderen' }))
    await waitFor(() => expect(deleteMock).toHaveBeenCalledWith('p-asset'))
  })

  it('creates an asset override via the dialog', async () => {
    render(<MaintenancePolicySummary assetKind="Vehicle" assetId="veh-1" />)
    await waitFor(() => expect(screen.getByText('Bedrijfsstandaard')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Afwijkende regel instellen' }))
    await userEvent.type(screen.getByLabelText('Interval (maanden)'), '6')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(createMock).toHaveBeenCalledWith(
        expect.objectContaining({ kind: 'Inspection', vehicleId: 'veh-1', trailerId: null, intervalMonths: 6 }),
      ),
    )
  })

  it('hides management controls without the permission', async () => {
    auth.permissions = new Set()
    render(<MaintenancePolicySummary assetKind="Vehicle" assetId="veh-1" />)
    await waitFor(() => expect(screen.getByText('Bedrijfsstandaard')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /Afwijkende regel|Gebruik opnieuw/ })).not.toBeInTheDocument()
  })
})
