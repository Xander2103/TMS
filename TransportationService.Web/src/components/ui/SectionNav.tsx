import { useRef } from 'react'

export interface SectionNavItem {
  id: string
  label: string
  optional?: boolean
  hasError?: boolean
  complete?: boolean
  /**
   * Optional third state between `complete` and untouched: the section holds (optional)
   * data without being "af". `true` renders a ● indicator, an explicit `false` on an
   * optional section renders a subtle ○ ("leeg"). Leave undefined (existing callers) for
   * the classic two-state behaviour — nothing changes for them.
   */
  filled?: boolean
}

interface SectionNavProps {
  items: SectionNavItem[]
  activeId: string
  onActiveChange: (id: string) => void
  /** 'vertical' renders the sticky left rail used by {@link SectionedForm}'s `orientation="left"`. */
  orientation?: 'horizontal' | 'vertical'
}

/** Desktop ARIA tablist for {@link SectionedForm}: scrollable, roving-tabindex, arrow-key nav. */
export function SectionNav({ items, activeId, onActiveChange, orientation = 'horizontal' }: SectionNavProps) {
  const refs = useRef<Record<string, HTMLButtonElement | null>>({})
  const vertical = orientation === 'vertical'

  function onKeyDown(e: React.KeyboardEvent, index: number) {
    let next: number
    const nextKey = vertical ? 'ArrowDown' : 'ArrowRight'
    const prevKey = vertical ? 'ArrowUp' : 'ArrowLeft'
    if (e.key === nextKey) next = (index + 1) % items.length
    else if (e.key === prevKey) next = (index - 1 + items.length) % items.length
    else if (e.key === 'Home') next = 0
    else if (e.key === 'End') next = items.length - 1
    else return
    e.preventDefault()
    const target = items[next]
    onActiveChange(target.id)
    refs.current[target.id]?.focus()
  }

  return (
    <div
      className="ui-section-nav"
      role="tablist"
      aria-label="Formuliersecties"
      aria-orientation={vertical ? 'vertical' : undefined}
    >
      {items.map((item, index) => {
        const selected = item.id === activeId
        return (
          <button
            key={item.id}
            ref={(el) => { refs.current[item.id] = el }}
            type="button"
            role="tab"
            id={`section-tab-${item.id}`}
            aria-selected={selected}
            aria-controls={`section-panel-${item.id}`}
            tabIndex={selected ? 0 : -1}
            data-has-error={item.hasError ? 'true' : undefined}
            data-complete={item.complete ? 'true' : undefined}
            {...(item.optional ? {} : { 'data-required': 'true' })}
            className="ui-section-tab"
            onClick={() => onActiveChange(item.id)}
            onKeyDown={(e) => onKeyDown(e, index)}
          >
            <span className="ui-section-tab-label">{item.label}</span>
            {item.hasError && <span className="ui-section-tab-error" aria-label="bevat fouten">!</span>}
            {!item.hasError && item.complete && (
              <span className="ui-section-tab-complete" aria-hidden="true">✓</span>
            )}
            {!item.hasError && !item.complete && item.filled === true && (
              <span className="ui-section-tab-filled" aria-label="bevat gegevens" title="Bevat gegevens">●</span>
            )}
            {!item.hasError && !item.complete && item.filled === false && item.optional && (
              <span className="ui-section-tab-empty" aria-label="leeg (optioneel)" title="Leeg (optioneel)">○</span>
            )}
          </button>
        )
      })}
    </div>
  )
}
