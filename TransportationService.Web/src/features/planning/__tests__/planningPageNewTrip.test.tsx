import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { PlanningPage } from '../pages/PlanningPage'

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }),
}))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const planningApi = vi.hoisted(() => ({
  listTrips: vi.fn(),
  getPlanningProposals: vi.fn(),
  createTrip: vi.fn(),
}))
vi.mock('../api/planningApi', () => planningApi)
const driversApi = vi.hoisted(() => ({ searchDrivers: vi.fn() }))
vi.mock('../../drivers/api/driversApi', () => driversApi)
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: vi.fn().mockResolvedValue([]) }))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: vi.fn().mockResolvedValue([]) }))

function renderPage() {
  const router = createMemoryRouter(
    [
      { path: '/planning', element: <PlanningPage /> },
      { path: '/planning/:id', element: <p>RITDETAIL</p> },
    ],
    { initialEntries: ['/planning'] },
  )
  render(<RouterProvider router={router} />)
  return router
}

describe('PlanningPage — safe new-trip flow (B06)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    planningApi.listTrips.mockResolvedValue([])
    planningApi.getPlanningProposals.mockResolvedValue({ date: '2026-10-14', proposals: [], excluded: [] })
    driversApi.searchDrivers.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 })
    planningApi.createTrip.mockImplementation(
      () => new Promise((resolve) => setTimeout(() => resolve({ id: 'trip-new', tripNumber: 'RIT-0099' }), 20)),
    )
  })

  it('"Nieuwe rit" opens a dialog; cancelling creates nothing', async () => {
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Nieuwe rit' }))
    const dialog = screen.getByRole('dialog', { name: 'Nieuwe rit' })
    expect(dialog).toHaveTextContent('Datum')
    expect(dialog).toHaveTextContent('Chauffeur')
    await userEvent.click(screen.getByRole('button', { name: 'Annuleren' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(planningApi.createTrip).not.toHaveBeenCalled()
  })

  it('creates exactly one trip on a double click, with the chosen date, and opens it', async () => {
    const router = renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Nieuwe rit' }))
    const dialog = screen.getByRole('dialog', { name: 'Nieuwe rit' })
    const date = within(dialog).getByLabelText('Datum')
    await userEvent.clear(date)
    await userEvent.type(date, '2026-10-20')
    const create = screen.getByRole('button', { name: 'Rit aanmaken' })
    await userEvent.dblClick(create)
    await waitFor(() => expect(router.state.location.pathname).toBe('/planning/trip-new'))
    expect(planningApi.createTrip).toHaveBeenCalledTimes(1)
    expect(planningApi.createTrip).toHaveBeenCalledWith(expect.objectContaining({ tripDate: '2026-10-20', driverId: null, orderIds: [] }))
    expect(screen.getByText('RITDETAIL')).toBeInTheDocument()
  })
})
