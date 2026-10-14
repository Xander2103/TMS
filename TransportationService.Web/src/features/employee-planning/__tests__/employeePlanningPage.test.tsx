import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import * as api from '../api/employeePlanningApi'
import { EmployeePlanningPage } from '../pages/EmployeePlanningPage'
import type { PlanningEmployeeOption, PlanningFilterOptions, ScheduleEntry, ScheduleGrid, ScheduleRow } from '../types'

/**
 * Personeelsplanning scalability wave: the page is driven by ONE selection in the URL. These
 * tests exercise the filter semantics at the UI level, the independence of the global employee
 * search from the active filters, URL restore (F5 / Back / Forward), stale-response protection,
 * the empty states, the export call and the list view sharing the same selection.
 */
const auth = vi.hoisted(() => ({
  permissions: ['employee_planning.view', 'employee_planning.manage', 'employees.view'],
  user: { id: 'user-1', employeeId: 'emp-els' },
}))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: auth.user,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const DEPT_TRANSPORT = 'aaaaaaaa-0000-0000-0000-000000000001'
const DEPT_DISPATCH = 'aaaaaaaa-0000-0000-0000-000000000002'
const FN_CHAUFFEUR = 'bbbbbbbb-0000-0000-0000-000000000001'
const FN_DISPATCHER = 'bbbbbbbb-0000-0000-0000-000000000002'
const JAN = 'cccccccc-0000-0000-0000-000000000001'
const MARC = 'cccccccc-0000-0000-0000-000000000002'
const ELS = 'cccccccc-0000-0000-0000-000000000003'
const WEEK = '2027-03-01'

const OPTIONS: PlanningFilterOptions = {
  departments: [
    { id: DEPT_TRANSPORT, name: 'Transport' },
    { id: DEPT_DISPATCH, name: 'Dispatch' },
  ],
  jobFunctions: [
    { id: FN_CHAUFFEUR, name: 'Chauffeur' },
    { id: FN_DISPATCHER, name: 'Dispatcher' },
  ],
  contractTypes: [],
  myDepartmentId: DEPT_DISPATCH,
  hasDrivers: true,
}

const DIRECTORY: PlanningEmployeeOption[] = [
  { id: JAN, employeeNumber: 'E-001', firstName: 'Jan', lastName: 'Peeters', departmentName: 'Transport', functionNames: ['Chauffeur'], isActive: true },
  { id: MARC, employeeNumber: 'E-002', firstName: 'Marc', lastName: 'Janssens', departmentName: 'Dispatch', functionNames: ['Dispatcher'], isActive: true },
  { id: ELS, employeeNumber: 'E-003', firstName: 'Els', lastName: 'Claes', departmentName: 'Dispatch', functionNames: ['Planner'], isActive: true },
]

function shift(date: string): ScheduleEntry {
  return {
    state: 'Planned', shiftId: `shift-${date}`, absenceId: null, tripId: null, sourceType: 'Shift', label: 'Antwerpen',
    startTime: '06:00:00', endTime: '14:00:00', shiftType: 'Work', workLocation: 'Antwerpen', vehicleSummary: null,
    statusLabel: 'Gepland', conflictSeverity: null, conflictNotes: null, colour: null, noteId: null,
  }
}

function row(id: string, name: string, department: string, fn: string, extra: Partial<ScheduleRow> = {}): ScheduleRow {
  const days = Array.from({ length: 7 }, (_, i) => {
    const date = `2027-03-${String(1 + i).padStart(2, '0')}`
    return { date, entries: i === 0 ? [shift(date)] : [] }
  })
  return { employeeId: id, employeeName: name, employeeNumber: id.slice(-3), departmentName: department, plannedMinutes: 450, days, functionNames: [fn], contractTypeName: 'Vast', isActive: true, matchesFilters: true, ...extra }
}

