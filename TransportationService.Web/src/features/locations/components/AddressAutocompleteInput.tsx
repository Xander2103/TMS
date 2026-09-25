import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { ADDRESS_PICKER_GROUP_KEYS, type AddressPickerOption } from '../api/customerAddressesApi'
import { fullAddressLine } from '../addressFields'
import { useAddressSearch } from '../hooks/useAddressSearch'
import './AddressAutocompleteInput.css'

interface AddressAutocompleteInputProps {
  id?: string
  /** The typed text. It is the planner's own: a suggestion never replaces it on its own. */
  value: string
  onChange: (text: string) => void
  /** Called ONLY on an explicit choice (click, or Enter on a row reached with the arrow keys). */
  onSelect: (option: AddressPickerOption) => void
  /** Rank this customer's addresses first. */
  customerId?: string | null
  disabled?: boolean
  placeholder?: string
  maxLength?: number
  ariaLabel?: string
  ariaInvalid?: boolean
  /**
   * A click or ArrowDown on the EMPTY field already offers the customer's own and recent
   * addresses (the address-search field). Typing always starts suggesting at 2 characters.
   */
  suggestWhenEmpty?: boolean
  /** Optional last row of the popover (e.g. "+ Nieuw adres «query»"); runs with the typed text. */
  footerAction?: { label: (query: string) => string; run: (query: string) => void }
}

/**
 * GPS-style address suggestions under a plain text field (master sprint 2026-09-21, D3). The
 * SAME component drives the stop's address-search field and its street field: typing
 * "Avenue Sab" offers "Novellini — Avenue Sabin 1, 1300 Waver" in both.
 *
 * Deliberately NOT a select: the text stays exactly what was typed, no row is pre-highlighted,
 * Enter without a highlighted row only closes the list, and a free address that matches nothing
 * is perfectly valid. The popover is absolutely positioned, so it never shifts the form.
 */
export function AddressAutocompleteInput({
  id,
  value,
  onChange,
  onSelect,
  customerId,
  disabled,
  placeholder,
  maxLength,
  ariaLabel,
  ariaInvalid,
  suggestWhenEmpty = false,
  footerAction,
}: AddressAutocompleteInputProps) {
  const { t } = useLocale()
  const generatedId = useId()
  const baseId = id ?? generatedId
  const listboxId = `${baseId}-suggestions`
  const rootRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  /** -1 = nothing highlighted: the first result is never selected on the planner's behalf. */
  const [highlighted, setHighlighted] = useState(-1)

  const { results, status, active } = useAddressSearch(value, open && !disabled, { customerId, suggestWhenEmpty })
  // The create row needs a typed name; the empty-field suggestions (`suggestWhenEmpty`) never offer it.
  const showFooter = Boolean(footerAction) && active && value.trim() !== ''
  const rowCount = results.length + (showFooter ? 1 : 0)
  const visible = open && active
  const activeIndex = highlighted >= rowCount ? -1 : highlighted

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [open])

  function close() {
    setOpen(false)
    setHighlighted(-1)
  }

  function activate(index: number) {
    if (index < results.length) {
      onSelect(results[index])
    } else if (footerAction) {
      footerAction.run(value.trim())
    }
    close()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      if (!visible) {
        if (event.key === 'ArrowDown') setOpen(true)
        return
      }
      event.preventDefault()
      if (rowCount === 0) return
      const step = event.key === 'ArrowDown' ? 1 : -1
      // From "nothing" ArrowDown lands on the first row and ArrowUp on the last; then it wraps.
      setHighlighted(activeIndex === -1 ? (step === 1 ? 0 : rowCount - 1) : (activeIndex + step + rowCount) % rowCount)
    } else if (event.key === 'Enter') {
      if (!visible) return
      // Never submit the surrounding form from under an open suggestion list.
      event.preventDefault()
      if (activeIndex >= 0) activate(activeIndex)
      else close()
    } else if (event.key === 'Escape') {
      if (!visible) return
      // Only the list closes — not the drawer or dialog the form lives in.
      event.stopPropagation()
      close()
    } else if (event.key === 'Tab') {
      close()
    }
  }

  return (
    <div className="address-autocomplete" ref={rootRef}>
      <input
        id={baseId}
        type="text"
        role="combobox"
        aria-expanded={visible}
        aria-controls={listboxId}
        aria-autocomplete="list"
        aria-activedescendant={visible && activeIndex >= 0 ? `${baseId}-suggestion-${activeIndex}` : undefined}
        aria-label={ariaLabel}
        aria-invalid={ariaInvalid ? true : undefined}
        autoComplete="off"
        value={value}
        placeholder={placeholder}
        maxLength={maxLength}
        disabled={disabled}
        onChange={(event) => {
          onChange(event.target.value)
          setOpen(true)
          setHighlighted(-1)
        }}
        onClick={suggestWhenEmpty ? () => setOpen(true) : undefined}
        onKeyDown={handleKeyDown}
      />
      <span className="address-autocomplete-status" role="status" aria-live="polite">
        {visible && status === 'done' ? t('ui.select.resultCount', { count: results.length }) : ''}
      </span>
      {visible && (
        <ul className="address-autocomplete-list" role="listbox" id={listboxId}>
          {results.map((option, index) => {
            const line = fullAddressLine(option)
            const owner = option.customerNames ?? option.customerName ?? null
            return (
              <li
                key={option.locationId}
                id={`${baseId}-suggestion-${index}`}
                role="option"
                aria-selected={index === activeIndex}
                className={index === activeIndex ? 'address-autocomplete-option is-highlighted' : 'address-autocomplete-option'}
                onPointerDown={(event) => event.preventDefault()}
                onClick={() => activate(index)}
              >
                <span className="address-autocomplete-name">{option.name}</span>
                {line && <span className="address-autocomplete-line">{line}</span>}
                <span className="address-autocomplete-meta">
                  {owner ? `${t(ADDRESS_PICKER_GROUP_KEYS[option.group])} · ${owner}` : t(ADDRESS_PICKER_GROUP_KEYS[option.group])}
                </span>
              </li>
            )
          })}
          {status === 'loading' && results.length === 0 && (
            <li className="address-autocomplete-note">{t('stopEditor.search.searching')}</li>
          )}
          {status === 'error' && (
            <li className="address-autocomplete-note address-autocomplete-note-error">{t('stopEditor.search.failed')}</li>
          )}
          {status === 'done' && results.length === 0 && (
            <li className="address-autocomplete-note">{t('stopEditor.search.noResults')}</li>
          )}
          {showFooter && footerAction && (
            <li
              id={`${baseId}-suggestion-${results.length}`}
              role="option"
              aria-selected={activeIndex === results.length}
              className={
                activeIndex === results.length
                  ? 'address-autocomplete-option address-autocomplete-footer is-highlighted'
                  : 'address-autocomplete-option address-autocomplete-footer'
              }
              onPointerDown={(event) => event.preventDefault()}
              onClick={() => activate(results.length)}
            >
              + {footerAction.label(value.trim())}
            </li>
          )}
        </ul>
      )}
    </div>
  )
}
