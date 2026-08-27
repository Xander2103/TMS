import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerHistoryPanel } from '../CustomerHistoryPanel'
import type { CustomerHistoryPage } from '../../api/customerHistoryApi'

const getHistorySpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/customerHistoryApi', () => ({
  getCustomerHistory: (...args: unknown[]) => getHistorySpy(...args),
}))

function page(): CustomerHistoryPage {
  return {
    items: [
      {
        id: 'a0a0a0a0-1111-2222-3333-444444444444',
        timestamp: '2026-08-01T10:00:00Z',
        userName: 'An Peeters',
        action: 'Updated',
        actionLabel: 'Gewijzigd',
        category: 'Klant',
        categoryCode: 'customer',
        summary: 'Naam gewijzigd.',
        changes: [{ field: 'Naam', before: 'Acme', after: 'Acme BV' }],
      },
      {
        id: 'b1b1b1b1-1111-2222-3333-444444444444',
        timestamp: '2026-07-30T08:30:00Z',
        userName: null,
        action: 'ContactAdded',
        actionLabel: 'Contactpersoon toegevoegd',
        category: 'Contactpersonen',
        categoryCode: 'contacts',
        summary: 'Contactpersoon Jan Claes toegevoegd.',
        changes: [],
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 25,
  }
}

describe('CustomerHistoryPanel', () => {
  beforeEach(() => {
    getHistorySpy.mockReset()
    getHistorySpy.mockResolvedValue(page())
  })

  it('renders entries with actor, category badge and expandable change table — no raw ids', async () => {
    render(<CustomerHistoryPanel customerId="c1" />)

    expect(await screen.findByText('Gewijzigd door An Peeters')).toBeInTheDocument()
    expect(screen.getByText('Contactpersoon toegevoegd door Systeem')).toBeInTheDocument()
    expect(screen.getByText('Naam gewijzigd.')).toBeInTheDocument()
    // Raw entity/audit ids never surface.
    expect(document.body.textContent).not.toContain('a0a0a0a0')

    // Veld/Voor/Na table only after expanding.
    expect(screen.queryByText('Acme BV')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Uitklappen' }))
    expect(screen.getByText('Veld')).toBeInTheDocument()
    expect(screen.getByText('Acme')).toBeInTheDocument()
    expect(screen.getByText('Acme BV')).toBeInTheDocument()
  })

  it('refetches with the chosen category CODE when a chip is clicked', async () => {
    render(<CustomerHistoryPanel customerId="c1" />)
    await screen.findByText('Naam gewijzigd.')
    expect(getHistorySpy).toHaveBeenCalledWith('c1', 1, 25, null)

    await userEvent.click(screen.getByRole('button', { name: 'Contactpersonen' }))
    await waitFor(() => expect(getHistorySpy).toHaveBeenCalledWith('c1', 1, 25, 'contacts'))

    await userEvent.click(screen.getByRole('button', { name: 'Alles' }))
    await waitFor(() => expect(getHistorySpy).toHaveBeenLastCalledWith('c1', 1, 25, null))
  })

  it('shows the empty state when there is no history', async () => {
    getHistorySpy.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })
    render(<CustomerHistoryPanel customerId="c1" />)
    expect(await screen.findByText('Nog geen historiek voor deze klant.')).toBeInTheDocument()
  })
})
