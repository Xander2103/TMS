import { useEffect, useState } from 'react'
import type { BadgeTone } from '../../components/ui/Badge'
import { HistoryTimeline, type HistoryItem } from '../../components/ui/HistoryTimeline'
import { useLocale } from '../../i18n/localeContext'
import {
  FLEET_HISTORY_FILTERS,
  getFleetHistory,
  matchesFleetHistoryFilter,
  type FleetAssetKind,
  type FleetHistoryEvent,
  type FleetHistoryFilter,
} from './fleetHistoryApi'
import './fleet-history.css'

const PAGE_SIZE = 25

const TONE: Record<string, BadgeTone> = {
  readiness: 'warning',
  status: 'info',
  damage: 'warning',
  maintenance: 'neutral',
  planning: 'neutral',
}

/**
 * FL-H3: the vehicle/trailer story on the shared history timeline — WHEN / WHO (or Systeem) / WHAT,
 * newest first. The server merges vehicle, documents, inspections, maintenance, damage, fuel,
 * leasing, driver links, trips and readiness moments into Dutch sentences; this panel only
 * renders and filters them. Amounts only arrive for fleet_finance.view holders.
 */
export function FleetHistoryPanel({ assetKind, assetId }: { assetKind: FleetAssetKind; assetId: string }) {
  const { t } = useLocale()
  const [loaded, setLoaded] = useState<{ key: string; events: FleetHistoryEvent[] | null; error: string | null } | null>(null)
  const [filter, setFilter] = useState<FleetHistoryFilter>('all')
  const [page, setPage] = useState(1)
  const key = `${assetKind}:${assetId}`

  useEffect(() => {
    const controller = new AbortController()
    getFleetHistory(assetKind, assetId, controller.signal)
      .then((events) => setLoaded({ key, events, error: null }))
      .catch(() => {
        if (!controller.signal.aborted) setLoaded({ key, events: null, error: t('fleet.history.loadFailed') })
      })
    return () => controller.abort()
  }, [assetKind, assetId, key, t])

  const current = loaded?.key === key ? loaded : null
  const events = current?.events ?? null
  const visible = (events ?? []).filter((event) => matchesFleetHistoryFilter(event, filter))
  const available = FLEET_HISTORY_FILTERS.filter(
    (code) => code === 'all' || (events ?? []).some((event) => matchesFleetHistoryFilter(event, code)),
  )
  const paged = visible.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  const items: HistoryItem[] | null = events
    ? paged.map((event) => ({
        id: event.id,
        timestamp: event.at,
        actorName: event.actorKind === 'System' ? t('fleet.history.system') : (event.actorName ?? t('fleet.history.unknownUser')),
        action: event.title,
        summary: event.title,
        detail: event.detail,
        category: { label: t(`fleet.history.categories.${event.category}`), tone: TONE[event.category] ?? 'neutral' },
        facts: event.technical ? [{ label: t('fleet.history.technical'), value: event.technical }] : [],
      }))
    : null

  return (
    <div className="fleet-history" data-testid="fleet-history">
      <HistoryTimeline
        items={current?.error ? [] : items}
        loading={!current}
        error={current?.error ?? null}
        emptyMessage={events && events.length > 0 ? t('fleet.history.emptyFiltered') : t('fleet.history.empty')}
        loadingMessage={t('fleet.history.loading')}
        categories={
          events && available.length > 2
            ? {
                label: t('fleet.history.filterLabel'),
                value: filter,
                options: available.map((code) => ({ value: code, label: t(`fleet.history.filters.${code}`) })),
                onChange: (value) => {
                  setFilter(value as FleetHistoryFilter)
                  setPage(1)
                },
              }
            : undefined
        }
        pagination={
          visible.length > PAGE_SIZE
            ? { page, pageSize: PAGE_SIZE, totalCount: visible.length, onPageChange: setPage }
            : undefined
        }
        // WHO only: the sentence itself (WHAT) is the summary line below.
        actorLine={(item) => item.actorName}
      />
    </div>
  )
}
