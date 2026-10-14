import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useListDensity } from '../../../components/ui/listDensity'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getPlanningFilterOptions, getShift } from '../api/employeePlanningApi'
import { ActiveFilterChips } from '../components/ActiveFilterChips'
import { CopyWeekDialog } from '../components/CopyWeekDialog'
import { PlanningGrid } from '../components/PlanningGrid'
import { PlanningListView } from '../components/PlanningListView'
import { PlanningToolbar } from '../components/PlanningToolbar'
import { ScheduleLegend } from '../components/ScheduleChip'
import { ShiftDialog, type ShiftDialogState } from '../components/ShiftDialog'
import { usePlanningSchedule } from '../hooks/usePlanningSchedule'
import {
  hasAnyFilter,
  readLastUsedFilters,
  selectionFromSearchParams,
  selectionToSearchParams,
  urlHasFilters,
  writeLastUsedFilters,
  type PlanningSelection,
} from '../planningFilters'
import { type ScheduleEntry, type ScheduleRow, toIsoDate, type PlanningFilterOptions } from '../types'
import '../components/employee-planning.css'

/**
 * Personnel planning: employees × days with chips per state, a shift dialog and week copy.
 *
 * The SELECTION (period, group filters, status scope, explicit employees, view) lives in the
 * URL — F5, Back/Forward, bookmarks and shared links all restore it. The grid, the list view,
 * "week kopiëren" and the Excel export all read that one selection and the backend resolves it
 * with one shared employee filter, so no two features can disagree on who is on screen.
 * Filtering happens server-side; the toolbar's quick search only narrows the loaded rows.
 */
