import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { useLocale } from '../../i18n/localeContext'
import './SearchableSelect.css'

export interface SearchableSelectOption {
  value: string
  /** Committed text: shown in the input once selected and used for client-side filtering. */
  label: string
  /** Secondary line shown under the label in the dropdown (classic two-line rendering). */
  description?: string
  /** Extra search terms (e.g. ISO codes) matched in addition to the label. */
  keywords?: string
  /** Rich rendering — first line; defaults to `label`. */
  title?: string
  /** Rich rendering — second line (e.g. street, postal code and city). */
  subtitle?: string
  /** Rich rendering — third muted line (e.g. group / customer names). */
  meta?: string
  /** Rich rendering — small pills after the title. */
  badges?: string[]
}

export interface SearchableSelectCreateConfig {
  /** Label for the create row, given the current query (e.g. q => `"${q}" toevoegen`). */
  label: (query: string) => string
  /** Creates the entity; resolve with the new option to auto-select it, or null to abort. */
  create: (query: string) => Promise<SearchableSelectOption | null>
  /**
   * Also render the create row while the query is empty (a permanent "manage" shortcut at the
   * bottom of the list). With a non-empty query the usual `label(query)` row applies.
   */
  alwaysShow?: boolean
  /** Complete row text for the empty-query row (e.g. "+ Nieuwe afdeling"); falls back to `label('')`. */
  emptyQueryLabel?: string
}

export type SearchableSelectSearchFn = (query: string, signal: AbortSignal) => Promise<SearchableSelectOption[]>

export interface SearchableSelectProps {
  id?: string
  value: string | null
  onChange: (value: string | null) => void
  /**
   * Sync mode: the full list, filtered client-side. Async mode (`onSearch` set): only used to
   * resolve the label of the current `value` — pass `[]` plus `selectedLabel` when unknown.
   */
  options: SearchableSelectOption[]
  placeholder?: string
  disabled?: boolean
  isLoading?: boolean
  /** Show a clear (×) button when a value is selected. Default true. */
  clearable?: boolean
  emptyMessage?: string
  ariaLabel?: string
  /**
   * Optional inline-create action rendered as the last row when the query has no exact match
   * (and, with `alwaysShow`, also while the query is empty).
   */
  onCreate?: SearchableSelectCreateConfig
  /**
   * Async mode: server-side search. Called on open (immediately) and on every query change
   * (debounced); the previous request is aborted through `signal` and stale responses are
   * dropped. No client-side filtering happens in this mode.
   */
  onSearch?: SearchableSelectSearchFn
  /** Minimum query length before `onSearch` runs. Default 0 (empty query = "suggested" items). */
  searchMinChars?: number
  /** Debounce for query changes in async mode. Default 250 ms. */
  searchDebounceMs?: number
  /** Label shown for `value` when no option (sync list, search result or previous selection) matches it. */
  selectedLabel?: string
}

const CREATE_ROW = '__create__'

type AsyncStatus = 'idle' | 'loading' | 'done' | 'error'

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError')
  )
}

function isRich(option: SearchableSelectOption): boolean {
  return Boolean(option.subtitle || option.meta || (option.badges && option.badges.length > 0))
}

