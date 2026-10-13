import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, X } from 'lucide-react'
import { useLocale } from '../../i18n/localeContext'
import './MultiSelect.css'

export interface MultiSelectOption {
  value: string
  label: string
  /** Secondary line under the label (e.g. department · function). */
  description?: string
  /** Small pill after the label (e.g. "Uit dienst"). */
  badge?: string
  /** Extra search terms matched besides the label in sync mode. */
  keywords?: string
}

export type MultiSelectSearchFn = (query: string, signal: AbortSignal) => Promise<MultiSelectOption[]>

export interface MultiSelectProps {
  id?: string
  /** Accessible name of the combobox; also the label of the chip group. */
  label: string
  values: string[]
  onChange: (values: string[]) => void
  /**
   * Sync mode: the full option list, filtered client-side on label + keywords.
   * Async mode (`onSearch` set): only used to resolve labels of already selected values.
   */
  options?: MultiSelectOption[]
  /**
   * Async mode: server-side search, called on open and on every (debounced) query change. The
   * previous request is aborted through `signal`; stale responses are dropped. Results are never
   * filtered client-side, so the caller decides what "everyone" means.
   */
  onSearch?: MultiSelectSearchFn
  searchDebounceMs?: number
  searchMinChars?: number
  /** Labels for selected values that are not (or no longer) in `options` / the current results. */
  selectedLabels?: Record<string, string>
  placeholder?: string
  disabled?: boolean
  /** Shows the selected values as removable chips under the control. Default true. */
  showChips?: boolean
  emptyMessage?: string
  /** Hint shown while the query is shorter than `searchMinChars`. */
  minCharsMessage?: string
}

type AsyncStatus = 'idle' | 'loading' | 'done' | 'error'

function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError'
}

/**
 * Searchable multi-select combobox: a type-to-search input that keeps its listbox open while the
 * user toggles several options (checkmarks, `aria-selected`), with full keyboard support
 * (arrows, Home/End, Enter/Space toggles, Escape closes, Backspace on an empty query removes
 * the last chip). Sync mode filters a given list; async mode delegates to `onSearch` — used for
 * the global employee search, whose results must never be narrowed by other filters.
 */
