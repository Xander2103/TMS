import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/leaveBalanceApi'
import { LeaveSettingsPage } from '../LeaveSettingsPage'
import type { LeaveBalanceType, LeaveSettings, LeaveType } from '../../types'

const auth = vi.hoisted(() => ({ permissions: ['leave_types.manage'] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

const settings: LeaveSettings = {
  defaultAnnualEntitlementDays: 20, pendingReservesBalance: true, allowNegativeBalance: false,
  carryOverEnabled: true, maxCarryOverDays: null,
}
const balanceTypes: LeaveBalanceType[] = [
  { id: 'b1', code: 'WETTELIJK', name: 'Wettelijk verlof', description: null, isActive: true, sortOrder: 1 },
]
const leaveTypes: LeaveType[] = [
  {
    id: 'l1', code: 'WETTELIJK', name: 'Wettelijk verlof', description: null, isActive: true, isPaid: true,
    deductsFromBalance: true, balanceTypeId: 'b1', absenceType: 'Vacation', requiresApproval: true,
    allowsHalfDays: true, requiresReason: false, requiresAttachment: false, visibleInSelfService: true,
    colour: '#2563eb', sortOrder: 1,
  },
]

beforeEach(() => {
  auth.permissions = ['leave_types.manage']
  vi.spyOn(api, 'getLeaveSettings').mockResolvedValue(settings)
  vi.spyOn(api, 'getLeaveBalanceTypes').mockResolvedValue(balanceTypes)
  vi.spyOn(api, 'getLeaveTypes').mockResolvedValue(leaveTypes)
})

function renderPage() {
  return render(<MemoryRouter><LeaveSettingsPage /></MemoryRouter>)
}

describe('LeaveSettingsPage', () => {
  it('lists balance types and leave types with management actions', async () => {
    renderPage()
    expect(await screen.findByText('Saldotypes')).toBeInTheDocument()
    expect(screen.getByText('Verloftypes')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Saldotype' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Verloftype' })).toBeInTheDocument()
  })

  it('hides management actions without leave_types.manage', async () => {
    auth.permissions = []
    renderPage()
    expect(await screen.findByText('Saldotypes')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Saldotype' })).not.toBeInTheDocument()
  })
})
