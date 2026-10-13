import { useState } from 'react'
import './InlineSelect.css'

interface InlineSelectProps<T extends string> {
  value: T
  options: readonly T[]
  labels: Record<T, string>
  /** Persists the change server-side; throw to keep the old value (the caller shows the error). */
  onChange: (value: T) => Promise<void>
  ariaLabel: string
  disabled?: boolean
}

/**
 * Safe inline edit for simple enum fields: backend-validated, shows a busy state, restores
 * the previous value on rejection, keyboard/screen-reader accessible (a plain select).
 */
export function InlineSelect<T extends string>({
  value, options, labels, onChange, ariaLabel, disabled = false,
}: InlineSelectProps<T>) {
  const [busy, setBusy] = useState(false)
  const [current, setCurrent] = useState(value)

  return (
    <select
      className="ui-inline-select"
      value={current}
      aria-label={ariaLabel}
      disabled={disabled || busy}
      aria-busy={busy}
      onClick={(event) => event.stopPropagation()}
      onChange={(event) => {
        const next = event.target.value as T
        const previous = current
        setCurrent(next)
        setBusy(true)
        onChange(next)
          .catch(() => setCurrent(previous))
          .finally(() => setBusy(false))
      }}
    >
      {options.map((option) => (
        <option key={option} value={option}>{labels[option]}</option>
      ))}
    </select>
  )
}