export function EmployeePlanningPage() {
  const { t } = useLocale()
  const { showError } = useToast()
  const { hasPermission, user } = useAuth()
  const navigate = useNavigate()
  const canManage = hasPermission('employee_planning.manage')
  const canViewTrips = hasPermission('planning.view')
  const canViewEmployees = hasPermission('employees.view')
  const canOverrideConflicts = hasPermission('employee_planning.conflict_override')

  const [searchParams, setSearchParams] = useSearchParams()
  const selection = useMemo(() => selectionFromSearchParams(searchParams), [searchParams])

  // Last-used filters (per user) seed a URL that carries no filter at all; the URL always wins.
  const seededRef = useRef(false)
  useEffect(() => {
    if (seededRef.current || !user) return
    seededRef.current = true
    if (urlHasFilters(searchParams)) return
    const stored = readLastUsedFilters(user.id)
    if (stored && hasAnyFilter({ ...selection, ...stored })) {
      setSearchParams(selectionToSearchParams({ ...selection, ...stored }), { replace: true })
    }
  }, [user, searchParams, selection, setSearchParams])

  useEffect(() => {
    if (!seededRef.current) return
    writeLastUsedFilters(user?.id ?? null, selection)
  }, [user, selection])

  const updateSelection = useCallback(
    (patch: Partial<PlanningSelection>) => {
      setSearchParams(selectionToSearchParams({ ...selection, ...patch }))
    },
    [selection, setSearchParams],
  )

  const onLoadError = useCallback(() => showError(t('employeePlanning.empty.loadError')), [showError, t])
  const { grid, isFetching, error, reload } = usePlanningSchedule(selection, onLoadError)

  const [filterOptions, setFilterOptions] = useState<PlanningFilterOptions | null>(null)
  const [filterOptionsError, setFilterOptionsError] = useState(false)
  useEffect(() => {
    const controller = new AbortController()
    getPlanningFilterOptions(controller.signal).then(
      (options) => setFilterOptions(options),
      (err: unknown) => {
        if ((err as { name?: string } | null)?.name !== 'AbortError') setFilterOptionsError(true)
      },
    )
    return () => controller.abort()
  }, [])

  // Display names for explicit picks: the picker contributes what it searched, the grid the rest.
  const [pickedNames, setPickedNames] = useState<Record<string, string>>({})
  const employeeNames = useMemo(() => {
    const names: Record<string, string> = { ...pickedNames }
    for (const row of grid?.rows ?? []) names[row.employeeId] = row.employeeName
    return names
  }, [pickedNames, grid])

  const [quickSearch, setQuickSearch] = useState('')
  const [density, setDensity] = useListDensity('employee-planning')
  const [dialog, setDialog] = useState<ShiftDialogState | null>(null)
  const [copyOpen, setCopyOpen] = useState(false)

  const days = useMemo(() => {
    const result: string[] = []
    const start = new Date(`${selection.weekStart}T00:00:00`).getTime()
    for (let i = 0; i < selection.weeks * 7; i += 1) result.push(toIsoDate(new Date(start + i * 86_400_000)))
    return result
  }, [selection.weekStart, selection.weeks])
  const today = toIsoDate(new Date())

  const serverRows = useMemo(() => grid?.rows ?? [], [grid])
  const quick = quickSearch.trim().toLowerCase()
  const rows = useMemo(
    () => (quick ? serverRows.filter((row) => `${row.employeeName} ${row.employeeNumber}`.toLowerCase().includes(quick)) : serverRows),
    [serverRows, quick],
  )

  async function openEdit(shiftId: string, employeeName: string) {
    try {
      const shift = await getShift(shiftId)
      setDialog({ mode: 'edit', employeeId: shift.employeeId, employeeName, shift, date: shift.date })
    } catch {
      showError(t('employeePlanning.page.shiftLoadFailed'))
    }
  }

  /**
   * Click behaviour per source: trip chips deep-link to the trip, absence chips to the
   * employee's absence tab, shift chips open the dialog (read-only without manage rights).
   */
  const chipAction = useCallback(
    (entry: ScheduleEntry, row: ScheduleRow): (() => void) | undefined => {
      if (entry.sourceType === 'Trip') {
        return canViewTrips && entry.tripId ? () => navigate(`/planning/${entry.tripId}`) : undefined
      }
      if (entry.sourceType === 'Absence') {
        return canViewEmployees && entry.absenceId
          ? () => navigate(`/employees/${row.employeeId}?tab=verlof&absenceId=${entry.absenceId}`)
          : undefined
      }
      if (entry.shiftId) {
        return () => void openEdit(entry.shiftId!, row.employeeName)
      }
      return undefined
    },
    // openEdit only closes over stable toast/translate helpers.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [canViewTrips, canViewEmployees, navigate],
  )

  const filtered = hasAnyFilter(selection)
  const showEmpty = grid !== null && !isFetching && rows.length === 0
  const gridMaxHeight = 'calc(100vh - 330px)'

  return (
    <div className="ep-page">
      <PageHeader title={t('employeePlanning.page.title')} subtitle={t('employeePlanning.page.subtitle')} />

      <PlanningToolbar
        selection={selection}
        onChange={updateSelection}
        filterOptions={filterOptions}
        filterOptionsError={filterOptionsError}
        employeeNames={employeeNames}
        onEmployeesChange={(employeeIds, names) => {
          setPickedNames((current) => ({ ...current, ...names }))
          updateSelection({ employeeIds })
        }}
        quickSearch={quickSearch}
        onQuickSearch={setQuickSearch}
        canManage={canManage}
        onCopyWeek={() => setCopyOpen(true)}
        density={density}
        onDensityChange={setDensity}
      />

      <div className="ep-status-row">
        <ActiveFilterChips
          selection={selection}
          options={filterOptions}
          employeeNames={employeeNames}
          shownCount={serverRows.length}
          quickFilteredCount={quick ? rows.length : null}
          totalInScope={grid?.totalInScope ?? null}
          onChange={updateSelection}
        />
        {isFetching && <span className="ep-updating" role="status">{grid ? t('employeePlanning.empty.updating') : t('employeePlanning.page.loading')}</span>}
      </div>

      <ScheduleLegend />

      {error && !grid && (
        <div className="ep-empty ep-error" role="alert">
          <p>{t('employeePlanning.empty.loadError')}</p>
          <Button variant="secondary" onClick={reload}>{t('employeePlanning.empty.retry')}</Button>
        </div>
      )}

      {showEmpty && (
        <div className="ep-empty">
          <p>
            {quick
              ? t('employeePlanning.empty.noneForQuickSearch', { query: quickSearch.trim() })
              : filtered
                ? t('employeePlanning.empty.noneForFilters')
                : t('employeePlanning.empty.noneForSelection')}
          </p>
          {quick ? (
            <Button variant="secondary" onClick={() => setQuickSearch('')}>{t('ui.actions.close')}</Button>
          ) : filtered ? (
            <Button variant="secondary" onClick={() => updateSelection({ departmentIds: [], jobFunctionIds: [], contractTypeIds: [], statuses: [], driversOnly: false, employeeIds: [] })}>
              {t('employeePlanning.chips.clearFilters')}
            </Button>
          ) : null}
        </div>
      )}

      {grid && rows.length > 0 && !selection.listView && (
        <PlanningGrid
          rows={rows}
          days={days}
          today={today}
          canManage={canManage}
          canViewEmployees={canViewEmployees}
          chipAction={chipAction}
          onAddShift={(row, date) => setDialog({ mode: 'create', employeeId: row.employeeId, employeeName: row.employeeName, date })}
          maxHeight={gridMaxHeight}
          density={density}
        />
      )}

      {grid && rows.length > 0 && selection.listView && (
        <PlanningListView rows={rows} canViewEmployees={canViewEmployees} chipAction={chipAction} maxHeight={gridMaxHeight} />
      )}

      {dialog && (
        <ShiftDialog
          key={dialog.shift?.id ?? `create-${dialog.employeeId}-${dialog.date}`}
          dialog={dialog}
          canManage={canManage}
          canOverrideConflicts={canOverrideConflicts}
          onClose={() => setDialog(null)}
          onChanged={reload}
        />
      )}

      {copyOpen && (
        <CopyWeekDialog
          selection={selection}
          employeeCount={serverRows.length}
          onClose={() => setCopyOpen(false)}
          onCopied={(target) => {
            setCopyOpen(false)
            updateSelection({ weekStart: target })
          }}
        />
      )}
    </div>
  )
}
