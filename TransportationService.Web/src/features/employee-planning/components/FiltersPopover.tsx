import { useMemo, useRef, useState } from 'react'
import { SlidersHorizontal } from 'lucide-react'
import { MultiSelect, type MultiSelectOption } from '../../../components/ui/MultiSelect'
import { useLocale } from '../../../i18n/localeContext'
import { EMPTY_GROUP_FILTERS, type PlanningEmployeeStatus, type PlanningGroupFilters } from '../planningFilters'
import type { PlanningFilterOptions } from '../types'
import { PlanningPopover } from './PlanningPopover'

interface FiltersPopoverProps {
  filters: PlanningGroupFilters
  options: PlanningFilterOptions | null
  optionsError: boolean
  onChange: (patch: Partial<PlanningGroupFilters>) => void
}

function toOptions(items: { id: string; name: string }[]): MultiSelectOption[] {
  return items.map((item) => ({ value: item.id, label: item.name }))
}

/**
 * The "Filters" action of the toolbar: a compact panel with searchable multi-selects for
 * departments, functions and contract types, the employment scope, the drivers-only switch and
 * the quick selections. Every change applies immediately (the grid keeps its previous rows
 * while the new selection loads), so no separate "apply" step is needed.
 */
export function FiltersPopover({ filters, options, optionsError, onChange }: FiltersPopoverProps) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)

  const activeCount =
    filters.departmentIds.length +
    filters.jobFunctionIds.length +
    filters.contractTypeIds.length +
    filters.statuses.length +
    (filters.driversOnly ? 1 : 0)

  const departmentOptions = useMemo(() => toOptions(options?.departments ?? []), [options])
  const functionOptions = useMemo(() => toOptions(options?.jobFunctions ?? []), [options])
  const contractOptions = useMemo(() => toOptions(options?.contractTypes ?? []), [options])

  function toggleStatus(status: PlanningEmployeeStatus, checked: boolean) {
    const next = checked ? [...filters.statuses.filter((s) => s !== status), status] : filters.statuses.filter((s) => s !== status)
    onChange({ statuses: next })
  }

  return (
    <div className="ep-toolbar-popover-host">
      <button
        ref={buttonRef}
        type="button"
        className={`ep-toolbar-button${activeCount > 0 ? ' is-active' : ''}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <SlidersHorizontal size={15} aria-hidden="true" />
        {activeCount > 0 ? t('employeePlanning.toolbar.filtersWithCount', { count: activeCount }) : t('employeePlanning.toolbar.filters')}
      </button>
      {/* Focus lands on the panel, not on the first control: the Afdelingen combobox opens on
          focus (shared MultiSelect behaviour) and must stay closed until the user clicks it. */}
      <PlanningPopover
        open={open}
        onClose={() => setOpen(false)}
        anchorRef={buttonRef}
        label={t('employeePlanning.filters.title')}
        className="ep-popover-filters"
        initialFocus="panel"
      >
        <div className="ep-popover-header">
          <strong>{t('employeePlanning.filters.title')}</strong>
          <button type="button" className="ep-link-button" onClick={() => onChange({ ...EMPTY_GROUP_FILTERS })} disabled={activeCount === 0}>
            {t('employeePlanning.filters.clearAll')}
          </button>
        </div>
        {optionsError && <p className="ep-popover-error" role="alert">{t('employeePlanning.filters.optionsLoadFailed')}</p>}

        {(options?.hasDrivers || options?.myDepartmentId) && (
          <div className="ep-popover-section">
            <span className="ep-popover-label">{t('employeePlanning.filters.quick')}</span>
            <div className="ep-quick-row">
              {options?.hasDrivers && (
                <button
                  type="button"
                  className={`ep-quick-button${filters.driversOnly ? ' is-active' : ''}`}
                  aria-pressed={filters.driversOnly}
                  onClick={() => onChange({ driversOnly: !filters.driversOnly })}
                >
                  {t('employeePlanning.filters.quickDrivers')}
                </button>
              )}
              {options?.myDepartmentId && (
                <button
                  type="button"
                  className={`ep-quick-button${filters.departmentIds.includes(options.myDepartmentId) ? ' is-active' : ''}`}
                  aria-pressed={filters.departmentIds.includes(options.myDepartmentId)}
                  onClick={() => {
                    const mine = options.myDepartmentId!
                    onChange({
                      departmentIds: filters.departmentIds.includes(mine)
                        ? filters.departmentIds.filter((id) => id !== mine)
                        : [...filters.departmentIds, mine],
                    })
                  }}
                >
                  {t('employeePlanning.filters.quickMyDepartment')}
                </button>
              )}
            </div>
          </div>
        )}

        <div className="ep-popover-section">
          <label className="ep-popover-label" htmlFor="ep-filter-departments">{t('employeePlanning.filters.departments')}</label>
          <MultiSelect
            id="ep-filter-departments"
            label={t('employeePlanning.filters.departments')}
            options={departmentOptions}
            values={filters.departmentIds}
            onChange={(departmentIds) => onChange({ departmentIds })}
            placeholder={t('employeePlanning.filters.searchPlaceholder')}
          />
        </div>
        <div className="ep-popover-section">
          <label className="ep-popover-label" htmlFor="ep-filter-functions">{t('employeePlanning.filters.functions')}</label>
          <MultiSelect
            id="ep-filter-functions"
            label={t('employeePlanning.filters.functions')}
            options={functionOptions}
            values={filters.jobFunctionIds}
            onChange={(jobFunctionIds) => onChange({ jobFunctionIds })}
            placeholder={t('employeePlanning.filters.searchPlaceholder')}
          />
        </div>
        {contractOptions.length > 0 && (
          <div className="ep-popover-section">
            <label className="ep-popover-label" htmlFor="ep-filter-contracts">{t('employeePlanning.filters.contractTypes')}</label>
            <MultiSelect
              id="ep-filter-contracts"
              label={t('employeePlanning.filters.contractTypes')}
              options={contractOptions}
              values={filters.contractTypeIds}
              onChange={(contractTypeIds) => onChange({ contractTypeIds })}
              placeholder={t('employeePlanning.filters.searchPlaceholder')}
            />
          </div>
        )}
        <fieldset className="ep-popover-section ep-popover-fieldset">
          <legend className="ep-popover-label">{t('employeePlanning.filters.status')}</legend>
          <label className="ep-check">
            <input
              type="checkbox"
              checked={filters.statuses.includes('Active')}
              onChange={(event) => toggleStatus('Active', event.target.checked)}
            />
            {t('employeePlanning.filters.statusActive')}
          </label>
          <label className="ep-check">
            <input
              type="checkbox"
              checked={filters.statuses.includes('Inactive')}
              onChange={(event) => toggleStatus('Inactive', event.target.checked)}
            />
            {t('employeePlanning.filters.statusInactive')}
          </label>
          <p className="ep-popover-hint">{t('employeePlanning.filters.statusHint')}</p>
        </fieldset>
        {options?.hasDrivers && (
          <div className="ep-popover-section">
            <label className="ep-check">
              <input type="checkbox" checked={filters.driversOnly} onChange={(event) => onChange({ driversOnly: event.target.checked })} />
              {t('employeePlanning.filters.driversOnly')}
            </label>
          </div>
        )}
        <p className="ep-popover-hint">{t('employeePlanning.filters.semantics')}</p>
        <div className="ep-popover-footer">
          <button type="button" className="ep-toolbar-button" onClick={() => setOpen(false)}>
            {t('employeePlanning.filters.close')}
          </button>
        </div>
      </PlanningPopover>
    </div>
  )
}
