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

// Het dashboard is sinds Wave 1 §16 rolgericht: tegelgroepen renderen alleen voor hun
// audience. De tests sturen de zichtbaarheid via deze permissieset.
const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'u1', firstName: 'Ada', lastName: 'Byron', tenantName: 'Acme', employeeId: null },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const attention = vi.hoisted(() => ({ count: 0 }))
vi.mock('../../../dossiers/api/dossiersApi', () => ({
  getDossierAttentionCount: () => Promise.resolve(attention.count),
}))

/** Broad internal permission set matching the pre-§16 "sees everything" behavior. */
const BROAD_PERMISSIONS = [
  'dashboard.view', 'planning.view', 'orders.view',
  'inventory.view', 'tasks.view_own', 'tasks.view_team', 'tasks.view_all',
]

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
    inventory: null,
    tasks: null,
    unreadInternalMessages: 0,
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

describe('DashboardPage — voorraad- en taaktegels', () => {
  beforeEach(() => {
    navigate.mockClear()
    state.dashboard = baseDashboard()
    auth.permissions = [...BROAD_PERMISSIONS]
    attention.count = 0
  })

  it('hides the Voorraad and Taken sections when the api returns null for them', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Ritten vandaag' })
    expect(screen.queryByRole('heading', { name: 'Voorraad' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Taken' })).not.toBeInTheDocument()
    // The unread-messages tile is permission-free and always present.
    expect(screen.getByText('Nieuwe berichten')).toBeInTheDocument()
  })

  it('renders the Voorraad tiles with alert styling and navigates on click', async () => {
    const user = userEvent.setup()
    state.dashboard = {
      ...baseDashboard(),
      inventory: {
        lowStock: 4,
        criticalStock: 2,
        outOfStock: 1,
        negativeStock: 3,
        openReorderProposals: 5,
        overdueReturns: 6,
      },
    }
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Voorraad' })).toBeInTheDocument()

    const negative = screen.getByRole('button', { name: /Negatieve voorraad/ })
    expect(negative).toHaveClass('db-kpi-alert')
    expect(screen.getByRole('button', { name: /Kritieke voorraad/ })).toHaveClass('db-kpi-alert')
    expect(screen.getByRole('button', { name: /Lage voorraad/ })).not.toHaveClass('db-kpi-alert')

    await user.click(negative)
    expect(navigate).toHaveBeenCalledWith('/inventory?status=NegativeStock')

    await user.click(screen.getByRole('button', { name: /Lage voorraad/ }))
    expect(navigate).toHaveBeenCalledWith('/inventory?status=LowStock')

    await user.click(screen.getByRole('button', { name: /Achterstallige retouren/ }))
    expect(navigate).toHaveBeenCalledWith('/inventory')
  })

  it('renders my task tiles without team tiles when the team counts are null', async () => {
    const user = userEvent.setup()
    state.dashboard = {
      ...baseDashboard(),
      tasks: {
        myOpen: 7,
        myDueToday: 2,
        myOverdue: 1,
        myToAcknowledge: 3,
        teamOpen: null,
        teamOverdue: null,
        teamBlocked: null,
        teamWaitingReview: null,
      },
    }
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Taken' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Mijn open taken/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Open teamtaken/ })).not.toBeInTheDocument()

    const overdue = screen.getByRole('button', { name: /Achterstallig/ })
    expect(overdue).toHaveClass('db-kpi-alert')
    await user.click(overdue)
    expect(navigate).toHaveBeenCalledWith('/tasks?mine=1&overdue=1')

    await user.click(screen.getByRole('button', { name: /Mijn open taken/ }))
    expect(navigate).toHaveBeenCalledWith('/tasks?mine=1')

    await user.click(screen.getByRole('button', { name: /Te bevestigen berichten/ }))
    expect(navigate).toHaveBeenCalledWith('/inbox')
  })

  it('renders the team tiles and their targets when team counts are present', async () => {
    const user = userEvent.setup()
    state.dashboard = {
      ...baseDashboard(),
      tasks: {
        myOpen: 0,
        myDueToday: 0,
        myOverdue: 0,
        myToAcknowledge: 0,
        teamOpen: 9,
        teamOverdue: 4,
        teamBlocked: 2,
        teamWaitingReview: 1,
      },
    }
    renderPage()

    expect(await screen.findByRole('button', { name: /Open teamtaken/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Team achterstallig/ })).toHaveClass('db-kpi-alert')

    await user.click(screen.getByRole('button', { name: /Geblokkeerd/ }))
    expect(navigate).toHaveBeenCalledWith('/tasks?status=Blocked')

    await user.click(screen.getByRole('button', { name: /Wacht op controle/ }))
    expect(navigate).toHaveBeenCalledWith('/tasks?review=1')

    await user.click(screen.getByRole('button', { name: /Team achterstallig/ }))
    expect(navigate).toHaveBeenCalledWith('/tasks?overdue=1')
  })

  it('navigates to the inbox from the unread-messages tile', async () => {
    const user = userEvent.setup()
    state.dashboard = { ...baseDashboard(), unreadInternalMessages: 5 }
    renderPage()

    const tile = await screen.findByRole('button', { name: /Nieuwe berichten/ })
    expect(tile).toHaveTextContent('5')
    await user.click(tile)
    expect(navigate).toHaveBeenCalledWith('/inbox')
  })
})

describe('DashboardPage — rolgerichte tegelgroepen (§16)', () => {
  beforeEach(() => {
    navigate.mockClear()
    state.dashboard = baseDashboard()
    attention.count = 0
  })

  it('dispatcher sees the planning group but NOT the HR group (despite CommonView perms)', async () => {
    // Subset of the seeded dispatcher template relevant to the dashboard, incl. the
    // CommonView employees/absences codes that must NOT surface the HR group.
    auth.permissions = [
      'dashboard.view', 'planning.view', 'orders.view', 'dossiers.view',
      'incidents.view', 'exceptions.view', 'employees.view', 'absences.view',
      'vehicles.view', 'reports.view', 'warehouse.view',
    ]
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Planning & uitvoering' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Personeel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Chauffeurs afwezig vandaag/ })).not.toBeInTheDocument()
    // No invoicing permissions → no backoffice group either.
    expect(screen.queryByRole('heading', { name: 'Facturatie & administratie' })).not.toBeInTheDocument()
  })

  it('renders the fetched dossier attention count and links the tile to /dossiers', async () => {
    const user = userEvent.setup()
    auth.permissions = ['dashboard.view', 'planning.view', 'dossiers.view']
    attention.count = 7

    renderPage()

    const tile = await screen.findByRole('button', { name: /Dossiers met aandacht/ })
    expect(tile).toHaveTextContent('7')
    expect(tile).toHaveClass('db-kpi-alert')
    await user.click(tile)
    expect(navigate).toHaveBeenCalledWith('/dossiers')
  })

  it('does not render the attention tile for users without dossiers.view (no fetch)', async () => {
    auth.permissions = ['dashboard.view', 'planning.view']
    attention.count = 7

    renderPage()

    await screen.findByRole('heading', { name: 'Planning & uitvoering' })
    expect(screen.queryByRole('button', { name: /Dossiers met aandacht/ })).not.toBeInTheDocument()
  })

  it('hides planning and order panels for a backoffice-only permission set', async () => {
    auth.permissions = ['dashboard.view', 'invoices.view']
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Facturatie & administratie' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Planning & uitvoering' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Ritten vandaag' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Recente opdrachten' })).not.toBeInTheDocument()
  })
})

describe('DashboardPage — Aandachtspunten personeel', () => {
  beforeEach(() => {
    navigate.mockClear()
    state.dashboard = baseDashboard()
    auth.permissions = [...BROAD_PERMISSIONS]
    attention.count = 0
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
