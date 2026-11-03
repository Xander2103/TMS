import { useEffect, useState } from 'react'
import { History } from 'lucide-react'
import type { BadgeTone } from '../../../components/ui/Badge'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { useLocale } from '../../../i18n/localeContext'
import { DOSSIER_TIMELINE_CATEGORIES, getDossierTimeline, type DossierTimelineEvent } from '../api/dossierTimelineApi'
import { HistoryCardHeader, HistoryListFooter, HistoryListToolbar } from './HistoryListControls'
import { matchesSearch, sortByMoment, useListPaging, type HistorySort } from './historyListState'

const TONE: Record<string, BadgeTone> = {
  dossier: 'info',
  orders: 'neutral',
  planning: 'neutral',
  pricing: 'neutral',
  documents: 'neutral',
  incidents: 'warning',
}

/**
 * T-H6: the dossier's story on the shared history timeline — WHEN / WHO / WHAT. The server merges
 * dossier, order, trip, document, POD, incident and lifecycle events into Dutch sentences and
 * returns the complete list, so searching, the type filter, sorting and paging all happen here.
 * Readable for everyone who may open the dossier (dossiers.view).
 */
export function DossierTimelinePanel({ dossierId, title }: { dossierId: string; title: string }) {
  const { t } = useLocale()
  const [loaded, setLoaded] = useState<{ dossierId: string; events: DossierTimelineEvent[] | null; error: string | null } | null>(null)
  const [category, setCategory] = useState<string>('all')
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<HistorySort>('newest')

  useEffect(() => {
    const controller = new AbortController()
    getDossierTimeline(dossierId, controller.signal)
      .then((events) => setLoaded({ dossierId, events, error: null }))
      .catch(() => {
        if (!controller.signal.aborted) setLoaded({ dossierId, events: null, error: t('dossiers.timeline.loadFailed') })
      })
    return () => controller.abort()
  }, [dossierId, t])

  const current = loaded?.dossierId === dossierId ? loaded : null
  const events = current?.events ?? null
  const present = new Set((events ?? []).map((event) => event.category))

  const actor = (event: DossierTimelineEvent) =>
    event.actorKind === 'System'
      ? t('dossiers.timeline.system')
      : (event.actorName ?? t('dossiers.timeline.unknownUser'))
  const categoryLabel = (code: string) => t(`dossiers.timeline.categories.${code}`)

  const matching = (events ?? []).filter(
    (event) =>
      (category === 'all' || event.category === category) &&
      matchesSearch(search, [
        event.title,
        event.detail,
        actor(event),
        categoryLabel(event.category),
        ...event.fields.flatMap((field) => [field.label, field.value, field.before]),
      ]),
  )
  const paging = useListPaging(matching.length)
  const shown = sortByMoment(matching, (event) => event.timestamp, sort).slice(Math.max(paging.start - 1, 0), paging.end)

  const items: HistoryItem[] | null = events
    ? shown.map((event) => ({
        id: event.id,
        timestamp: event.timestamp,
        actorName: actor(event),
        action: event.title,
        summary: event.title,
        detail: event.detail,
        category: { label: categoryLabel(event.category), tone: TONE[event.category] ?? 'neutral' },
        changes: event.fields
          .filter((field) => field.before !== null)
          .map((field) => ({ field: field.label, before: field.before, after: field.value })),
        facts: event.fields
          .filter((field) => field.before === null && field.value !== null)
          .map((field) => ({ label: field.label, value: field.value as string })),
      }))
    : null

  return (
    <div className="dossier-history-card">
      <HistoryCardHeader icon={<History size={20} />} title={title} subtitle={t('dossiers.timeline.subtitle')} />
      {events && (
        <HistoryListToolbar
          search={search}
          onSearch={(value) => {
            setSearch(value)
            paging.setPage(1)
          }}
          searchPlaceholder={t('dossiers.timeline.searchPlaceholder')}
          sort={sort}
          onSort={(value) => {
            setSort(value)
            paging.setPage(1)
          }}
          filter={
            <label className="dossier-history-select">
              <select
                value={category}
                aria-label={t('dossiers.timeline.filterLabel')}
                onChange={(event) => {
                  setCategory(event.target.value)
                  paging.setPage(1)
                }}
              >
                {['all', ...DOSSIER_TIMELINE_CATEGORIES]
                  .filter((code) => code === 'all' || present.has(code))
                  .map((code) => (
                    <option key={code} value={code}>
                      {t('dossiers.timeline.typeOption', { type: categoryLabel(code) })}
                    </option>
                  ))}
              </select>
            </label>
          }
        />
      )}
      <div className="dossier-history-scroll" role="region" aria-label={title} tabIndex={0}>
        <HistoryTimeline
          items={current?.error ? [] : items}
          loading={!current}
          error={current?.error ?? null}
          emptyMessage={events && events.length > 0 ? t('dossiers.timeline.emptyFiltered') : t('dossiers.timeline.empty')}
          loadingMessage={t('dossiers.timeline.loading')}
          // WHO only: the sentence itself (WHAT) is the summary line below.
          actorLine={(item) => item.actorName}
        />
      </div>
      {matching.length > 0 && (
        <HistoryListFooter
          summary={t('dossiers.timeline.range', { start: paging.start, end: paging.end, count: matching.length })}
          paging={paging}
        />
      )}
    </div>
  )
}