const ALL_ROWS = [row(ELS, 'Els Claes', 'Dispatch', 'Planner'), row(MARC, 'Marc Janssens', 'Dispatch', 'Dispatcher'), row(JAN, 'Jan Peeters', 'Transport', 'Chauffeur')]

/** A fake backend applying the same selection semantics as the real one. */
function fakeGrid(selection: Parameters<typeof api.getScheduleForSelection>[0]): ScheduleGrid {
  const hasGroup = selection.departmentIds.length > 0 || selection.jobFunctionIds.length > 0 || selection.driversOnly
  const explicit = selection.employeeIds
  const group = ALL_ROWS.filter((r) => {
    const deptOk = selection.departmentIds.length === 0 || selection.departmentIds.includes(r.departmentName === 'Transport' ? DEPT_TRANSPORT : DEPT_DISPATCH)
    const fnOk = selection.jobFunctionIds.length === 0 || selection.jobFunctionIds.includes(r.functionNames![0] === 'Chauffeur' ? FN_CHAUFFEUR : r.functionNames![0] === 'Dispatcher' ? FN_DISPATCHER : 'none')
    const driverOk = !selection.driversOnly || r.functionNames![0] === 'Chauffeur'
    return deptOk && fnOk && driverOk
  })
  let rows: ScheduleRow[]
  if (explicit.length > 0 && !hasGroup) rows = ALL_ROWS.filter((r) => explicit.includes(r.employeeId))
  else if (explicit.length > 0) {
    rows = ALL_ROWS.filter((r) => group.includes(r) || explicit.includes(r.employeeId)).map((r) => ({ ...r, matchesFilters: group.includes(r) }))
  } else rows = group
  return { from: selection.weekStart, to: selection.weekStart, rows, totalInScope: ALL_ROWS.length }
}

function LocationProbe() {
  const location = useLocation()
  const navigate = useNavigate()
  return (
    <>
      <span data-testid="url">{`${location.pathname}${location.search}`}</span>
      <button type="button" onClick={() => navigate(-1)}>browser back</button>
    </>
  )
}

/** Queries scoped to the grid/list table, so chips with the same names do not interfere. */
function table() {
  return within(screen.getByRole('table'))
}

function renderPage(initialEntry = `/employee-planning?week=${WEEK}`) {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/employee-planning" element={<><EmployeePlanningPage /><LocationProbe /></>} />
        <Route path="/employees/:id" element={<span>employee page</span>} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
  toast.showError.mockReset()
  vi.spyOn(api, 'getScheduleForSelection').mockImplementation(async (selection) => fakeGrid(selection))
  vi.spyOn(api, 'getPlanningFilterOptions').mockResolvedValue(OPTIONS)
  vi.spyOn(api, 'searchPlanningEmployees').mockImplementation(async (search) =>
    DIRECTORY.filter((e) => `${e.firstName} ${e.lastName} ${e.employeeNumber}`.toLowerCase().includes(search.toLowerCase())),
  )
  vi.spyOn(api, 'downloadScheduleExcel').mockResolvedValue()
})

async function openFilters(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /^Filters/ }))
  return screen.getByRole('dialog', { name: 'Filters' })
}

