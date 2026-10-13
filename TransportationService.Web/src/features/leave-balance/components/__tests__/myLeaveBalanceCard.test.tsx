import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import * as api from '../../api/leaveBalanceApi'
import { MyLeaveBalanceCard } from '../MyLeaveBalanceCard'
import type { EmployeeLeaveBalance } from '../../types'

const auth = vi.hoisted(() => ({ permissions: ['leave_balances.view_own'] }))

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

const balance: EmployeeLeaveBalance = {
  employeeId: 'me',
  year: new Date().getFullYear(),
  rows: [
    {
      balanceTypeId: 'b1', balanceTypeCode: 'WETTELIJK', balanceTypeName: 'Wettelijk verlof',
      baseEntitlementDays: 20, carryOverDays: 0, manualAdjustmentDays: 0, approvedUsedDays: 4,
      pendingReservedDays: 1, remainingDays: 15, pendingReserves: true,
    },
  ],
}

beforeEach(() => {
  auth.permissions = ['leave_balances.view_own']
  vi.spyOn(api, 'getMyLeaveBalance').mockResolvedValue(balance)
})

describe('MyLeaveBalanceCard', () => {
  it('renders the own balance read-only, without any edit controls', async () => {
    render(<MyLeaveBalanceCard />)
    expect(await screen.findByText('Wettelijk verlof')).toBeInTheDocument()
    expect(screen.getByText('Mijn verlofsaldo')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Jaarrecht instellen/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Saldo aanpassen/i })).not.toBeInTheDocument()
  })

  it('renders nothing without the view-own permission', () => {
    auth.permissions = []
    vi.mocked(api.getMyLeaveBalance).mockClear()
    const { container } = render(<MyLeaveBalanceCard />)
    expect(container).toBeEmptyDOMElement()
    expect(api.getMyLeaveBalance).not.toHaveBeenCalled()
  })
})
