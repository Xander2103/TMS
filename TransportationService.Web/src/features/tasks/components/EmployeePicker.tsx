import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useLocale } from '../../../i18n/localeContext'
import { useEmployeeOptions, type EmployeeOptionsSource } from './useEmployeeOptions'

type Translate = ReturnType<typeof useLocale>['t']

/** HR acceptance #4: why the assignable list is narrowed (or empty), shown under the picker. */
function scopeHint(t: Translate, state: ReturnType<typeof useEmployeeOptions>): string | null {
  if (state.isLoading || state.scope === undefined) return null
  switch (state.limitation) {
    case 'no_department':
      return t('tasks.picker.scopeNoDepartment')
    case 'no_employee_link':
      return t('tasks.picker.scopeNoEmployeeLink')
    case 'no_permission':
      return t('tasks.picker.scopeSelfOnly')
  }
  if (state.scope === 'department' && state.departmentName) {
    return t('tasks.picker.scopeDepartment', { department: state.departmentName })
  }
  return null
}

function ScopeHint({ text }: { text: string | null }) {
  return text ? (
    <p className="ui-form-field-hint task-picker-scope-hint" role="note">
      {text}
    </p>
  ) : null
}

interface EmployeeSelectProps {
  id?: string
  value: string | null
  onChange: (value: string | null) => void
  disabled?: boolean
  ariaLabel?: string
  /** "assignable": only employees the signed-in user may assign a task to (task assignee pickers). */
  source?: EmployeeOptionsSource
}

/** Single-employee combobox used by redistribute/apply/recurrence dialogs. */
export function EmployeeSelect({ id, value, onChange, disabled, ariaLabel, source = 'all' }: EmployeeSelectProps) {
  const { t } = useLocale()
  const employees = useEmployeeOptions(source)
  const { options, isLoading } = employees
  return (
    <>
      <SearchableSelect
        id={id}
        value={value}
        onChange={onChange}
        options={options}
        isLoading={isLoading}
        disabled={disabled}
        ariaLabel={ariaLabel ?? t('tasks.picker.employee')}
        placeholder={t('tasks.picker.searchPlaceholder')}
        emptyMessage={t('tasks.picker.empty')}
      />
      <ScopeHint text={scopeHint(t, employees)} />
    </>
  )
}

interface EmployeeMultiSelectProps {
  id?: string
  value: string[]
  onChange: (value: string[]) => void
  disabled?: boolean
  /** "assignable": only employees the signed-in user may assign a task to. */
  source?: EmployeeOptionsSource
}

/** Multi-employee picker: combobox to add, chips with remove. */
export function EmployeeMultiSelect({ id, value, onChange, disabled, source = 'all' }: EmployeeMultiSelectProps) {
  const { t } = useLocale()
  const employees = useEmployeeOptions(source)
  const { options, isLoading } = employees
  const selectable = options.filter((option) => !value.includes(option.value))
  const labelFor = (employeeId: string) => options.find((option) => option.value === employeeId)?.label ?? employeeId

  return (
    <div>
      <SearchableSelect
        id={id}
        value={null}
        onChange={(next) => {
          if (next && !value.includes(next)) onChange([...value, next])
        }}
        options={selectable}
        isLoading={isLoading}
        disabled={disabled}
        clearable={false}
        ariaLabel={t('tasks.picker.addEmployee')}
        placeholder={t('tasks.picker.addPlaceholder')}
        emptyMessage={t('tasks.picker.empty')}
      />
      <ScopeHint text={scopeHint(t, employees)} />
      {value.length > 0 && (
        <ul className="task-employee-chips">
          {value.map((employeeId) => (
            <li key={employeeId} className="task-employee-chip">
              {labelFor(employeeId)}
              {!disabled && (
                <button
                  type="button"
                  aria-label={t('tasks.picker.removeAria', { name: labelFor(employeeId) })}
                  onClick={() => onChange(value.filter((entry) => entry !== employeeId))}
                >
                  ×
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
