import { useCallback, useRef, useState } from 'react'
import { Users } from 'lucide-react'
import { MultiSelect, type MultiSelectOption } from '../../../components/ui/MultiSelect'
import { useLocale } from '../../../i18n/localeContext'
import { searchPlanningEmployees } from '../api/employeePlanningApi'
import { employeeDisplayName } from '../planningDays'
import { PlanningPopover } from './PlanningPopover'

interface EmployeePickerProps {
  employeeIds: string[]
  /** Known display names (from the grid and earlier picks) for the selected ids. */
  employeeNames: Record<string, string>
  onChange: (employeeIds: string[], names: Record<string, string>) => void
}

/**
 * "Medewerkers": explicit picks through the GLOBAL employee search. The search goes to its own
 * endpoint without any of the planning filters, so a planner looking at Dispatch can still find
 * and add a chauffeur from Transport. Debounced, server-side, keyboard-navigable (MultiSelect).
 */
export function EmployeePicker({ employeeIds, employeeNames, onChange }: EmployeePickerProps) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)
  // Names of everything the search ever returned, so a pick keeps its label after the results change.
  const seenRef = useRef<Record<string, string>>({})

  const search = useCallback(
    async (query: string, signal: AbortSignal): Promise<MultiSelectOption[]> => {
      const results = await searchPlanningEmployees(query, signal)
      for (const hit of results) seenRef.current[hit.id] = employeeDisplayName(hit)
      return results.map((hit) => ({
        value: hit.id,
        label: employeeDisplayName(hit),
        description: [hit.employeeNumber, hit.departmentName, hit.functionNames.join(', ')].filter(Boolean).join(' · '),
        badge: hit.isActive ? undefined : t('employeePlanning.picker.inactiveBadge'),
      }))
    },
    [t],
  )

  function handleChange(next: string[]) {
    const names: Record<string, string> = { ...employeeNames }
    for (const id of next) {
      if (seenRef.current[id]) names[id] = seenRef.current[id]
    }
    onChange(next, names)
  }

  return (
    <div className="ep-toolbar-popover-host">
      <button
        ref={buttonRef}
        type="button"
        className={`ep-toolbar-button${employeeIds.length > 0 ? ' is-active' : ''}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <Users size={15} aria-hidden="true" />
        {employeeIds.length > 0
          ? t('employeePlanning.toolbar.employeesWithCount', { count: employeeIds.length })
          : t('employeePlanning.toolbar.employees')}
      </button>
      <PlanningPopover open={open} onClose={() => setOpen(false)} anchorRef={buttonRef} label={t('employeePlanning.picker.title')} className="ep-popover-picker">
        <div className="ep-popover-header">
          <strong>{t('employeePlanning.picker.title')}</strong>
          <button type="button" className="ep-link-button" onClick={() => onChange([], employeeNames)} disabled={employeeIds.length === 0}>
            {t('employeePlanning.filters.clearAll')}
          </button>
        </div>
        <p className="ep-popover-hint">{t('employeePlanning.picker.hint')}</p>
        <MultiSelect
          id="ep-employee-picker"
          label={t('employeePlanning.toolbar.employees')}
          onSearch={search}
          values={employeeIds}
          onChange={handleChange}
          selectedLabels={employeeNames}
          placeholder={t('employeePlanning.picker.searchPlaceholder')}
          emptyMessage={t('employeePlanning.picker.noResults')}
        />
        <div className="ep-popover-footer">
          <button type="button" className="ep-toolbar-button" onClick={() => setOpen(false)}>
            {t('employeePlanning.picker.done')}
          </button>
        </div>
      </PlanningPopover>
    </div>
  )
}
