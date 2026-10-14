import { ChevronLeft, ChevronRight, Copy, Search } from 'lucide-react'
import { Button } from '../../../components/ui/Button'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import type { ListDensity } from '../../../components/ui/listDensity'
import { useLocale } from '../../../i18n/localeContext'
import { shiftWeekStart, type PlanningGroupFilters, type PlanningSelection, type PlanningWeeks } from '../planningFilters'
import { mondayOf, toIsoDate } from '../types'
import type { PlanningFilterOptions } from '../types'
import { EmployeePicker } from './EmployeePicker'
import { ExportMenu } from './ExportMenu'
import { FiltersPopover } from './FiltersPopover'

interface PlanningToolbarProps {
  selection: PlanningSelection
  onChange: (patch: Partial<PlanningSelection>) => void
  filterOptions: PlanningFilterOptions | null
  filterOptionsError: boolean
  employeeNames: Record<string, string>
  onEmployeesChange: (employeeIds: string[], names: Record<string, string>) => void
  quickSearch: string
  onQuickSearch: (value: string) => void
  canManage: boolean
  onCopyWeek: () => void
  /** B49: grid row density (the list view has its own layout and ignores it). */
  density: ListDensity
  onDensityChange: (value: ListDensity) => void
}

/**
 * One compact row: period navigation, the employee picker, the filters, the quick search in
 * the loaded results and the view toggle; export and week copy on the right. Wraps on narrow
 * screens instead of stacking into a form.
 */
export function PlanningToolbar({
  selection,
  onChange,
  filterOptions,
  filterOptionsError,
  employeeNames,
  onEmployeesChange,
  quickSearch,
  onQuickSearch,
  canManage,
  onCopyWeek,
  density,
  onDensityChange,
}: PlanningToolbarProps) {
  const { t } = useLocale()

  function changeGroup(patch: Partial<PlanningGroupFilters>) {
    onChange(patch)
  }

  return (
    <div className="ep-toolbar" role="toolbar" aria-label={t('employeePlanning.page.title')}>
      <span className="ep-week-nav">
        <button type="button" onClick={() => onChange({ weekStart: shiftWeekStart(selection.weekStart, -1) })} aria-label={t('employeePlanning.page.prevWeek')}>
          <ChevronLeft size={16} aria-hidden="true" />
        </button>
        <input
          type="date"
          value={selection.weekStart}
          onChange={(e) => e.target.value && onChange({ weekStart: toIsoDate(mondayOf(new Date(`${e.target.value}T00:00:00`))) })}
          aria-label={t('employeePlanning.page.weekStart')}
        />
        <button type="button" onClick={() => onChange({ weekStart: shiftWeekStart(selection.weekStart, 1) })} aria-label={t('employeePlanning.page.nextWeek')}>
          <ChevronRight size={16} aria-hidden="true" />
        </button>
      </span>
      <label className="ep-toolbar-field">
        <span className="visually-hidden">{t('employeePlanning.page.period')}</span>
        <select value={selection.weeks} onChange={(e) => onChange({ weeks: Number(e.target.value) as PlanningWeeks })} aria-label={t('employeePlanning.page.period')}>
          <option value={1}>{t('employeePlanning.page.weeks', { count: 1 })}</option>
          <option value={2}>{t('employeePlanning.page.weeks', { count: 2 })}</option>
          <option value={4}>{t('employeePlanning.page.weeks', { count: 4 })}</option>
        </select>
      </label>
      <EmployeePicker employeeIds={selection.employeeIds} employeeNames={employeeNames} onChange={onEmployeesChange} />
      <FiltersPopover filters={selection} options={filterOptions} optionsError={filterOptionsError} onChange={changeGroup} />
      <span className="ep-quick-search">
        <Search size={14} aria-hidden="true" />
        <input
          type="search"
          value={quickSearch}
          onChange={(e) => onQuickSearch(e.target.value)}
          placeholder={t('employeePlanning.toolbar.quickSearchPlaceholder')}
          aria-label={t('employeePlanning.toolbar.quickSearchAria')}
        />
      </span>
      <label className="ep-check ep-toolbar-check">
        <input type="checkbox" checked={selection.listView} onChange={(e) => onChange({ listView: e.target.checked })} />
        {t('employeePlanning.page.listView')}
      </label>
      {!selection.listView && <DensityToggle value={density} onChange={onDensityChange} />}
      <span className="ep-toolbar-spacer" />
      <ExportMenu selection={selection} />
      {canManage && (
        <Button variant="secondary" onClick={onCopyWeek}>
          <Copy size={15} aria-hidden="true" />
          {t('employeePlanning.page.copyWeek')}
        </Button>
      )}
    </div>
  )
}
