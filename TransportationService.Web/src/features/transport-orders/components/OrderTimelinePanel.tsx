import { useEffect, useState } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { EmptyState } from '../../../components/ui/EmptyState'
import { getTransportOrderTimeline, type OrderTimelineEvent } from '../api/transportOrdersApi'
import { formatDateTime } from '../../../utils/dates'

const CATEGORY_LABELS: Record<string, { label: string; tone: BadgeTone }> = {
  order: { label: 'Opdracht', tone: 'neutral' },
  status: { label: 'Status', tone: 'info' },
  package: { label: 'Collo', tone: 'success' },
  stop: { label: 'Stop', tone: 'warning' },
  invoice: { label: 'Factuur', tone: 'danger' },
}

function formatTimestamp(value: string): string {
  return formatDateTime(value)
}

/**
 * Chronological activity log of one order — who did what and when. Newest events last
 * (matching reading order); a filter keeps long histories navigable.
 */
export function OrderTimelinePanel({ orderId }: { orderId: string }) {
  const [events, setEvents] = useState<OrderTimelineEvent[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')

  useEffect(() => {
    let mounted = true
    getTransportOrderTimeline(orderId)
      .then((data) => {
        if (mounted) setEvents(data)
      })
      .catch(() => {
        if (mounted) setError('De historiek kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [orderId])

  if (error) return <ErrorState message={error} />
  if (events === null) return <LoadingState message="Historiek laden..." />
  if (events.length === 0) return <EmptyState message="Nog geen gebeurtenissen." />

  const term = filter.trim().toLowerCase()
  const visible = term
    ? events.filter((event) =>
        [event.title, event.detail ?? '', event.userName ?? ''].some((text) => text.toLowerCase().includes(term)),
      )
    : events

  return (
    <div className="to-timeline">
      {events.length > 10 && (
        <input
          type="search"
          className="to-timeline-filter"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder="Historiek filteren…"
          aria-label="Historiek filteren"
        />
      )}
      <ol className="to-timeline-list">
        {visible.map((event, index) => {
          const category = CATEGORY_LABELS[event.category] ?? { label: event.category, tone: 'neutral' as BadgeTone }
          return (
            <li key={`${event.timestamp}-${index}`} className="to-timeline-item">
              <span className="to-timeline-time">{formatTimestamp(event.timestamp)}</span>
              <Badge tone={category.tone}>{category.label}</Badge>
              <span className="to-timeline-body">
                {event.title}
                {event.detail && <span className="to-timeline-detail"> — {event.detail}</span>}
                {event.userName && <span className="to-timeline-user"> · {event.userName}</span>}
              </span>
            </li>
          )
        })}
        {visible.length === 0 && <li className="to-timeline-item">Geen gebeurtenissen voor deze filter.</li>}
      </ol>
    </div>
  )
}
