import { useEffect, useState } from 'react'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { useLocale } from '../../../i18n/localeContext'
import { getTransportOrderTimeline, type OrderTimelineEvent } from '../api/transportOrdersApi'
import { newestFirst } from './orderTimeline'

const CATEGORIES = ['all', 'order', 'status', 'package', 'stop', 'invoice'] as const

/**
 * The order's activity log on the shared timeline (B25): who did what and when, newest first,
 * with a category filter for long histories. The server merges audit, status, package, stop and
 * invoice events into Dutch sentences.
 */
export function OrderTimelinePanel({ orderId }: { orderId: string }) {
  const { t } = useLocale()
  const [events, setEvents] = useState<OrderTimelineEvent[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [category, setCategory] = useState<string>('all')

  useEffect(() => {
    let mounted = true
    getTransportOrderTimeline(orderId)
      .then((data) => {
        if (mounted) setEvents(newestFirst(data))
      })
      .catch(() => {
        if (mounted) setError(t('transportOrders.timeline.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [orderId, t])

  const visible = (events ?? []).filter((event) => category === 'all' || event.category === category)
  const items: HistoryItem[] | null = events
    ? visible.map((event, index) => ({
        id: `${event.timestamp}-${index}`,
        timestamp: event.timestamp,
        actorName: event.userName,
        action: t(`transportOrders.timeline.categories.${event.category}`),
        summary: event.title,
        detail: event.detail,
        category: null,
      }))
    : null
  const present = new Set((events ?? []).map((event) => event.category))

  return (
    <HistoryTimeline
      items={error ? [] : items}
      error={error}
      emptyMessage={t('transportOrders.timeline.empty')}
      categories={
        events && present.size > 1
          ? {
              label: t('transportOrders.timeline.filterLabel'),
              value: category,
              options: CATEGORIES.filter((code) => code === 'all' || present.has(code)).map((code) => ({
                value: code,
                label: t(`transportOrders.timeline.categories.${code}`),
              })),
              onChange: setCategory,
            }
          : undefined
      }
      actorLine={(item) =>
        item.actorName
          ? t('ui.history.actorLine', { action: item.action, name: item.actorName })
          : item.action
      }
    />
  )
}
