import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { RelatedIncidentsNotice } from '../RelatedIncidentsNotice'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['incidents.view']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
const api = vi.hoisted(() => ({ listIncidents: vi.fn() }))
vi.mock('../../api/incidentsApi', () => api)

describe('RelatedIncidentsNotice (M12)', () => {
  it('links the open incidents of an order and skips resolved ones', async () => {
    // PERF-H3: the list endpoint is paged; the notice asks for one page of OPEN incidents.
    api.listIncidents.mockResolvedValue({
      items: [
        { id: 'i-1', title: 'Verkeerde lossplaats', status: 'InProgress' },
        { id: 'i-2', title: 'Oude schade', status: 'Resolved' },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 50,
    })
    render(<MemoryRouter><RelatedIncidentsNotice transportOrderId="o-1" /></MemoryRouter>)
    expect(await screen.findByRole('link', { name: 'Verkeerde lossplaats' })).toHaveAttribute('href', '/incidents/i-1')
    expect(screen.queryByText('Oude schade')).toBeNull()
    expect(api.listIncidents).toHaveBeenCalledWith({ transportOrderId: 'o-1', tripId: undefined, openOnly: true, pageSize: 50 })
  })

  it('does not query incidents for users who cannot see them', () => {
    auth.permissions = new Set()
    api.listIncidents.mockClear()
    render(<MemoryRouter><RelatedIncidentsNotice tripId="t-1" /></MemoryRouter>)
    expect(api.listIncidents).not.toHaveBeenCalled()
  })
})
