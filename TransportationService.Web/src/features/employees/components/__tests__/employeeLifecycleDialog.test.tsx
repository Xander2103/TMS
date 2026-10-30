import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { EmployeeLifecycleDialog } from '../EmployeeLifecycleDialog'

// Employee offboarding (master-data remediation 2026-10-18): deactivating also blocks the linked
// login, makes the driver unplannable and releases vehicle slots — the dialog says so. A refusal
// (last active administrator) stays in the dialog with the server's Dutch message.

const api = vi.hoisted(() => ({ deactivateEmployee: vi.fn(), reactivateEmployee: vi.fn(), getOffboardingImpact: vi.fn() }))
vi.mock('../../api/employeesApi', () => ({
  deactivateEmployee: (...args: unknown[]) => api.deactivateEmployee(...args),
  reactivateEmployee: (...args: unknown[]) => api.reactivateEmployee(...args),
  getOffboardingImpact: (...args: unknown[]) => api.getOffboardingImpact(...args),
}))

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.includes(code) }),
}))

// WH-H5: outstanding company items are listed as a warning — never a block, never auto-returned.
const outstanding = vi.hoisted(() => vi.fn())
vi.mock('../../../issued-items/inventoryApi', () => ({
  getOutstandingEquipment: (...args: unknown[]) => outstanding(...args),
}))

describe('EmployeeLifecycleDialog', () => {
  beforeEach(() => {
    api.deactivateEmployee.mockReset()
    api.reactivateEmployee.mockReset()
    outstanding.mockReset()
    outstanding.mockResolvedValue({ count: 0, items: [] })
    auth.permissions = []
    api.getOffboardingImpact.mockReset()
    api.getOffboardingImpact.mockResolvedValue({ activeAccounts: 0, upcomingTripCount: 0, upcomingTrips: [], vehicles: [], fixedTrailerNumber: null })
  })

  const heavyImpact = {
    activeAccounts: 1,
    upcomingTripCount: 12,
    upcomingTrips: [{ id: 't-1', tripNumber: 'RIT-0241', tripDate: '2026-10-31', status: 'Planned' }],
    vehicles: [{ id: 'v-1', internalNumber: 'VRT-0022', licensePlate: '1-UXG-101', isFixedDriver: true, isCurrentDriver: false }],
    fixedTrailerNumber: 'OPL-0001',
  }

  it('UX-012: shows active work, the assignment that ends and the account before confirming', async () => {
    api.getOffboardingImpact.mockResolvedValue(heavyImpact)
    render(<MemoryRouter><EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Bert" onDone={vi.fn()} onCancel={vi.fn()} /></MemoryRouter>)
    const impact = await screen.findByTestId('offboarding-impact')
    await waitFor(() => expect(impact).toHaveTextContent('Lopende en geplande ritten (12)'))
    expect(impact).toHaveTextContent('een planner moet ze herplannen')
    expect(impact).toHaveTextContent('RIT-0241')
    expect(impact).toHaveTextContent('en nog 11 andere')
    expect(impact).toHaveTextContent('VRT-0022 · 1-UXG-101 (vaste chauffeur)')
    expect(impact).toHaveTextContent('OPL-0001 (vaste oplegger)')
    // HR has no planning.view: the trip number is information, never a dead link.
    expect(screen.queryByRole('link', { name: 'RIT-0241' })).not.toBeInTheDocument()
  })

  it('UX-012: with planning.view the trip opens; a failed preview is said out loud and never blocks', async () => {
    auth.permissions = ['planning.view']
    api.getOffboardingImpact.mockResolvedValue(heavyImpact)
    const { unmount } = render(<MemoryRouter><EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Bert" onDone={vi.fn()} onCancel={vi.fn()} /></MemoryRouter>)
    expect(await screen.findByRole('link', { name: 'RIT-0241' })).toHaveAttribute('href', '/planning/t-1')
    unmount()

    api.getOffboardingImpact.mockRejectedValue(new Error('boom'))
    render(<MemoryRouter><EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Bert" onDone={vi.fn()} onCancel={vi.fn()} /></MemoryRouter>)
    expect(await screen.findByText(/De gevolgen konden niet opgehaald worden/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inactief zetten' })).toBeEnabled()
  })

  it('warns about company items still held, and deactivating can still go ahead', async () => {
    outstanding.mockResolvedValue({
      count: 1,
      items: [{
        itemId: 'i-1', itemCode: 'BM-0001', itemName: 'TEST WH Golden Scanner', variantLabel: null,
        serialNumber: 'SER-GOLD-001', issuedDate: '2026-10-18', expectedReturnDate: '2026-11-01',
      }],
    })
    api.deactivateEmployee.mockResolvedValue(undefined)
    const onDone = vi.fn()
    render(<EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Bert Bosmans" onDone={onDone} onCancel={vi.fn()} />)

    const warning = await screen.findByTestId('offboarding-outstanding')
    expect(warning).toHaveTextContent('Deze medewerker heeft nog 1 bedrijfsmiddel in bezit.')
    expect(warning).toHaveTextContent('BM-0001 · TEST WH Golden Scanner')
    expect(warning).toHaveTextContent('serienummer SER-GOLD-001')
    expect(warning).toHaveTextContent(/uitgegeven 18\/10\/2026/)
    expect(warning).toHaveTextContent(/terug verwacht 01\/11\/2026/)
    expect(warning).toHaveTextContent('de items worden niet automatisch teruggenomen')
    expect(outstanding).toHaveBeenCalledWith('emp-1')
    // Integration (Core + Warehouse): the warning sits next to Core's final offboarding consequences.
    expect(screen.getByText(/gekoppelde gebruikersaccount wordt geblokkeerd/)).toBeInTheDocument()
    expect(screen.getByText(/de vaste\/huidige voertuigtoewijzing en de vaste aanhangwagen vervallen/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Inactief zetten' }))
    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1))
  })

  it('does not look up equipment when reactivating', () => {
    render(<EmployeeLifecycleDialog mode="reactivate" employeeId="emp-1" employeeName="Bert Bosmans" onDone={vi.fn()} onCancel={vi.fn()} />)
    expect(outstanding).not.toHaveBeenCalled()
  })

  it('explains the offboarding consequences before deactivating', () => {
    render(<EmployeeLifecycleDialog mode="deactivate" employeeId="emp-1" employeeName="Jan Peeters" onDone={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByText(/gekoppelde gebruikersaccount wordt geblokkeerd/)).toBeInTheDocument()
    expect(screen.getByText(/kan niet meer ingepland worden en de vaste\/huidige voertuigtoewijzing en de vaste aanhangwagen vervallen/)).toBeInTheDocument()
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
