import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { OperationsPage } from '../pages/OperationsPage'

// SEC-M5 (defence in depth behind the route guard, e.g. a role removed mid-session): a refused
// overview renders "Geen toegang" and stops the 30-second polling instead of a subtitle stuck on
// "Laden…", a toast per poll with the raw server text and an "Opnieuw proberen" link.

const api = vi.hoisted(() => ({
  getOperationsOverview: vi.fn(),
  listAlerts: vi.fn(),
  acknowledgeAlert: vi.fn(),
  resolveAlert: vi.fn(),
}))
vi.mock('../api/operationsApi', () => api)

const toast = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false, hasAnyPermission: () => false }),
}))

function renderPage() {
  render(
    <MemoryRouter>
      <OperationsPage />
    </MemoryRouter>,
  )
}

/** Lets the pending promise chain settle (fake timers do not run microtasks by themselves). */
async function flush() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0)
  })
}

describe('OperationsPage load refusals', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders "Geen toegang" on a 403 and stops polling, without toasts or retry', async () => {
    api.getOperationsOverview.mockRejectedValue(new ApiError('Missing permission: operations.view', 403))
    api.listAlerts.mockRejectedValue(new ApiError('Missing permission: operations.view', 403))
    renderPage()
    await flush()

    expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
    expect(screen.queryByText(/Laden/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
    expect(toast.showError).not.toHaveBeenCalled()
    expect(api.listAlerts).not.toHaveBeenCalled()

    for (let poll = 0; poll < 3; poll++) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(30_000)
      })
    }
    expect(api.getOperationsOverview).toHaveBeenCalledTimes(1)
  })

  it('keeps polling after a server error, toasting once per outage and offering retry', async () => {
    api.getOperationsOverview.mockRejectedValue(new ApiError('Internal error', 500))
    api.listAlerts.mockRejectedValue(new ApiError('Internal error', 500))
    renderPage()
    await flush()

    expect(screen.queryByRole('heading', { name: 'Geen toegang' })).not.toBeInTheDocument()
    // The subtitle says the load failed instead of staying on "Laden…" forever.
    expect(screen.getByText(/Overzicht laden mislukt/)).toBeInTheDocument()
    expect(screen.queryByText(/Laden…/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Opnieuw proberen' })).toBeInTheDocument()
    expect(toast.showError).toHaveBeenCalledTimes(1)

    // One act per poll: React batches state updates within a single act.
    for (let poll = 0; poll < 2; poll++) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(30_000)
      })
    }
    expect(api.getOperationsOverview).toHaveBeenCalledTimes(3)
    expect(toast.showError).toHaveBeenCalledTimes(1)
  })

  it('stops polling and drops the retry when the alert list is refused with a 4xx', async () => {
    api.getOperationsOverview.mockResolvedValue({
      generatedAt: '2026-10-18T10:00:00Z',
      counters: { activeTrips: 0, delayedTrips: 0, openExceptions: 0, openCriticalIncidents: 0, missingPods: 0, activeAlerts: 0, criticalAlerts: 0 },
      trips: [],
    })
    api.listAlerts.mockRejectedValue(new ApiError('Bad request', 400))
    renderPage()
    await flush()

    expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
    for (let poll = 0; poll < 2; poll++) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(30_000)
      })
    }
    expect(api.getOperationsOverview).toHaveBeenCalledTimes(1)
  })
})
