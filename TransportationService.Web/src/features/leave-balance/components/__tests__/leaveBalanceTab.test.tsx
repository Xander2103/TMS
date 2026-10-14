import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
    expect(screen.getByRole('button', { name: /Saldo aanpassen/i })).toBeInTheDocument()
  })

  it('B49: one direct action per row; jaarrecht and historiek live in the row menu', async () => {
    render(<LeaveBalanceTab employeeId="emp-1" />)
    const row = (await screen.findByText('Wettelijk verlof')).closest('tr')!
    // Saldo aanpassen is the only visible button; no stacked Jaarrecht/Historiek buttons.
    expect(within(row).getByRole('button', { name: 'Saldo aanpassen' })).toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: /Jaarrecht instellen/i })).not.toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: /^Historiek$/i })).not.toBeInTheDocument()

    await userEvent.click(within(row).getByRole('button', { name: 'Meer acties voor Wettelijk verlof' }))
    const items = within(screen.getByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent)
    expect(items).toEqual(['Jaarrecht instellen', 'Historiek'])
  })

  it('keeps Jaarrecht instellen in the menu for HR that can manage but not adjust', async () => {
    auth.permissions = ['leave_balances.view', 'leave_balances.manage']
    render(<LeaveBalanceTab employeeId="emp-1" />)
    const row = (await screen.findByText('Wettelijk verlof')).closest('tr')!
    expect(within(row).queryByRole('button', { name: 'Saldo aanpassen' })).not.toBeInTheDocument()
    await userEvent.click(within(row).getByRole('button', { name: 'Meer acties voor Wettelijk verlof' }))
    expect(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Jaarrecht instellen' })).toBeInTheDocument()
  })

  it('hides management actions without manage/adjust permissions (read-only)', async () => {
    auth.permissions = ['leave_balances.view']
    render(<LeaveBalanceTab employeeId="emp-1" />)
    await waitFor(() => expect(screen.getByText('Wettelijk verlof')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /Jaarrecht instellen/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Saldo aanpassen/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Meer acties/i })).not.toBeInTheDocument()
  })
})
