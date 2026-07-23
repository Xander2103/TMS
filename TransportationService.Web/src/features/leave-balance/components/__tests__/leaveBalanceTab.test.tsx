import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import * as api from '../../api/leaveBalanceApi'
import { LeaveBalanceTab } from '../LeaveBalanceTab'
import type { EmployeeLeaveBalance } from '../../types'

const auth = vi.hoisted(() => ({ permissions: ['leave_balances.view', 'leave_balances.manage', 'leave_balances.adjust'] }))

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

const balance: EmployeeLeaveBalance = {
  employeeId: 'emp-1',
  year: new Date().getFullYear(),
  rows: [
    {
      balanceTypeId: 'b1', balanceTypeCode: 'WETTELIJK', balanceTypeName: 'Wettelijk verlof',
      baseEntitlementDays: 20, carryOverDays: 2, manualAdjustmentDays: 1, approvedUsedDays: 5,
      pendingReservedDays: 3, remainingDays: 15, pendingReserves: true,
    },
  ],
}

beforeEach(() => {
  auth.permissions = ['leave_balances.view', 'leave_balances.manage', 'leave_balances.adjust']
  vi.spyOn(api, 'getEmployeeLeaveBalance').mockResolvedValue(balance)
})

describe('LeaveBalanceTab', () => {
  it('shows the balance rows and HR management actions with permission', async () => {
    render(<LeaveBalanceTab employeeId="emp-1" />)
    expect(await screen.findByText('Wettelijk verlof')).toBeInTheDocument()
    expect(screen.getByText('15')).toBeInTheDocument() // remaining
    expect(screen.getByText(/gereserveerd/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Jaarrecht instellen/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Saldo aanpassen/i })).toBeInTheDocument()
  })

  it('hides management actions without manage/adjust permissions (read-only)', async () => {
    auth.permissions = ['leave_balances.view']
    render(<LeaveBalanceTab employeeId="emp-1" />)
    await waitFor(() => expect(screen.getByText('Wettelijk verlof')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /Jaarrecht instellen/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Saldo aanpassen/i })).not.toBeInTheDocument()
  })
})
