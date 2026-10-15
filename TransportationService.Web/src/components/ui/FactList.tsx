import type { ReactNode } from 'react'
import './FactList.css'

export interface Fact {
  /** Stable key; defaults to the label when that is a string. */
  key?: string
  label: ReactNode
  /** null/undefined/'' render as a muted "—", so an empty field reads as empty, not missing. */
  value: ReactNode
  /** Spans the full width (notes, addresses) when the list has two columns. */
  wide?: boolean
}

interface FactListProps {
  facts: (Fact | null | false | undefined)[]
  /** Two columns on desktop (collapses to one on narrow screens). Default one. */
  columns?: 1 | 2
  className?: string
}

/**
 * Compact read-only facts: label left, value right, one line each — the read mode of a detail
 * page (B24 "compact facts layout", B15/B16/B17 overview cards). Replaces long single-column
 * definition lists where every value sat on its own indented line.
 */
export function FactList({ facts, columns = 1, className }: FactListProps) {
  const visible = facts.filter((fact): fact is Fact => Boolean(fact))
  return (
    <dl className={['ui-facts', columns === 2 ? 'ui-facts-2' : null, className].filter(Boolean).join(' ')}>
      {visible.map((fact, index) => {
        const empty = fact.value === null || fact.value === undefined || fact.value === ''
        return (
          <div key={fact.key ?? (typeof fact.label === 'string' ? fact.label : index)} className={fact.wide ? 'ui-fact ui-fact-wide' : 'ui-fact'}>
            <dt>{fact.label}</dt>
            <dd className={empty ? 'is-empty' : undefined}>{empty ? '—' : fact.value}</dd>
          </div>
        )
      })}
    </dl>
  )
}
