import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { PlanningPage } from '../pages/PlanningPage'
import { localIsoDate, addDaysIso } from '../tripGroups'
import type { TripListItem } from '../types'

const auth = vi.hoisted(() => ({ canFix: true }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => auth.canFix }),
}))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }) }))
const planningApi = vi.hoisted(() => ({ listTrips: vi.fn(), getPlanningProposals: vi.fn(), createTrip: vi.fn() }))
vi.mock('../api/planningApi', () => planningApi)
vi.mock('../../drivers/api/driversApi', () => ({ searchDrivers: vi.fn().mockResolvedValue({ items: [] }) }))
vi.mock('../../vehicles/api/vehiclesApi', () => ({ getVehicleOptions: vi.fn().mockResolvedValue([]) }))
vi.mock('../../trailers/api/trailersApi', () => ({ getTrailerOptions: vi.fn().mockResolvedValue([]) }))

const TODAY = localIsoDate()

function trip(id: string, tripDate: string, status: TripListItem['status']): TripListItem {
  return {
    id, tripNumber: `RIT-${id}`, tripDate, status, driverId: null, driverName: 'Jan Peeters', vehicleId: null,
    vehicleNumber: null, vehicleLicensePlate: null, trailerId: null, trailerNumber: null, orderCount: 2, blockingConflictCount: 0,
  }
}

function renderPage() {
  const router = createMemoryRouter([{ path: '/planning', element: <PlanningPage /> }, { path: '/planning/:id', element: <p>detail</p> }], { initialEntries: ['/planning'] })
  render(<RouterProvider router={router} />)
}

describe('Ritlijst — operational groups (B20)', () => {
  beforeEach(() => {
    auth.canFix = true
    const runningOld = trip('0021', addDaysIso(TODAY, -3), 'InProgress')
    const runningToday = trip('0025', TODAY, 'InProgress')
    const overdue = trip('0019', addDaysIso(TODAY, -1), 'Planned')
    const plannedToday = trip('0026', TODAY, 'Planned')
    const page = (items: unknown[]) => Promise.resolve({ items, truncated: false, limit: null })
    planningApi.listTrips.mockImplementation((params: { from?: string; status?: string }) => {
      if (params.status === 'InProgress') return page([runningOld, runningToday])
      if (params.from === TODAY) return page([runningToday, plannedToday])
      return page([runningOld, overdue])
    })
    planningApi.getPlanningProposals.mockResolvedValue({
      date: TODAY,
      proposals: [
        { zoneCode: 'KEMP', zoneName: 'Turnhout → Geel', totalWeightKg: 1200, totalLoadingMeters: 0, totalPallets: 0, explanations: ['Zelfde leverzone'],
          orders: [1, 2, 3].map((n) => ({ transportOrderId: `o-${n}`, orderNumber: `ORD-00${n}`, customerName: 'Klant', orderDate: TODAY, deliveryCity: 'Geel', deliveryPostalCode: '2440', weightKg: 400, loadingMeters: null, palletCount: null, overdue: n === 1, constraints: [] })) },
      ],
      excluded: ['ORD-0099: geen losstop — vul de route aan.'],
    })
  })

  it('a trip that started days ago stays visible under Lopend; every trip appears once', async () => {
    renderPage()
    const running = (await screen.findByText('Lopend')).closest('tbody')!
    expect(within(running).getByText('RIT-0021')).toBeInTheDocument()
    expect(within(running).getByText(/^gestart /)).toBeInTheDocument()
    expect(within(running).getByText('RIT-0025')).toBeInTheDocument()

    const overdue = screen.getByText('Achterstallig').closest('tbody')!
    expect(within(overdue).getByText('RIT-0019')).toBeInTheDocument()
    expect(within(overdue).getByText('niet vertrokken')).toBeInTheDocument()

    const day = screen.getByText('Vandaag', { selector: '.pl-group-title' }).closest('tbody')!
    expect(within(day).getByText('RIT-0026')).toBeInTheDocument()
    expect(within(day).queryByText('RIT-0025')).not.toBeInTheDocument()
    expect(within(day).getByText(/1 rit van deze dag staat hierboven/)).toBeInTheDocument()
    expect(screen.getAllByText('RIT-0025')).toHaveLength(1)
  })

  it('proposals read as suggestions with one action; data gaps only for users who can fix them', async () => {
    renderPage()
    const suggestion = (await screen.findByText('Turnhout → Geel')).closest('li')!
    expect(suggestion).toHaveTextContent('3 opdrachten kunnen gecombineerd worden')
    expect(within(suggestion).getByRole('button', { name: 'Maak rit' })).toBeInTheDocument()
    expect(screen.getByText('1 opdracht niet voorgesteld — aan te vullen')).toBeInTheDocument()
  })

  it('hides configuration gaps from dispatchers who cannot fix orders', async () => {
    auth.canFix = false
    renderPage()
    await screen.findByText('Turnhout → Geel')
    expect(screen.queryByText(/niet voorgesteld/)).not.toBeInTheDocument()
  })
})
