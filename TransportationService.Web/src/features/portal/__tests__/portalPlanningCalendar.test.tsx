import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { PortalPlanningPage } from '../pages/PortalPlanningPage'
import type { ScheduleDay } from '../../employee-planning/types'
import { toIsoDate } from '../../../components/calendar/dateUtils'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => false }),
}))

const planning = vi.hoisted(() => ({ value: [] as ScheduleDay[] }))

vi.mock('../../../api/apiClient', async () => {
  const actual = await vi.importActual<typeof import('../../../api/apiClient')>('../../../api/apiClient')
  return {
    ...actual,
    apiClient: {
      ...actual.apiClient,
      getJson: vi.fn(() => Promise.resolve(planning.value)),
    },
  }
})

const createNoteSpy = vi.hoisted(() => vi.fn(() => Promise.resolve({})))

vi.mock('../api/calendarNotesApi', async () => {
  const actual = await vi.importActual<typeof import('../api/calendarNotesApi')>('../api/calendarNotesApi')
  return {
    ...actual,
    createCalendarNote: createNoteSpy,
    updateCalendarNote: vi.fn(),
    deleteCalendarNote: vi.fn(),
  }
})

function entry(overrides: Partial<ScheduleDay['entries'][number]>): ScheduleDay['entries'][number] {
  return {
    state: 'Trip',
    shiftId: null,
    absenceId: null,
    tripId: null,
    sourceType: 'Trip',
    label: 'RIT-0001',
    startTime: null,
    endTime: null,
    shiftType: null,
    workLocation: null,
    vehicleSummary: null,
    statusLabel: null,
    conflictSeverity: null,
    conflictNotes: null,
    colour: null,
    noteId: null,
    ...overrides,
  }
}

// Must match the app's own local-date logic (`dateUtils.toIsoDate`), not UTC: the week grid is
// anchored on `mondayOf(new Date())` (local calendar day), so a UTC-based "today" can land in
// the wrong week (or even the wrong month) whenever local and UTC dates briefly disagree near
// midnight UTC — which happens every day in timezones ahead of UTC.
function isoToday(): string {
  return toIsoDate(new Date())
}

describe('PortalPlanningPage calendar colours + notes', () => {
  beforeEach(() => {
    createNoteSpy.mockClear()
    planning.value = [
      {
        date: isoToday(),
        entries: [
          entry({}),
          entry({
            state: 'LeaveApproved',
            sourceType: 'Absence',
            label: 'Verlof',
            tripId: null,
            colour: '#9333ea',
          }),
          entry({
            state: 'Note',
            sourceType: 'Note',
            label: 'Tandarts',
            colour: '#16a34a',
            noteId: 'note-1',
          }),
        ],
      },
    ]
  })

  it('renders month view with coloured chips that keep their labels', async () => {
    render(
      <MemoryRouter>
        <PortalPlanningPage />
      </MemoryRouter>,
    )
    await waitFor(() => expect(screen.getByRole('button', { name: 'Maand' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Maand' }))

    // Month cells now use the chip component: labels stay, colours apply inline.
    await waitFor(() => expect(screen.getAllByText('RIT-0001').length).toBeGreaterThan(0))
    const leaveChip = screen.getAllByText('Verlof goedgekeurd')[0].closest('.schedule-chip') as HTMLElement
    expect(leaveChip.style.borderColor).toBe('rgb(147, 51, 234)')
  })

  it('shows the personal note with its chosen colour in the week view', async () => {
    render(
      <MemoryRouter>
        <PortalPlanningPage />
      </MemoryRouter>,
    )
    await waitFor(() => expect(screen.getAllByText('Tandarts').length).toBeGreaterThan(0))
    const noteChip = screen.getAllByText('Tandarts')[0].closest('.schedule-chip') as HTMLElement
    expect(noteChip.classList.contains('schedule-chip-note')).toBe(true)
    expect(noteChip.style.borderColor).toBe('rgb(22, 163, 74)')
  })

  it('creates a personal note through the palette dialog', async () => {
    render(
      <MemoryRouter>
        <PortalPlanningPage />
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('button', { name: '+ Notitie' }))
    await userEvent.type(screen.getByLabelText(/Titel/), 'Auto garage')
    await userEvent.click(screen.getByRole('radio', { name: /Oranje/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(createNoteSpy).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Auto garage', colour: '#ea580c', allDay: true }),
      ),
    )
  })
})
