import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DashboardPage } from '../DashboardPage'
import type { Dashboard } from '../../types'

const navigate = vi.hoisted(() => vi.fn())
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))

const state = vi.hoisted(() => ({ dashboard: null as Dashboard | null }))
vi.mock('../../api/dashboardApi', () => ({
  getDashboard: () => Promise.resolve(state.dashboard),
}))

function baseDashboard(): Dashboard {
  return {
    ordersOpenCount: 0,
    ordersInExecutionCount: 0,
    ordersCompletedThisMonth: 0,
    tripsTodayTotal: 0,
    tripsTodayInProgress: 0,
    tripsTodayWithConflicts: 0,
    revenueInvoicedThisMonth: 0,
    outstandingAmount: 0,
    overdueInvoiceCount: 0,
    driversAbsentToday: 0,
    vehiclesAvailable: 0,
    maintenanceDueCount: 0,
    inspectionsDueCount: 0,
    documentsExpiringCount: 0,
    openDamageCount: 0,
    qualificationsExpiring30d: 0,
    qualificationsExpired: 0,
    openIncidentCount: 0,
    missingPodCount: 0,
    failedScanCount: 0,
    overdueMaintenanceCount: 0,
    recentOrders: [],
    tripsToday: [],
    pinnedEmployeeNotes: [],
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  )
}

describe('DashboardPage — Aandachtspunten personeel', () => {
  beforeEach(() => {
    navigate.mockClear()
    state.dashboard = baseDashboard()
  })

  it('does not render the panel when there are no pinned notes', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Ritten vandaag' })
    expect(screen.queryByText('Aandachtspunten personeel')).not.toBeInTheDocument()
  })

  it('renders a pinned note and navigates to the employee profile on click', async () => {
    const user = userEvent.setup()
    state.dashboard = {
      ...baseDashboard(),
      pinnedEmployeeNotes: [
        {
          noteId: 'note-1',
          employeeId: 'emp-1',
          employeeName: 'Jan Janssen',
          excerpt: 'Heeft hoogtevrees — nooit op kraanwerk.',
          pinnedAt: '2026-07-28T10:00:00Z',
          authorName: 'Ann HR',
        },
      ],
    }
    renderPage()

    expect(await screen.findByText('Aandachtspunten personeel')).toBeInTheDocument()
    expect(screen.getByText(/Heeft hoogtevrees/)).toBeInTheDocument()
    expect(screen.getByText('Jan Janssen')).toBeInTheDocument()
    // Displays the pin action's date/author, not the note's original write.
    expect(screen.getByText(/Ann HR/)).toBeInTheDocument()

    await user.click(screen.getByText(/Heeft hoogtevrees/))
    expect(navigate).toHaveBeenCalledWith('/employees/emp-1?tab=profiel')
  })
})