export function MultiSelect({
  id,
  label,
  values,
  onChange,
  options = [],
  onSearch,
  searchDebounceMs = 250,
  searchMinChars = 0,
  selectedLabels,
  placeholder,
  disabled,
  showChips = true,
  emptyMessage,
  minCharsMessage,
}: MultiSelectProps) {
  const { t } = useLocale()
  const generatedId = useId()
  const baseId = id ?? generatedId
  const listboxId = `${baseId}-listbox`
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [highlighted, setHighlighted] = useState(0)
  // Labels of everything ever selected in this instance, so a chip keeps its text after the
  // search results that produced it are gone.
  const [remembered, setRemembered] = useState<Record<string, MultiSelectOption>>({})

  const isAsync = typeof onSearch === 'function'
  const onSearchRef = useRef(onSearch)
  useEffect(() => {
    onSearchRef.current = onSearch
  })
  const [asyncOptions, setAsyncOptions] = useState<MultiSelectOption[] | null>(null)
  const [asyncStatus, setAsyncStatus] = useState<AsyncStatus>('idle')
  const seqRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  const firedSinceOpenRef = useRef(false)

  const trimmedQuery = query.trim()
  const belowMinChars = isAsync && trimmedQuery.length < searchMinChars

  useEffect(() => {
    if (!isAsync || !open || belowMinChars) return
    const immediate = !firedSinceOpenRef.current
    firedSinceOpenRef.current = true
    const timer = setTimeout(
      () => {
        const search = onSearchRef.current
        if (!search) return
        abortRef.current?.abort()
        const controller = new AbortController()
        abortRef.current = controller
        const seq = ++seqRef.current
        setAsyncStatus('loading')
        search(trimmedQuery, controller.signal).then(
          (results) => {
            if (seq !== seqRef.current) return
            setAsyncOptions(results)
            setAsyncStatus('done')
          },
          (error: unknown) => {
            if (isAbortError(error) || seq !== seqRef.current) return
            setAsyncStatus('error')
          },
        )
      },
      immediate ? 0 : searchDebounceMs,
    )
    return () => clearTimeout(timer)
  }, [isAsync, open, belowMinChars, trimmedQuery, searchDebounceMs])

  useEffect(() => {
    const abort = abortRef
    const seq = seqRef
    return () => {
      abort.current?.abort()
      seq.current++
    }
  }, [])

  const close = useCallback(() => {
    setOpen(false)
    setQuery('')
    abortRef.current?.abort()
    abortRef.current = null
    seqRef.current++
    firedSinceOpenRef.current = false
    setAsyncOptions(null)
    setAsyncStatus('idle')
  }, [])

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) close()
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [open, close])

  const filtered = useMemo(() => {
    if (isAsync) return belowMinChars ? [] : (asyncOptions ?? [])
    const q = trimmedQuery.toLowerCase()
    if (!q) return options
    const words = q.split(/\s+/)
    return options.filter((option) => {
      const haystack = `${option.label} ${option.keywords ?? ''}`.toLowerCase()
      return words.every((word) => haystack.includes(word))
    })
  }, [isAsync, belowMinChars, asyncOptions, options, trimmedQuery])

  const labelFor = useCallback(
    (value: string): string =>
      options.find((o) => o.value === value)?.label
      ?? asyncOptions?.find((o) => o.value === value)?.label
      ?? remembered[value]?.label
      ?? selectedLabels?.[value]
      ?? '…',
    [options, asyncOptions, remembered, selectedLabels],
  )

  const selectedSet = useMemo(() => new Set(values), [values])
  const rowCount = filtered.length
  const activeIndex = rowCount === 0 ? 0 : Math.min(highlighted, rowCount - 1)

  function openList() {
    if (disabled) return
    setOpen(true)
    setHighlighted(0)
  }

  function toggle(option: MultiSelectOption) {
    setRemembered((current) => (current[option.value] ? current : { ...current, [option.value]: option }))
    onChange(selectedSet.has(option.value) ? values.filter((v) => v !== option.value) : [...values, option.value])
    // Keep the list open for the next pick; keep focus in the input.
    inputRef.current?.focus()
  }

  function remove(value: string) {
    onChange(values.filter((v) => v !== value))
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
    } else if (event.key === 'Home' && open && rowCount > 0) {
      event.preventDefault()
      setHighlighted(0)
    } else if (event.key === 'End' && open && rowCount > 0) {
      event.preventDefault()
      setHighlighted(rowCount - 1)
    } else if (event.key === 'Enter' || (event.key === ' ' && trimmedQuery.length === 0 && open)) {
      if (open && rowCount > 0) {
        event.preventDefault()
        toggle(filtered[activeIndex])
      }
    } else if (event.key === 'Escape') {
      if (open) {
        event.stopPropagation()
        close()
      }
    } else if (event.key === 'Backspace' && query.length === 0 && values.length > 0) {
      remove(values[values.length - 1])
    } else if (event.key === 'Tab') {
      close()
    }
  }

  const searching = isAsync && asyncStatus === 'loading'
  const showLoading = searching && asyncOptions === null
  const showError = isAsync && asyncStatus === 'error'
  const settled = !isAsync || asyncStatus === 'done'
  const summary = values.length > 0 ? t('ui.multiSelect.selectedCount', { count: values.length }) : null

  return (
    <div className={`ui-multi-select${open ? ' is-open' : ''}${disabled ? ' is-disabled' : ''}`} ref={rootRef}>
      <div className="ui-multi-select-control">
        <input
          ref={inputRef}
          id={baseId}
          className="ui-multi-select-input"
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={open && rowCount > 0 ? `${baseId}-opt-${activeIndex}` : undefined}
          aria-label={label}
          autoComplete="off"
          placeholder={summary ?? placeholder ?? t('ui.select.placeholder')}
          value={query}
          disabled={disabled}
          onFocus={openList}
          onClick={openList}
          onChange={(event) => {
            setQuery(event.target.value)
            setHighlighted(0)
            if (!open) setOpen(true)
          }}
          onKeyDown={handleKeyDown}
        />
        <span className="ui-multi-select-adornments" aria-hidden="true">
          {values.length > 0 && <span className="ui-multi-select-count">{values.length}</span>}
          <ChevronDown size={16} className="ui-multi-select-caret" />
        </span>
      </div>
      <ul
        id={listboxId}
        role="listbox"
        aria-multiselectable="true"
        aria-label={label}
        className="ui-multi-select-list"
        hidden={!open}
      >
        {open && showLoading && <li className="ui-multi-select-note">{t('ui.select.loading')}</li>}
        {open && showError && <li className="ui-multi-select-note ui-multi-select-note-error">{t('ui.select.searchError')}</li>}
        {open && belowMinChars && (
          <li className="ui-multi-select-note">{minCharsMessage ?? t('ui.multiSelect.typeToSearch')}</li>
        )}
        {open && settled && !belowMinChars && rowCount === 0 && !showError && (
          <li className="ui-multi-select-note">{emptyMessage ?? t('ui.select.noResults')}</li>
        )}
        {open &&
          filtered.map((option, index) => {
            const selected = selectedSet.has(option.value)
            return (
              <li
                key={option.value}
                id={`${baseId}-opt-${index}`}
                role="option"
                aria-selected={selected}
                className={`ui-multi-select-option${index === activeIndex ? ' is-highlighted' : ''}${selected ? ' is-selected' : ''}`}
                onMouseEnter={() => setHighlighted(index)}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => toggle(option)}
              >
                <span className="ui-multi-select-check" aria-hidden="true">
                  {selected && <Check size={14} />}
                </span>
                <span className="ui-multi-select-option-text">
                  <span className="ui-multi-select-option-label">
                    {option.label}
                    {option.badge && <span className="ui-multi-select-badge">{option.badge}</span>}
                  </span>
                  {option.description && <span className="ui-multi-select-option-description">{option.description}</span>}
                </span>
              </li>
            )
          })}
      </ul>
      {showChips && values.length > 0 && (
        <ul className="ui-multi-select-chips" aria-label={t('ui.multiSelect.selectedLabel', { label })}>
          {values.map((value) => (
            <li key={value} className="ui-multi-select-chip">
              <span>{labelFor(value)}</span>
              <button
                type="button"
                className="ui-multi-select-chip-remove"
                aria-label={t('ui.multiSelect.remove', { label: labelFor(value) })}
                onClick={() => remove(value)}
                disabled={disabled}
              >
                <X size={12} aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
