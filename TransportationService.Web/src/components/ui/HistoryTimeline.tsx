import { useState, type ReactNode } from 'react'
import { useLocale } from '../../i18n/localeContext'
import { formatDateTime } from '../../utils/dates'
import { Badge, type BadgeTone } from './Badge'
import { Pagination } from './Pagination'
import './HistoryTimeline.css'

/**
 * B25 — ONE history presentation. Every event answers WHEN, WHO and WHAT before anything is
 * expanded; the field-level before/after sits behind "Uitklappen". Sources (audit log, order
 * timeline, customer/employee history) only adapt their data into `HistoryItem`s — nothing is
 * removed from the record, it is only made readable (resolved references, Dutch labels).
 */
export interface HistoryChange {
  field: string
  before: string | null
  after: string | null
}

export interface HistoryItem {
  id: string
  timestamp: string
  /** Acting person; null = an automatic/system event. */
  actorName: string | null
  /** The action in words ("Status gewijzigd", "Activiteit toegevoegd"). */
  action: string
  /** One readable line saying WHAT happened ("Status gewijzigd van Gepland naar Onderweg"). */
  summary?: string | null
  category?: { label: string; tone?: BadgeTone } | null
  /** Free-text reason/note recorded with the event. */
  detail?: string | null
  changes?: HistoryChange[]
  /** Plain facts behind "Uitklappen" (label → value), for events that are not an old → new change. */
  facts?: HistoryFact[]
}

export interface HistoryFact {
  label: string
  value: string
}

export interface HistoryCategoryFilter {
  options: { value: string; label: string }[]
  value: string
  onChange: (value: string) => void
  label: string
}

interface HistoryTimelineProps {
  items: HistoryItem[] | null
  loading?: boolean
  error?: string | null
  emptyMessage: string
  loadingMessage?: string
  /** "{action} door {name}"-style line; defaults to the shared wording. */
  actorLine?: (item: HistoryItem) => ReactNode
  categories?: HistoryCategoryFilter
  pagination?: { page: number; pageSize: number; totalCount: number; onPageChange: (page: number) => void }
  /** Overview cards: only the newest N, without filters or paging. */
  limit?: number
}

export function HistoryTimeline({
  items, loading = false, error = null, emptyMessage, loadingMessage, actorLine, categories, pagination, limit,
}: HistoryTimelineProps) {
  const { t } = useLocale()
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const toggle = (id: string) =>
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const shown = limit ? (items ?? []).slice(0, limit) : (items ?? [])
  const defaultActorLine = (item: HistoryItem) =>
    t('ui.history.actorLine', { action: item.action, name: item.actorName ?? t('ui.history.system') })

  return (
    <div className="ui-history">
      {categories && (
        <div className="ui-history-filters" role="group" aria-label={categories.label}>
          {categories.options.map((option) => (
            <button
              key={option.value}
              type="button"
              className="ui-history-chip"
              aria-pressed={categories.value === option.value}
              onClick={() => categories.onChange(option.value)}
            >
              {option.label}
            </button>
          ))}
        </div>
      )}

      {error && <p className="placeholder-text">{error}</p>}
      {!error && (loading || items === null) && <p className="placeholder-text">{loadingMessage ?? t('ui.history.loading')}</p>}
      {!error && !loading && items !== null && shown.length === 0 && <p className="placeholder-text">{emptyMessage}</p>}

      {!error && !loading && shown.length > 0 && (
        <ol className="ui-history-list">
          {shown.map((item) => {
            const changes = item.changes ?? []
            const facts = item.facts ?? []
            const expandable = changes.length > 0 || facts.length > 0
            const isOpen = expanded.has(item.id)
            return (
              <li key={item.id}>
                <article className="ui-history-item">
                  <header className="ui-history-head">
                    <time className="ui-history-when" dateTime={item.timestamp}>{formatDateTime(item.timestamp)}</time>
                    <span className="ui-history-actor">{(actorLine ?? defaultActorLine)(item)}</span>
                    {item.category && <Badge tone={item.category.tone ?? 'neutral'}>{item.category.label}</Badge>}
                  </header>
                  {item.summary && <p className="ui-history-summary">{item.summary}</p>}
                  {item.detail && <p className="ui-history-detail">{item.detail}</p>}
                  {expandable && !limit && (
                    <button type="button" className="ui-history-toggle" aria-expanded={isOpen} onClick={() => toggle(item.id)}>
                      {isOpen ? t('ui.history.collapse') : t('ui.history.expand')}
                    </button>
                  )}
                  {facts.length > 0 && isOpen && (
                    <dl className="ui-history-facts">
                      {facts.map((fact, index) => (
                        <div key={`${item.id}-fact-${index}`}>
                          <dt>{fact.label}</dt>
                          <dd>{fact.value}</dd>
                        </div>
                      ))}
                    </dl>
                  )}
                  {changes.length > 0 && isOpen && (
                    <table className="ui-history-table">
                      <thead>
                        <tr>
                          <th scope="col">{t('ui.history.field')}</th>
                          <th scope="col">{t('ui.history.before')}</th>
                          <th scope="col">{t('ui.history.after')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {changes.map((change, index) => (
                          <tr key={`${item.id}-${index}`}>
                            <td>{change.field}</td>
                            <td>{change.before ?? '—'}</td>
                            <td>{change.after ?? '—'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </article>
              </li>
            )
          })}
        </ol>
      )}

      {pagination && !error && <Pagination {...pagination} />}
    </div>
  )
}
