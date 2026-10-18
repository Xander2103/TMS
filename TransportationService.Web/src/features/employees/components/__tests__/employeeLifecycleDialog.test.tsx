import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../../api/apiClient'
import { EmployeeLifecycleDialog } from '../EmployeeLifecycleDialog'

// Employee offboarding (master-data remediation 2026-10-18): deactivating also blocks the linked
// login, makes the driver unplannable and releases vehicle slots — the dialog says so. A refusal
// (last active administrator) stays in the dialog with the server's Dutch message.

const api = vi.hoisted(() => ({ deactivateEmployee: vi.fn(), reactivateEmployee: vi.fn() }))
vi.mock('../../api/employeesApi', () => ({
  deactivateEmployee: (...args: unknown[]) => api.deactivateEmployee(...args),
  reactivateEmployee: (...args: unknown[]) => api.reactivateEmployee(...args),
}))

describe('EmployeeLifecycleDialog', () => {
  beforeEach(() => {
    api.deactivateEmployee.mockReset()
    api.reactivateEmployee.mockReset()
  })

  it('explains the offboarding consequences before deactivating', () => {
    render(<EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Jan Peeters" onDone={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByText(/gekoppelde gebruikersaccount wordt geblokkeerd/)).toBeInTheDocument()
    expect(screen.getByText(/kan niet meer ingepland worden en de vaste voertuigtoewijzing vervalt/)).toBeInTheDocument()
    expect(screen.getByText(/Historiek \(ritten, afleverbewijzen\) blijft bewaard/)).toBeInTheDocument()
  })

  it('deactivates and reports back', async () => {
    api.deactivateEmployee.mockResolvedValue(undefined)
    const onDone = vi.fn()
    render(<EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Jan Peeters" onDone={onDone} onCancel={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: 'Inactief zetten' }))

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1))
    expect(api.deactivateEmployee).toHaveBeenCalledWith('emp-1')
  })

  it('keeps the dialog open with the server message when the server refuses', async () => {
    const message = 'Het gekoppelde gebruikersaccount is de laatste actieve beheerder en kan niet worden gedeactiveerd.'
    api.deactivateEmployee.mockRejectedValue(new ApiError(message, 400, { detail: message, code: 'common.validation_failed' }))
    const onDone = vi.fn()
    render(<EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Jan Peeters" onDone={onDone} onCancel={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: 'Inactief zetten' }))

    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(onDone).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Inactief zetten' })).toBeEnabled()
  })

  it('tells up front that reactivating does not re-enable the login or the driver', () => {
    render(<EmployeeLifecycleDialog mode="reactivate" employeeId="emp-1" employeeName="Jan Peeters" onDone={vi.fn()} onCancel={vi.fn()} />)

    expect(
      screen.getByText('Het gebruikersaccount en de chauffeur blijven inactief tot een beheerder ze opnieuw activeert.'),
    ).toBeInTheDocument()
  })
})
