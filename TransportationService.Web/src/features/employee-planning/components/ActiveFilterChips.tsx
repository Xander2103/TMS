import { User, X } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { EMPTY_GROUP_FILTERS, hasAnyFilter, type PlanningSelection } from '../planningFilters'
import type { PlanningFilterOptions } from '../types'

interface ActiveFilterChipsProps {
  selection: PlanningSelection
  options: PlanningFilterOptions | null
  employeeNames: Record<string, string>
  /** Rows currently on screen (after the server selection). */
  shownCount: number
  /** Rows left after the client-side quick search, when it narrows the result. */
  quickFilteredCount: number | null
  totalInScope: number | null
  onChange: (patch: Partial<PlanningSelection>) => void
}

interface Chip {
  key: string
  label: string
  kind: 'group' | 'employee' | 'status'
  remove: () => void
}

/**
 * Makes the selection explainable at a glance: the result count, one removable chip per active
 * value (group filters, status scope, explicit employees — visually distinct) and a clear-all.
 * Nobody should have to guess why a person is or is not in the planning.
 */
export function ActiveFilterChips({ selection, options, employeeNames, shownCount, quickFilteredCount, totalInScope, onChange }: ActiveFilterChipsProps) {
  const { t } = useLocale()
  const name = (list: { id: string; name: string }[] | undefined, id: string) => list?.find((item) => item.id === id)?.name ?? '…'

  const chips: Chip[] = [
    ...selection.departmentIds.map<Chip>((id) => ({
      key: `d-${id}`, kind: 'group', label: name(options?.departments, id),
      remove: () => onChange({ departmentIds: selection.departmentIds.filter((v) => v !== id) }),
    })),
    ...selection.jobFunctionIds.map<Chip>((id) => ({
      key: `f-${id}`, kind: 'group', label: name(options?.jobFunctions, id),
      remove: () => onChange({ jobFunctionIds: selection.jobFunctionIds.filter((v) => v !== id) }),
    })),
    ...selection.contractTypeIds.map<Chip>((id) => ({
      key: `c-${id}`, kind: 'group', label: name(options?.contractTypes, id),
      remove: () => onChange({ contractTypeIds: selection.contractTypeIds.filter((v) => v !== id) }),
    })),
    ...(selection.driversOnly
      ? [{ key: 'drivers', kind: 'group', label: t('employeePlanning.chips.driversOnly'), remove: () => onChange({ driversOnly: false }) } satisfies Chip]
      : []),
    ...selection.statuses.map<Chip>((status) => ({
      key: `s-${status}`, kind: 'status',
      label: status === 'Active' ? t('employeePlanning.filters.statusActive') : t('employeePlanning.filters.statusInactive'),
      remove: () => onChange({ statuses: selection.statuses.filter((v) => v !== status) }),
    })),
    ...selection.employeeIds.map<Chip>((id) => ({
      key: `e-${id}`, kind: 'employee', label: employeeNames[id] ?? t('employeePlanning.chips.unknownEmployee'),
      remove: () => onChange({ employeeIds: selection.employeeIds.filter((v) => v !== id) }),
    })),
  ]

  const filtered = hasAnyFilter(selection)
  const count = filtered && totalInScope !== null && totalInScope >= shownCount
    ? t('employeePlanning.chips.countOfTotal', { count: shownCount, total: totalInScope })
    : t('employeePlanning.chips.count', { count: shownCount })

  return (
    <div className="ep-active-filters" role="group" aria-label={t('employeePlanning.chips.groupLabel')}>
      <span className="ep-count" aria-live="polite">
        {count}
        {quickFilteredCount !== null && quickFilteredCount !== shownCount && (
          <span className="ep-count-shown"> · {t('employeePlanning.chips.shown', { count: quickFilteredCount })}</span>
        )}
      </span>
      {chips.map((chip) => (
        <span key={chip.key} className={`ep-chip ep-chip-${chip.kind}`}>
          {chip.kind === 'employee' && <User size={12} aria-hidden="true" />}
          <span className="ep-chip-label">{chip.kind === 'employee' ? <span className="visually-hidden">{t('employeePlanning.chips.employeeChip', { name: '' })}</span> : null}{chip.label}</span>
          <button type="button" className="ep-chip-remove" aria-label={t('employeePlanning.chips.remove', { label: chip.label })} onClick={chip.remove}>
            <X size={12} aria-hidden="true" />
          </button>
        </span>
      ))}
      {filtered && (
        <button type="button" className="ep-link-button" onClick={() => onChange({ ...EMPTY_GROUP_FILTERS, employeeIds: [] })}>
          {t('employeePlanning.chips.clearFilters')}
        </button>
      )}
    </div>
  )
}
