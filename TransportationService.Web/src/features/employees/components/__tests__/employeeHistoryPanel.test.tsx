import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { EmployeeHistoryPanel } from '../EmployeeHistoryPanel'
import type { EmployeeHistoryPage } from '../../api/employeeHistoryApi'

const state = vi.hoisted(() => ({
  page: { items: [], totalCount: 0, page: 1, pageSize: 25 } as EmployeeHistoryPage,
}))

vi.mock('../../api/employeeHistoryApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/employeeHistoryApi')>()),
  getEmployeeHistory: () => Promise.resolve(state.page),
}))

describe('EmployeeHistoryPanel', () => {
  beforeEach(() => {
    state.page = { items: [], totalCount: 0, page: 1, pageSize: 25 }
  })

  it('renders one card per save with actor, category and Voor/Na rows', async () => {
    state.page = {
      items: [
        {
          id: 'log-1',
          timestamp: '2026-07-27T14:32:00',
          userName: 'Xander Van Malder',
          action: 'Updated',
          actionLabel: 'Gewijzigd',
          category: 'Profiel',
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
          changes: [],
        },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 25,
    }
    render(<EmployeeHistoryPanel employeeId="emp-1" />)

    expect(await screen.findByText('Gewijzigd door Xander Van Malder')).toBeInTheDocument()
    expect(screen.getByText('Profiel')).toBeInTheDocument()
    const card = screen.getByText('Gewijzigd door Xander Van Malder').closest('article')!
    const rows = within(card).getAllByRole('row')
    expect(within(rows[1]).getByText('Telefoonnummer')).toBeInTheDocument()
    expect(within(rows[1]).getByText('0470 12 34 56')).toBeInTheDocument()
    expect(within(rows[1]).getByText('0485 98 76 54')).toBeInTheDocument()

    // A change-less entry (e.g. Created on a child) still shows its action, never an empty table.
    expect(screen.getByText('Aangemaakt door Systeem')).toBeInTheDocument()
    expect(screen.getByText('Kwalificaties')).toBeInTheDocument()
  })

  it('shows an empty state without history', async () => {
    render(<EmployeeHistoryPanel employeeId="emp-1" />)
    expect(await screen.findByText('Nog geen historiek voor deze medewerker.')).toBeInTheDocument()
  })
})