describe('EmployeePlanningPage', () => {
  it('loads everyone by default and shows the result count', async () => {
    renderPage()
    expect(await screen.findByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByText('3 medewerkers')).toBeInTheDocument()
    expect(api.getScheduleForSelection).toHaveBeenCalledWith(
      expect.objectContaining({ weekStart: WEEK, weeks: 1, departmentIds: [], employeeIds: [] }),
      expect.anything(),
    )
  })

  it('applies several functions at once (OR) through the searchable filter and shows chips', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Jan Peeters')

    const dialog = await openFilters(user)
    const functions = within(dialog).getByRole('combobox', { name: 'Functies' })
    await user.click(functions)
    await user.click(within(dialog).getByRole('option', { name: 'Chauffeur' }))
    await user.click(within(dialog).getByRole('option', { name: 'Dispatcher' }))

    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent(`functions=${FN_CHAUFFEUR}%2C${FN_DISPATCHER}`))
    await waitFor(() => expect(screen.queryByText('Els Claes')).toBeNull())
    expect(screen.getByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByText('Marc Janssens')).toBeInTheDocument()
    expect(screen.getByText('2 van 3 medewerkers')).toBeInTheDocument()

    const chips = screen.getByRole('group', { name: 'Actieve selectie' })
    expect(within(chips).getByText('Chauffeur')).toBeInTheDocument()
    expect(within(chips).getByText('Dispatcher')).toBeInTheDocument()

    // Removing one chip narrows further; "Filters wissen" restores everyone.
    await user.click(within(chips).getByRole('button', { name: 'Chauffeur verwijderen' }))
    await waitFor(() => expect(screen.queryByText('Jan Peeters')).toBeNull())
    await user.click(screen.getByRole('button', { name: 'Filters wissen' }))
    await waitFor(() => expect(screen.getByText('3 medewerkers')).toBeInTheDocument())
    expect(screen.getByTestId('url')).toHaveTextContent(`/employee-planning?week=${WEEK}`)
  })

  it('department AND function narrow together; the global search still finds someone outside and pins them', async () => {
    const user = userEvent.setup()
    renderPage(`/employee-planning?week=${WEEK}&departments=${DEPT_DISPATCH}&functions=${FN_DISPATCHER}`)
    expect(await screen.findByText('Marc Janssens')).toBeInTheDocument()
    expect(screen.queryByText('Jan Peeters')).toBeNull()
    expect(screen.queryByText('Els Claes')).toBeNull()

    await user.click(screen.getByRole('button', { name: /^Medewerkers/ }))
    const picker = screen.getByRole('dialog', { name: 'Medewerkers kiezen' })
    await user.type(within(picker).getByRole('combobox', { name: 'Medewerkers' }), 'Jan')
    // The search endpoint receives the term only — never the department/function filters.
    await waitFor(() => expect(api.searchPlanningEmployees).toHaveBeenLastCalledWith('Jan', expect.anything()))
    await user.click(await within(picker).findByRole('option', { name: /Jan Peeters/ }))

    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent(`employees=${JAN}`))
    const grid = await screen.findByRole('table')
    expect(within(grid).getByText('Jan Peeters')).toBeInTheDocument()
    expect(within(grid).getByText('Marc Janssens')).toBeInTheDocument()
    expect(within(grid).getByText('Buiten huidige filters')).toBeInTheDocument()
    expect(screen.getByText('2 van 3 medewerkers')).toBeInTheDocument()
    const chips = screen.getByRole('group', { name: 'Actieve selectie' })
    expect(within(chips).getByRole('button', { name: 'Jan Peeters verwijderen' })).toBeInTheDocument()

    // Removing the pinned employee drops him again.
    await user.click(within(chips).getByRole('button', { name: 'Jan Peeters verwijderen' }))
    await waitFor(() => expect(within(screen.getByRole('table')).queryByText('Jan Peeters')).toBeNull())
  })

  it('two explicit employees without group filters are the whole selection', async () => {
    renderPage(`/employee-planning?week=${WEEK}&employees=${JAN},${ELS}`)
    await screen.findByRole('table')
    expect(table().getByText('Jan Peeters')).toBeInTheDocument()
    expect(table().getByText('Els Claes')).toBeInTheDocument()
    expect(table().queryByText('Marc Janssens')).toBeNull()
    expect(screen.queryByText('Buiten huidige filters')).toBeNull()
    expect(screen.getByText('2 van 3 medewerkers')).toBeInTheDocument()
  })

  it('restores the selection from the URL (F5) and keeps it across week navigation', async () => {
    const user = userEvent.setup()
    renderPage(`/employee-planning?week=${WEEK}&departments=${DEPT_TRANSPORT}&view=list`)
    expect(await screen.findByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Lijstweergave' })).toBeChecked()
    const chips = screen.getByRole('group', { name: 'Actieve selectie' })
    expect(within(chips).getByText('Transport')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Volgende week' }))
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent(`week=2027-03-08&departments=${DEPT_TRANSPORT}&view=list`))
    expect(api.getScheduleForSelection).toHaveBeenLastCalledWith(
      expect.objectContaining({ weekStart: '2027-03-08', departmentIds: [DEPT_TRANSPORT] }),
      expect.anything(),
    )
  })

  it('Back restores the previous selection (history entries per change)', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Jan Peeters')
    const dialog = await openFilters(user)
    await user.click(within(dialog).getByRole('button', { name: 'Alle chauffeurs' }))
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('drivers=1'))
    await waitFor(() => expect(screen.queryByText('Els Claes')).toBeNull())

    await user.click(screen.getByRole('button', { name: 'browser back' }))
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent(`/employee-planning?week=${WEEK}`))
    expect(await screen.findByText('Els Claes')).toBeInTheDocument()
  })

  it('a late response of an older selection never overwrites the newer one', async () => {
    let resolveSlow: ((grid: ScheduleGrid) => void) | null = null
    vi.spyOn(api, 'getScheduleForSelection').mockImplementation((selection, signal) => {
      if (selection.driversOnly) {
        return new Promise<ScheduleGrid>((resolve, reject) => {
          resolveSlow = resolve
          signal?.addEventListener('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })))
        })
      }
      return Promise.resolve(fakeGrid(selection))
    })
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Jan Peeters')

    // Request A (slow): drivers only. Request B: back to everyone — resolves immediately.
    const dialog = await openFilters(user)
    await user.click(within(dialog).getByRole('button', { name: 'Alle chauffeurs' }))
    expect(screen.getByRole('status')).toHaveTextContent('Bijwerken…')
    // Stale data stays visible meanwhile — no flash to empty.
    expect(screen.getByText('Els Claes')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Alle chauffeurs' }))
    await waitFor(() => expect(screen.queryByRole('status')).toBeNull())

    // A answers late with only Jan: it must be ignored (and was aborted anyway).
    act(() => {
      resolveSlow?.({ from: WEEK, to: WEEK, rows: [ALL_ROWS[2]], totalInScope: 3 })
    })
    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(screen.getByText('Els Claes')).toBeInTheDocument()
    expect(screen.getByText('3 medewerkers')).toBeInTheDocument()
  })

  it('distinguishes the empty states and offers to clear the filters', async () => {
    vi.spyOn(api, 'getScheduleForSelection').mockResolvedValue({ from: WEEK, to: WEEK, rows: [], totalInScope: 3 })
    const user = userEvent.setup()
    renderPage(`/employee-planning?week=${WEEK}&departments=${DEPT_TRANSPORT}`)
    const empty = await screen.findByText('Geen medewerkers voldoen aan de huidige filters.')
    await user.click(within(empty.parentElement!).getByRole('button', { name: 'Filters wissen' }))
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent(`/employee-planning?week=${WEEK}`))
    expect(await screen.findByText('Geen medewerkers gevonden voor deze selectie.')).toBeInTheDocument()
  })

  it('shows an error state with retry instead of an empty state when the API fails', async () => {
    vi.spyOn(api, 'getScheduleForSelection').mockRejectedValueOnce(new Error('boom')).mockImplementation(async (s) => fakeGrid(s))
    const user = userEvent.setup()
    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent('De personeelsplanning kon niet worden geladen.')
    expect(screen.queryByText('Geen medewerkers gevonden voor deze selectie.')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))
    expect(await screen.findByText('Jan Peeters')).toBeInTheDocument()
  })

  it('exports exactly the current selection', async () => {
    const user = userEvent.setup()
    renderPage(`/employee-planning?week=${WEEK}&departments=${DEPT_DISPATCH}&employees=${JAN}`)
    await screen.findByRole('table')
    await user.click(screen.getByRole('button', { name: /Exporteren/ }))
    await user.click(screen.getByRole('menuitem', { name: /Excel/ }))
    await waitFor(() =>
      expect(api.downloadScheduleExcel).toHaveBeenCalledWith(
        expect.objectContaining({ weekStart: WEEK, weeks: 1, departmentIds: [DEPT_DISPATCH], employeeIds: [JAN] }),
      ),
    )
  })

  it('the list view uses the same selection and query without refetching', async () => {
    const user = userEvent.setup()
    renderPage(`/employee-planning?week=${WEEK}&departments=${DEPT_TRANSPORT}`)
    await screen.findByText('Jan Peeters')
    const calls = vi.mocked(api.getScheduleForSelection).mock.calls.length
    await user.click(screen.getByRole('checkbox', { name: 'Lijstweergave' }))
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('view=list'))
    expect(screen.getByRole('columnheader', { name: 'Datum' })).toBeInTheDocument()
    expect(screen.getByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.queryByText('Marc Janssens')).toBeNull()
    expect(vi.mocked(api.getScheduleForSelection).mock.calls.length).toBe(calls)
  })

  it('the quick search only narrows the loaded rows and reports what is shown', async () => {
    renderPage()
    await screen.findByText('Jan Peeters')
    fireEvent.change(screen.getByRole('searchbox', { name: 'Zoeken in de getoonde medewerkers' }), { target: { value: 'marc' } })
    expect(screen.queryByText('Jan Peeters')).toBeNull()
    expect(screen.getByText('Marc Janssens')).toBeInTheDocument()
    expect(screen.getByText(/1 getoond/)).toBeInTheDocument()
    expect(vi.mocked(api.getScheduleForSelection).mock.calls.length).toBe(1)
  })

  it('seeds a plain URL with the user\'s last used filters, but never overrides a shared link', async () => {
    window.localStorage.setItem('ts.employeePlanning.filters:user-1', JSON.stringify({ departmentIds: [DEPT_TRANSPORT] }))
    const { unmount } = renderPage(`/employee-planning?week=${WEEK}`)
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent(`departments=${DEPT_TRANSPORT}`))
    unmount()

    renderPage(`/employee-planning?week=${WEEK}&employees=${MARC}`)
    await screen.findByRole('table')
    expect(table().getByText('Marc Janssens')).toBeInTheDocument()
    expect(screen.getByTestId('url')).not.toHaveTextContent('departments=')
  })

  it('B49: the density toggle switches the grid to compact rows without dropping chips or totals', async () => {
    const user = userEvent.setup()
    renderPage()
    expect(await screen.findByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByRole('table')).not.toHaveClass('ep-grid-compact')

    const density = screen.getByRole('group', { name: 'Dichtheid' })
    await user.click(within(density).getByRole('button', { name: 'Compact' }))

    expect(screen.getByRole('table')).toHaveClass('ep-grid-compact')
    expect(within(density).getByRole('button', { name: 'Compact' })).toHaveAttribute('aria-pressed', 'true')
    // Shifts, planned totals and the add-shift action stay available in compact.
    expect(table().getAllByText('06:00–14:00').length).toBeGreaterThan(0)
    expect(table().getAllByText(/7u30 gepland|7:30 gepland|gepland/).length).toBe(3)
    expect(table().getAllByRole('button', { name: '+ shift' }).length).toBeGreaterThan(0)
    expect(window.localStorage.getItem('ts.list.density.employee-planning')).toBe('compact')

    await user.click(within(density).getByRole('button', { name: 'Normaal' }))
    expect(screen.getByRole('table')).not.toHaveClass('ep-grid-compact')
  })

  it('B49: hides the density toggle in the list view (it only applies to the grid)', async () => {
    renderPage(`/employee-planning?week=${WEEK}&view=list`)
    expect(await screen.findByRole('columnheader', { name: 'Datum' })).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Dichtheid' })).toBeNull()
  })
})
