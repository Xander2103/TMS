import { useEffect, useRef, useState } from 'react'
import type { InputHTMLAttributes, KeyboardEvent, FocusEvent, ChangeEvent } from 'react'
import { useLocale } from '../../i18n/localeContext'
import { COMPLETE_TIME, normalizeTimeText, pad2 } from './timeText'
import './TimeInput.css'

export interface TimeInputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type' | 'onInvalid'> {
  /** Controlled value: 'HH:mm' (24h) or '' for empty. */
  value: string
  /** Called with a valid 'HH:mm' or '' (when cleared). Never called with an invalid/partial value. */
  onChange: (value: string) => void
  /** Optional: called when the user leaves the field with text that could not be normalized. */
  onInvalid?: (raw: string) => void
  id?: string
  className?: string
  disabled?: boolean
  required?: boolean
  'aria-label'?: string
  'aria-invalid'?: boolean | 'true' | 'false'
  /** Defaults to the localized `ui.time.placeholder` ("uu:mm" / "hh:mm"). */
  placeholder?: string
}

const MINUTES_PER_DAY = 24 * 60

/** Inserts the colon once a third digit arrives in a digits-only draft ("083" → "08:3"). */
function autoColon(raw: string): string {
  if (/^\d{3,4}$/.test(raw)) return `${raw.slice(0, 2)}:${raw.slice(2)}`
  return raw
}

function stepTime(time: string, deltaMinutes: number): string {
  const [h, m] = time.split(':').map(Number)
  const total = (((h * 60 + m + deltaMinutes) % MINUTES_PER_DAY) + MINUTES_PER_DAY) % MINUTES_PER_DAY
  return `${pad2(Math.floor(total / 60))}:${pad2(total % 60)}`
}

/**
 * 24-hour time field ("08:00", "12:00", "23:30") replacing native `<input type="time">`, which
 * renders a locale-dependent AM/PM segment UI in Chrome. Wire format is 'HH:mm' or '' for empty.
 * Keeps a draft while typing, emits only complete valid values, normalizes shorthand on blur and
 * supports ArrowUp/ArrowDown (±15 min, Shift = ±60 min).
 */
export function TimeInput({
  value,
  onChange,
  onInvalid,
  className,
  placeholder,
  'aria-invalid': ariaInvalidProp,
  onBlur,
  onFocus,
  onKeyDown,
  ...rest
}: TimeInputProps) {
  const { t } = useLocale()
  const [draft, setDraft] = useState(value)
  const [invalid, setInvalid] = useState(false)
  const focusedRef = useRef(false)

  // Follow the controlled value while the user is not editing the field.
  useEffect(() => {
    if (focusedRef.current) return
    setDraft(value)
    setInvalid(false)
  }, [value])

  const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
    const next = autoColon(event.target.value)
    setDraft(next)
    setInvalid(false)
    if (next === '') {
      if (value !== '') onChange('')
    } else if (COMPLETE_TIME.test(next)) {
      if (next !== value) onChange(next)
    }
  }

  const handleFocus = (event: FocusEvent<HTMLInputElement>) => {
    focusedRef.current = true
    onFocus?.(event)
  }

  const handleBlur = (event: FocusEvent<HTMLInputElement>) => {
    focusedRef.current = false
    const trimmed = draft.trim()
    if (trimmed === '') {
      setDraft('')
      setInvalid(false)
      if (value !== '') onChange('')
    } else {
      const normalized = normalizeTimeText(trimmed)
      if (normalized) {
        setDraft(normalized)
        setInvalid(false)
        if (normalized !== value) onChange(normalized)
      } else {
        setInvalid(true)
        onInvalid?.(draft)
      }
    }
    onBlur?.(event)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
      const base = normalizeTimeText(draft)
      if (base) {
        event.preventDefault()
        const delta = (event.shiftKey ? 60 : 15) * (event.key === 'ArrowUp' ? 1 : -1)
        const next = stepTime(base, delta)
        setDraft(next)
        setInvalid(false)
        if (next !== value) onChange(next)
      }
    }
    onKeyDown?.(event)
  }

  const ariaInvalid =
    invalid || ariaInvalidProp === true || ariaInvalidProp === 'true' ? 'true' : ariaInvalidProp

  return (
    <input
      {...rest}
      type="text"
      inputMode="numeric"
      autoComplete="off"
      pattern="([01]\d|2[0-3]):[0-5]\d"
      maxLength={5}
      className={className ? `ui-time-input ${className}` : 'ui-time-input'}
      placeholder={placeholder ?? t('ui.time.placeholder')}
      value={draft}
      aria-invalid={ariaInvalid}
      onChange={handleChange}
      onFocus={handleFocus}
      onBlur={handleBlur}
      onKeyDown={handleKeyDown}
    />
  )
}
