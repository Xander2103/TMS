import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { PlanningPage } from '../pages/PlanningPage'

// FL-M13: a link carrying ?date= opens the trip list on that day.

vi.mock('../../auth/authContextValue', () => ({ useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }) }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }) }))
const planningApi = vi.hoisted(() => ({
  listTrips: vi.fn().mockResolvedValue({ items: [], truncated: false, limit: null }),
  getPlanningProposals: vi.fn().mockResolvedValue({ date: '2026-10-29', proposals: [], excluded: [] }),
  createTrip: vi.fn(),
}))
vi.mock('../api/planningApi', () => planningApi)
vi.mock('../../drivers/api/driversApi', () => ({ searchDrivers: vi.fn().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 }) }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: vi.fn().mockResolvedValue([]) }))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: vi.fn().mockResolvedValue([]) }))

describe('PlanningPage ?date=', () => {
  it('starts on the requested day', async () => {
    const router = createMemoryRouter([{ path: '/planning', element: <PlanningPage /> }], { initialEntries: ['/planning?date=2026-10-29'] })
    render(<RouterProvider router={router} />)
    await waitFor(() => expect(planningApi.listTrips).toHaveBeenCalledWith(expect.objectContaining({ from: '2026-10-29', to: '2026-10-29' })))
    expect(screen.getByLabelText('Datum')).toHaveValue('2026-10-29')
  })
})
