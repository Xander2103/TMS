import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useEmployeeOptions } from './useEmployeeOptions'

interface EmployeeSelectProps {
  id?: string
  value: string | null
  onChange: (value: string | null) => void
  disabled?: boolean
  ariaLabel?: string
}

/** Single-employee combobox used by redistribute/apply/recurrence dialogs. */
export function EmployeeSelect({ id, value, onChange, disabled, ariaLabel }: EmployeeSelectProps) {
  const { options, isLoading } = useEmployeeOptions()
  return (
    <SearchableSelect
      id={id}
      value={value}
      onChange={onChange}
      options={options}
      isLoading={isLoading}
      disabled={disabled}
      ariaLabel={ariaLabel ?? 'Medewerker'}
      placeholder="Zoek een medewerker..."
      emptyMessage="Geen medewerkers gevonden"
    />
  )
}

interface EmployeeMultiSelectProps {
  id?: string
  value: string[]
  onChange: (value: string[]) => void
  disabled?: boolean
}

/** Multi-employee picker: combobox to add, chips with remove. */
export function EmployeeMultiSelect({ id, value, onChange, disabled }: EmployeeMultiSelectProps) {
  const { options, isLoading } = useEmployeeOptions()
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
        ariaLabel="Medewerker toevoegen"
        placeholder="Zoek en voeg medewerkers toe..."
        emptyMessage="Geen medewerkers gevonden"
      />
      {value.length > 0 && (
        <ul className="task-employee-chips">
          {value.map((employeeId) => (
            <li key={employeeId} className="task-employee-chip">
              {labelFor(employeeId)}
              {!disabled && (
                <button
                  type="button"
                  aria-label={`${labelFor(employeeId)} verwijderen`}
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