/**
 * Type-to-search combobox used for every entity/lookup dropdown in the app.
 * Filters on label + keywords (sync) or delegates to `onSearch` (async), supports full keyboard
 * navigation and an optional permission-gated inline-create row supplied by the caller.
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
  onSearch,
  searchMinChars = 0,
  searchDebounceMs = 250,
  selectedLabel,
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
  // Bridges the gap between creating/selecting an option and the parent knowing its label
  // (created options are not yet in `options`; async results are discarded on close).
  const [rememberedOption, setRememberedOption] = useState<SearchableSelectOption | null>(null)

  // ---- async search state -------------------------------------------------------------------
  const isAsync = typeof onSearch === 'function'
  const onSearchRef = useRef<SearchableSelectSearchFn | undefined>(onSearch)
  useEffect(() => {
    onSearchRef.current = onSearch
  })
  const [asyncOptions, setAsyncOptions] = useState<SearchableSelectOption[] | null>(null)
  const [asyncStatus, setAsyncStatus] = useState<AsyncStatus>('idle')
  // Count of the last completed search, announced to screen readers.
  const [resultCount, setResultCount] = useState<number | null>(null)
  const seqRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  // True once a request has been fired since the list opened: the first fires immediately,
  // later query changes are debounced.
  const firedSinceOpenRef = useRef(false)

  const trimmedQuery = query.trim()

  useEffect(() => {
    if (!isAsync || !open) return
    if (trimmedQuery.length < searchMinChars) return
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
            setResultCount(results.length)
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
  }, [isAsync, open, trimmedQuery, searchMinChars, searchDebounceMs])

  // Abort whatever is still in flight when the component unmounts and invalidate its sequence
  // so a non-abortable `onSearch` cannot resolve into state afterwards.
  useEffect(() => {
    const abort = abortRef
    const seq = seqRef
    return () => {
      abort.current?.abort()
      seq.current++
    }
  }, [])

  // Closes the list and reverts the input text; discards in-flight and shown search results.
  // Only stable setters/refs are touched, so the identity is constant.
  const close = useCallback(() => {
    setOpen(false)
    setQuery('')
    abortRef.current?.abort()
    abortRef.current = null
    seqRef.current++
    firedSinceOpenRef.current = false
    setAsyncOptions(null)
    setAsyncStatus('idle')
    setResultCount(null)
  }, [])

  // ---- option resolution --------------------------------------------------------------------
  const allOptions = useMemo(() => {
    // Only the CURRENT value needs bridging; once the parent moved on (or cleared the value,
    // as multi-select adders do) a remembered option must not sneak back into the list.
    if (
      !isAsync &&
      rememberedOption &&
      rememberedOption.value === value &&
      !options.some((o) => o.value === rememberedOption.value)
    ) {
      return [...options, rememberedOption]
    }
    return options
  }, [options, rememberedOption, isAsync, value])

  const selected = useMemo<SearchableSelectOption | null>(() => {
    if (value === null || value === '') return null
    return (
      allOptions.find((o) => o.value === value) ??
      asyncOptions?.find((o) => o.value === value) ??
      (rememberedOption?.value === value ? rememberedOption : undefined) ??
      (selectedLabel ? { value, label: selectedLabel } : null)
    )
  }, [value, allOptions, asyncOptions, rememberedOption, selectedLabel])

  const belowMinChars = isAsync && trimmedQuery.length < searchMinChars

  const filtered = useMemo(() => {
    if (isAsync) return belowMinChars ? [] : (asyncOptions ?? [])
    const q = trimmedQuery.toLowerCase()
    if (!q) return allOptions
    return allOptions.filter(
      (o) => o.label.toLowerCase().includes(q) || (o.keywords ?? '').toLowerCase().includes(q),
    )
  }, [isAsync, belowMinChars, asyncOptions, allOptions, trimmedQuery])

  const searching = isAsync && asyncStatus === 'loading'
  // Loading note only while nothing can be shown yet; later requests keep the previous rows.
  const showLoadingNote = Boolean(isLoading) || (searching && asyncOptions === null)
  const showErrorNote = isAsync && asyncStatus === 'error'
  const searchSettled = !isAsync || asyncStatus === 'done' || belowMinChars

  const createRowReady = Boolean(onCreate) && !showLoadingNote && !searching
  const showCreateRow =
    createRowReady &&
    (trimmedQuery.length > 0
      ? !filtered.some((o) => o.label.toLowerCase() === trimmedQuery.toLowerCase())
      : Boolean(onCreate?.alwaysShow))
  // Empty-query shortcut row ("+ Nieuwe …") vs. the query-driven "add \"query\"" row.
  const createRowLabel = !onCreate
    ? ''
    : trimmedQuery.length > 0
      ? `+ ${onCreate.label(trimmedQuery)}`
      : (onCreate.emptyQueryLabel ?? `+ ${onCreate.label('')}`)

  const rowCount = filtered.length + (showCreateRow ? 1 : 0)
  // Clamp instead of resetting through an effect: the option list can shrink while filtering.
  const activeIndex = rowCount === 0 ? 0 : Math.min(highlighted, rowCount - 1)

  // Close when clicking outside; revert the input text to the selected label.
  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        close()
      }
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [open, close])

  function openList() {
    if (disabled) return
    setOpen(true)
    setHighlighted(0)
  }

  function selectOption(option: SearchableSelectOption) {
    setRememberedOption(option)
    onChange(option.value)
    close()
  }

  async function runCreate() {
    if (!onCreate || creating) return
    setCreating(true)
    try {
      const created = await onCreate.create(trimmedQuery)
      if (created) {
        setRememberedOption(created)
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
    } else if (event.key === 'Home') {
      if (open && rowCount > 0) {
        event.preventDefault()
        setHighlighted(0)
      }
    } else if (event.key === 'End') {
      if (open && rowCount > 0) {
        event.preventDefault()
        setHighlighted(rowCount - 1)
      }
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

  function renderOptionContent(option: SearchableSelectOption) {
    if (!isRich(option)) {
      return (
        <>
          <span className="ui-searchable-select-option-label">{option.label}</span>
          {option.description && (
            <span className="ui-searchable-select-option-description">{option.description}</span>
          )}
        </>
      )
    }
    return (
      <>
        <span className="ui-searchable-select-option-title">
          <span className="ui-searchable-select-option-label">{option.title ?? option.label}</span>
          {option.badges?.map((badge) => (
            <span key={badge} className="ui-searchable-select-badge">
              {badge}
            </span>
          ))}
        </span>
        {option.subtitle && <span className="ui-searchable-select-option-subtitle">{option.subtitle}</span>}
        {option.meta && <span className="ui-searchable-select-option-meta">{option.meta}</span>}
      </>
    )
  }

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
      {isAsync && (
        <span className="ui-searchable-select-status" role="status" aria-live="polite">
          {open && resultCount !== null ? t('ui.select.resultCount', { count: resultCount }) : ''}
        </span>
      )}
      {open && (
        <ul className="ui-searchable-select-list" role="listbox" id={listboxId}>
          {showLoadingNote && <li className="ui-searchable-select-note">{t('ui.select.loading')}</li>}
          {!showLoadingNote &&
            filtered.map((option, index) => (
              <li
                key={option.value}
                id={`${baseId}-opt-${index}`}
                role="option"
                aria-selected={option.value === value}
                className={[
                  'ui-searchable-select-option',
                  isRich(option) ? 'is-rich' : '',
                  index === activeIndex ? 'is-highlighted' : '',
                  option.value === value ? 'is-selected' : '',
                ]
                  .filter(Boolean)
                  .join(' ')}
                onPointerDown={(e) => e.preventDefault()}
                onClick={() => activateRow(index)}
                onMouseEnter={() => setHighlighted(index)}
              >
                {renderOptionContent(option)}
              </li>
            ))}
          {showErrorNote && (
            <li className="ui-searchable-select-note ui-searchable-select-note-error">{t('ui.select.searchError')}</li>
          )}
          {!showLoadingNote && searchSettled && filtered.length === 0 && (!showCreateRow || trimmedQuery.length === 0) && (
            <li className="ui-searchable-select-note">{emptyMessage ?? t('ui.select.noResults')}</li>
          )}
          {showCreateRow && (
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
              {creating ? t('ui.select.adding') : createRowLabel}
            </li>
          )}
        </ul>
      )}
    </div>
  )
}
