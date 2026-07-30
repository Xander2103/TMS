import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EmployeePlanningTab } from '../EmployeePlanningTab'
import type { ScheduleDay, ScheduleEntry, ScheduleGrid } from '../../../employee-planning/types'

const navigateSpy = vi.fn()
vi.mock('react-router-dom', () => ({ useNavigate: () => navigateSpy }))

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['planning.view']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const getScheduleMock = vi.hoisted(() => vi.fn())
vi.mock('../../../employee-planning/api/employeePlanningApi', () => ({ getSchedule: getScheduleMock }))

const EMPLOYEE_ID = 'emp-1'
const VIEW_KEY = 'ts.employeePlanning.view'

function entry(overrides: Partial<ScheduleEntry>): ScheduleEntry {
  return {
    state: 'Confirmed',
    shiftId: 'shift-1',
    absenceId: null,
    tripId: null,
    sourceType: 'Shift',
    label: 'Shift',
    startTime: '08:00:00',
    endTime: '16:00:00',
    shiftType: 'Work',
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

/** Scopes queries to the week grid, excluding the legend (which repeats every state label). */
function weekGrid() {
  return within(screen.getByRole('grid', { name: 'Weekkalender' }))
}

function gridWith(days: ScheduleDay[]): ScheduleGrid {
  return {
    from: days[0]?.date ?? '',
    to: days[days.length - 1]?.date ?? '',
    rows: [
      {
        employeeId: EMPLOYEE_ID,
        employeeName: 'Test Werknemer',
        employeeNumber: 'W-001',
        departmentName: null,
        plannedMinutes: 0,
        days,
      },
    ],
  }
}

describe('EmployeePlanningTab', () => {
  beforeEach(() => {
    navigateSpy.mockClear()
    getScheduleMock.mockReset()
    getScheduleMock.mockResolvedValue(gridWith([]))
    auth.permissions = new Set(['planning.view'])
    window.localStorage.clear()
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 15, 12, 0, 0)) // Saturday 15 August 2026
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('defaults to month view and fetches exactly the padded grid range (leading/trailing weeks included)', async () => {
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()

    await waitFor(() => expect(getScheduleMock).toHaveBeenCalled())
    // August 2026's grid runs Mon 27 July .. Sun 6 September (6 full weeks).
    expect(getScheduleMock).toHaveBeenCalledWith('2026-07-27', '2026-09-06', undefined, EMPLOYEE_ID)
    expect(screen.getByRole('button', { name: 'Maand' }).className).toContain('cal-view-active')
  })

  it('restores the last selected view from localStorage and persists a new choice', async () => {
    window.localStorage.setItem(VIEW_KEY, 'week')
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()

    await waitFor(() => expect(getScheduleMock).toHaveBeenCalled())
    // Week of "today" (Sat 15 Aug 2026) is Mon 10 Aug .. Sun 16 Aug.
    expect(getScheduleMock).toHaveBeenCalledWith('2026-08-10', '2026-08-16', undefined, EMPLOYEE_ID)
    expect(screen.getByRole('button', { name: 'Week' }).className).toContain('cal-view-active')

    const user = userEvent.setup({ delay: null })
    await user.click(screen.getByRole('button', { name: 'Lijst' }))
    expect(window.localStorage.getItem(VIEW_KEY)).toBe('list')
  })

  it('guards a corrupted localStorage value by falling back to the default view', () => {
    window.localStorage.setItem(VIEW_KEY, '{not json')
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()
    expect(screen.getByRole('button', { name: 'Maand' }).className).toContain('cal-view-active')
  })

  it('fetches the 4-week window for list view', async () => {
    window.localStorage.setItem(VIEW_KEY, 'list')
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()

    await waitFor(() => expect(getScheduleMock).toHaveBeenCalled())
    expect(getScheduleMock).toHaveBeenCalledWith('2026-08-10', '2026-09-06', undefined, EMPLOYEE_ID)
  })

  it('keeps requested and approved leave visually and textually distinct', async () => {
    window.localStorage.setItem(VIEW_KEY, 'week')
    getScheduleMock.mockResolvedValue(
      gridWith([
        {
          date: '2026-08-11',
          entries: [
            entry({ state: 'LeaveRequested', sourceType: 'Absence', absenceId: 'abs-1', label: 'Verlof', startTime: null, endTime: null }),
            entry({ state: 'LeaveApproved', sourceType: 'Absence', absenceId: 'abs-2', label: 'Verlof', startTime: null, endTime: null }),
          ],
        },
      ]),
    )
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()

    await waitFor(() => expect(screen.getByRole('grid', { name: 'Weekkalender' })).toBeInTheDocument())
    const requested = await weekGrid().findByText('Verlof aangevraagd')
    const approved = await weekGrid().findByText('Verlof goedgekeurd')
    expect(requested).toBeInTheDocument()
    expect(approved).toBeInTheDocument()
    expect(requested.closest('.schedule-chip')?.className).not.toBe(approved.closest('.schedule-chip')?.className)
  })

  it('renders an all-day absence as a full-width chip', async () => {
    window.localStorage.setItem(VIEW_KEY, 'week')
    getScheduleMock.mockResolvedValue(
      gridWith([
        {
          date: '2026-08-11',
          entries: [entry({ state: 'Sick', sourceType: 'Absence', absenceId: 'abs-3', label: 'Ziek', startTime: null, endTime: null })],
        },
      ]),
    )
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()

    await waitFor(() => expect(screen.getByRole('grid', { name: 'Weekkalender' })).toBeInTheDocument())
    const chip = await weekGrid().findByText('Ziek')
    expect(chip.closest('.schedule-chip')?.className).toContain('schedule-chip-fullwidth')
  })

  it('opens a detail popover on entry click with a link into the Verlof tab, and an approval hint when allowed', async () => {
    auth.permissions = new Set(['planning.view', 'absences.approve'])
    window.localStorage.setItem(VIEW_KEY, 'week')
    getScheduleMock.mockResolvedValue(
      gridWith([
        {
          date: '2026-08-11',
          entries: [
            entry({
              state: 'LeaveRequested',
              sourceType: 'Absence',
              absenceId: 'abs-9',
              label: 'Verlof',
              startTime: null,
              endTime: null,
              statusLabel: 'Familiefeest',
            }),
          ],
        },
      ]),
    )
    render(<EmployeePlanningTab employeeId={EMPLOYEE_ID} />)
    vi.useRealTimers()

    await waitFor(() => expect(screen.getByRole('grid', { name: 'Weekkalender' })).toBeInTheDocument())
    const chip = await weekGrid().findByText('Verlof aangevraagd')
    const user = userEvent.setup({ delay: null })
    await user.click(chip)

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByText('Familiefeest')).toBeInTheDocument()
    expect(screen.getByText(/nog open ter goedkeuring/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Naar verlof/ }))
    expect(navigateSpy).toHaveBeenCalledWith(`/employees/${EMPLOYEE_ID}?tab=verlof&absenceId=abs-9`)
  })
})
