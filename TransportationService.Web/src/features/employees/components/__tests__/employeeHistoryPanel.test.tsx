import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EmployeeHistoryPanel } from '../EmployeeHistoryPanel'
import type { EmployeeHistoryPage } from '../../api/employeeHistoryApi'

const state = vi.hoisted(() => ({
  page: { items: [], totalCount: 0, page: 1, pageSize: 25 } as EmployeeHistoryPage,
}))

const getEmployeeHistory = vi.hoisted(() => vi.fn())

vi.mock('../../api/employeeHistoryApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/employeeHistoryApi')>()),
  getEmployeeHistory: (...args: unknown[]) => {
    getEmployeeHistory(...args)
    return Promise.resolve(state.page)
  },
}))

const RAW_GUID_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

describe('EmployeeHistoryPanel', () => {
  beforeEach(() => {
    state.page = { items: [], totalCount: 0, page: 1, pageSize: 25 }
    getEmployeeHistory.mockClear()
  })

  it('shows the actor/category header and a Summary line, but not the Voor/Na table, while collapsed', async () => {
    state.page = {
      items: [
        {
          id: 'log-1',
          timestamp: '2026-07-27T14:32:00',
          userName: 'Xander Van Malder',
          action: 'Updated',
          actionLabel: 'Gewijzigd',
          category: 'Profiel',
          categoryCode: 'profile',
          summary: 'Telefoonnummer: 0470 12 34 56 → 0485 98 76 54',
          changes: [
            { field: 'Telefoonnummer', before: '0470 12 34 56', after: '0485 98 76 54' },
            { field: 'Status tewerkstelling', before: 'Actief', after: 'Met verlof' },
          ],
        },
        {
          id: 'log-2',
          timestamp: '2026-07-26T09:00:00',
          userName: null,
          action: 'Created',
          actionLabel: 'Aangemaakt',
          category: 'Kwalificaties',
          categoryCode: 'qualifications',
          summary: 'Aangemaakt',
          changes: [],
        },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 25,
    }
    render(<EmployeeHistoryPanel employeeId="emp-1" />)

    expect(await screen.findByText('Gewijzigd door Xander Van Malder')).toBeInTheDocument()
    const card = screen.getByText('Gewijzigd door Xander Van Malder').closest('article')!
    expect(within(card).getByText('Profiel')).toBeInTheDocument()
    expect(within(card).getByText('Telefoonnummer: 0470 12 34 56 → 0485 98 76 54')).toBeInTheDocument()
    // Collapsed: the field-level table is not in the document yet.
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByText('Telefoonnummer', { selector: 'td' })).not.toBeInTheDocument()

    const toggle = within(card).getByRole('button', { name: 'Uitklappen' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')

    // A change-less entry (e.g. Created on a child) still shows its summary, never an empty table.
    const secondCard = screen.getByText('Aangemaakt door Systeem').closest('article')!
    expect(within(secondCard).getByText('Kwalificaties')).toBeInTheDocument()
    expect(within(secondCard).queryByRole('button')).not.toBeInTheDocument()
  })

  it('reveals the Voor/Na table when a card is expanded, and re-collapses it', async () => {
    const user = userEvent.setup()
    state.page = {
      items: [
        {
          id: 'log-1',
          timestamp: '2026-07-27T14:32:00',
          userName: 'Xander Van Malder',
          action: 'Updated',
          actionLabel: 'Gewijzigd',
          category: 'Profiel',
          categoryCode: 'profile',
          summary: 'Telefoonnummer: 0470 12 34 56 → 0485 98 76 54',
          changes: [{ field: 'Telefoonnummer', before: '0470 12 34 56', after: '0485 98 76 54' }],
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    }
    render(<EmployeeHistoryPanel employeeId="emp-1" />)

    const toggle = await screen.findByRole('button', { name: 'Uitklappen' })
    await user.click(toggle)

    expect(await screen.findByRole('table')).toBeInTheDocument()
    const row = screen.getAllByRole('row')[1]
    expect(within(row).getByText('Telefoonnummer')).toBeInTheDocument()
    expect(within(row).getByText('0470 12 34 56')).toBeInTheDocument()
    expect(within(row).getByText('0485 98 76 54')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inklappen' })).toHaveAttribute('aria-expanded', 'true')

    await user.click(screen.getByRole('button', { name: 'Inklappen' }))
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uitklappen' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('passes the selected category CODE to the API and resets to page 1', async () => {
    const user = userEvent.setup()
    render(<EmployeeHistoryPanel employeeId="emp-1" />)
    await screen.findByText('Nog geen historiek voor deze medewerker.')
    getEmployeeHistory.mockClear()

    await user.click(screen.getByRole('button', { name: 'Kwalificaties' }))

    expect(getEmployeeHistory).toHaveBeenCalledWith('emp-1', 1, 25, 'qualifications')
    expect(screen.getByRole('button', { name: 'Kwalificaties' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Alles' })).toHaveAttribute('aria-pressed', 'false')

    await user.click(screen.getByRole('button', { name: 'Alles' }))
    expect(getEmployeeHistory).toHaveBeenCalledWith('emp-1', 1, 25, null)
  })

  it('never renders a raw id anywhere, even for a resolved-name field', async () => {
    state.page = {
      items: [
        {
          id: 'log-1',
          timestamp: '2026-07-27T14:32:00',
          userName: 'Ann HR',
          action: 'Created',
          actionLabel: 'Aangemaakt',
          category: 'Kwalificaties',
          categoryCode: 'qualifications',
          summary: '4 velden gewijzigd (Kwalificatietype, Behaald op, Vervaldatum)',
          changes: [{ field: 'Kwalificatietype', before: null, after: 'ADR-attest' }],
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    }
    render(<EmployeeHistoryPanel employeeId="emp-1" />)

    await screen.findByText('4 velden gewijzigd (Kwalificatietype, Behaald op, Vervaldatum)')
    expect(screen.queryByText(RAW_GUID_ID)).not.toBeInTheDocument()
    expect(document.body.innerHTML).not.toContain(RAW_GUID_ID)
  })

  it('shows an empty state without history', async () => {
    render(<EmployeeHistoryPanel employeeId="emp-1" />)
    expect(await screen.findByText('Nog geen historiek voor deze medewerker.')).toBeInTheDocument()
  })
})
