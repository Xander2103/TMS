import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { useLocale } from '../../i18n/localeContext'
import './SearchableSelect.css'

export interface SearchableSelectOption {
  value: string
  label: string
  /** Secondary line shown under the label in the dropdown. */
  description?: string
  /** Extra search terms (e.g. ISO codes) matched in addition to the label. */
  keywords?: string
}

export interface SearchableSelectCreateConfig {
  /** Label for the create row, given the current query (e.g. q => `"${q}" toevoegen`). */
  label: (query: string) => string
  /** Creates the entity; resolve with the new option to auto-select it, or null to abort. */
  create: (query: string) => Promise<SearchableSelectOption | null>
}

export interface SearchableSelectProps {
  id?: string
  value: string | null
  onChange: (value: string | null) => void
  options: SearchableSelectOption[]
  placeholder?: string
  disabled?: boolean
  isLoading?: boolean
  /** Show a clear (×) button when a value is selected. Default true. */
  clearable?: boolean
  emptyMessage?: string
  ariaLabel?: string
  /** Optional inline-create action rendered as the last row when the query has no exact match. */
  onCreate?: SearchableSelectCreateConfig
}

const CREATE_ROW = '__create__'

/**
 * Type-to-search combobox used for every entity/lookup dropdown in the app.
 * Filters on label + keywords, supports full keyboard navigation and an optional
 * permission-gated inline-create row supplied by the caller.
 */
export function SearchableSelect({
  id,
  value,
  onChange,
  options,
  placeholder,
  disabled,
  isLoading,
  clearable = true,
  emptyMessage,
  ariaLabel,
  onCreate,
}: SearchableSelectProps) {
  const { t } = useLocale()
  const generatedId = useId()
  const baseId = id ?? generatedId
  const listboxId = `${baseId}-listbox`
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [highlighted, setHighlighted] = useState(0)
  const [creating, setCreating] = useState(false)
  // Bridges the gap between creating an option and the parent refreshing its options list.
  const [createdOption, setCreatedOption] = useState<SearchableSelectOption | null>(null)

  const allOptions = useMemo(() => {
    if (createdOption && !options.some((o) => o.value === createdOption.value)) {
      return [...options, createdOption]
    }
    return options
  }, [options, createdOption])

  const selected = allOptions.find((o) => o.value === value) ?? null

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return allOptions
    return allOptions.filter(
      (o) => o.label.toLowerCase().includes(q) || (o.keywords ?? '').toLowerCase().includes(q),
    )
  }, [allOptions, query])

  const trimmedQuery = query.trim()
  const showCreateRow =
    Boolean(onCreate) &&
    trimmedQuery.length > 0 &&
    !filtered.some((o) => o.label.toLowerCase() === trimmedQuery.toLowerCase())

  const rowCount = filtered.length + (showCreateRow ? 1 : 0)
  // Clamp instead of resetting through an effect: the option list can shrink while filtering.
  const activeIndex = rowCount === 0 ? 0 : Math.min(highlighted, rowCount - 1)

  // Close when clicking outside; revert the input text to the selected label.
  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false)
        setQuery('')
      }
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [open])

  function openList() {
    if (disabled) return
    setOpen(true)
    setHighlighted(0)
  }

  function close() {
    setOpen(false)
    setQuery('')
  }

  function selectOption(option: SearchableSelectOption) {
    onChange(option.value)
    close()
  }

  async function runCreate() {
    if (!onCreate || creating) return
    setCreating(true)
    try {
      const created = await onCreate.create(trimmedQuery)
      if (created) {
        setCreatedOption(created)
        onChange(created.value)
        close()
      }
    } finally {
      setCreating(false)
    }
  }

  function activateRow(index: number) {
    if (index < filtered.length) {
      selectOption(filtered[index])
    } else if (showCreateRow) {
      void runCreate()
    }
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      if (!open) return openList()
      setHighlighted(rowCount === 0 ? 0 : (activeIndex + 1) % rowCount)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) return openList()
      setHighlighted(rowCount === 0 ? 0 : (activeIndex - 1 + rowCount) % rowCount)
    } else if (event.key === 'Enter') {
      if (open) {
        event.preventDefault()
        activateRow(activeIndex)
      }
    } else if (event.key === 'Escape') {
      if (open) {
        event.stopPropagation()
        close()
      }
    } else if (event.key === 'Tab') {
      close()
    }
  }

  const displayValue = open ? query : (selected?.label ?? '')

  return (
    <div className="ui-searchable-select" ref={rootRef}>
      <div className="ui-searchable-select-control">
        <input
          ref={inputRef}
          id={baseId}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={open && rowCount > 0 ? `${baseId}-opt-${activeIndex}` : undefined}
          aria-label={ariaLabel}
          autoComplete="off"
          placeholder={selected ? selected.label : (placeholder ?? t('ui.select.placeholder'))}
          value={displayValue}
          disabled={disabled}
          onFocus={openList}
          onClick={openList}
          onChange={(e) => {
            setQuery(e.target.value)
            setHighlighted(0)
            if (!open) setOpen(true)
          }}
          onKeyDown={handleKeyDown}
        />
        {clearable && selected && !disabled && (
          <button
            type="button"
            className="ui-searchable-select-clear"
            aria-label={t('ui.select.clear')}
            onClick={() => {
              onChange(null)
              setQuery('')
              inputRef.current?.focus()
            }}
          >
            ×
          </button>
        )}
        <span className="ui-searchable-select-caret" aria-hidden="true">
          ▾
        </span>
      </div>
      {open && (
        <ul className="ui-searchable-select-list" role="listbox" id={listboxId}>
          {isLoading && <li className="ui-searchable-select-note">{t('ui.select.loading')}</li>}
          {!isLoading &&
            filtered.map((option, index) => (
              <li
                key={option.value}
                id={`${baseId}-opt-${index}`}
                role="option"
                aria-selected={option.value === value}
                className={[
                  'ui-searchable-select-option',
                  index === activeIndex ? 'is-highlighted' : '',
                  option.value === value ? 'is-selected' : '',
                ]
                  .filter(Boolean)
                  .join(' ')}
                onPointerDown={(e) => e.preventDefault()}
                onClick={() => activateRow(index)}
                onMouseEnter={() => setHighlighted(index)}
              >
                <span className="ui-searchable-select-option-label">{option.label}</span>
                {option.description && (
                  <span className="ui-searchable-select-option-description">{option.description}</span>
                )}
              </li>
            ))}
          {!isLoading && filtered.length === 0 && !showCreateRow && (
            <li className="ui-searchable-select-note">{emptyMessage ?? t('ui.select.noResults')}</li>
          )}
          {!isLoading && showCreateRow && (
            <li
              key={CREATE_ROW}
              id={`${baseId}-opt-${filtered.length}`}
              role="option"
              aria-selected={false}
              className={[
                'ui-searchable-select-option',
                'ui-searchable-select-create',
                activeIndex === filtered.length ? 'is-highlighted' : '',
              ]
                .filter(Boolean)
                .join(' ')}
              onPointerDown={(e) => e.preventDefault()}
              onClick={() => void runCreate()}
              onMouseEnter={() => setHighlighted(filtered.length)}
            >
              {creating ? t('ui.select.adding') : `+ ${onCreate!.label(trimmedQuery)}`}
            </li>
          )}
        </ul>
      )}
    </div>
  )
}
