import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { EscalationSettingsPage } from '../pages/EscalationSettingsPage'
import type { EscalationPolicy } from '../types'
import * as api from '../api/escalationsApi'

const auth = vi.hoisted(() => ({ permissions: ['escalations.manage'] }))
const toasts = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'user-1' },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toasts.showSuccess, showError: toasts.showError }),
}))
vi.mock('../../roles/hooks/usePermissions', () => ({
  usePermissions: () => ({
    permissions: [
      { id: 'p1', code: 'inventory.manage', module: 'Voorraad', action: 'manage', description: 'Voorraad beheren' },
      { id: 'p2', code: 'tasks.view_all', module: 'Taken', action: 'view_all', description: 'Alle taken bekijken' },
    ],
    isLoading: false,
    error: null,
  }),
}))

function makePolicy(overrides: Partial<EscalationPolicy>): EscalationPolicy {
  return {
    id: 'esc-1',
    kind: 'NegativeStockUnresolved',
    delayHours: 24,
    targetPermissionCode: 'inventory.manage',
    isActive: true,
    ...overrides,
  }
}

const policies: EscalationPolicy[] = [
  makePolicy({ id: 'esc-1', kind: 'NegativeStockUnresolved', delayHours: 24 }),
  makePolicy({ id: 'esc-2', kind: 'TaskOverdue', delayHours: 48, targetPermissionCode: 'tasks.view_all', isActive: false }),
]

beforeEach(() => {
  vi.restoreAllMocks()
  toasts.showSuccess.mockClear()
  toasts.showError.mockClear()
  auth.permissions = ['escalations.manage']
  vi.spyOn(api, 'listEscalationPolicies').mockResolvedValue(policies.map((p) => ({ ...p })))
  vi.spyOn(api, 'updateEscalationPolicy').mockImplementation((kind, input) =>
    Promise.resolve(makePolicy({ kind, ...input })),
  )
})

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/settings/escalations']}>
      <EscalationSettingsPage />
    </MemoryRouter>,
  )
}

describe('EscalationSettingsPage', () => {
  it('renders one row per policy with Dutch labels and current values', async () => {
    renderPage()

    expect(await screen.findByText('Negatieve voorraad onopgelost')).toBeInTheDocument()
    expect(screen.getByText('Taak achterstallig')).toBeInTheDocument()

    expect(screen.getByLabelText('Termijn in uren: Negatieve voorraad onopgelost')).toHaveValue(24)
    expect(screen.getByLabelText('Actief: Taak achterstallig')).not.toBeChecked()
    expect(screen.getByLabelText('Doel-permissie: Taak achterstallig')).toHaveValue('tasks.view_all')
    // The permission select shows module + description.
    expect(screen.getAllByText('Voorraad — Voorraad beheren').length).toBeGreaterThan(0)
  })

  it('sends the edited row as PUT payload and shows a success toast', async () => {
    renderPage()
    await screen.findByText('Negatieve voorraad onopgelost')

    fireEvent.change(screen.getByLabelText('Termijn in uren: Negatieve voorraad onopgelost'), {
      target: { value: '12' },
    })
    fireEvent.change(screen.getByLabelText('Doel-permissie: Negatieve voorraad onopgelost'), {
      target: { value: 'tasks.view_all' },
    })
    fireEvent.click(screen.getByLabelText('Actief: Negatieve voorraad onopgelost'))

    const saveButtons = screen.getAllByRole('button', { name: 'Opslaan' })
    fireEvent.click(saveButtons[0])

    await waitFor(() =>
      expect(api.updateEscalationPolicy).toHaveBeenCalledWith('NegativeStockUnresolved', {
        delayHours: 12,
        targetPermissionCode: 'tasks.view_all',
        isActive: false,
      }),
    )
    await waitFor(() => expect(toasts.showSuccess).toHaveBeenCalled())
  })

  it('rejects an invalid delay without calling the api', async () => {
    renderPage()
    await screen.findByText('Negatieve voorraad onopgelost')

    fireEvent.change(screen.getByLabelText('Termijn in uren: Negatieve voorraad onopgelost'), {
      target: { value: '' },
    })
    fireEvent.click(screen.getAllByRole('button', { name: 'Opslaan' })[0])

    await waitFor(() => expect(toasts.showError).toHaveBeenCalled())
    expect(api.updateEscalationPolicy).not.toHaveBeenCalled()
  })

  it('shows a permission message without escalations.manage', () => {
    auth.permissions = []
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om escalatieregels te beheren.')).toBeInTheDocument()
    expect(api.listEscalationPolicies).not.toHaveBeenCalled()
  })
})
