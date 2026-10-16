import { useEffect, useState } from 'react'
import type { BadgeTone } from '../../../components/ui/Badge'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { useLocale } from '../../../i18n/localeContext'
import { DOSSIER_TIMELINE_CATEGORIES, getDossierTimeline, type DossierTimelineEvent } from '../api/dossierTimelineApi'

const TONE: Record<string, BadgeTone> = {
  dossier: 'info',
  orders: 'neutral',
  planning: 'neutral',
  pricing: 'neutral',
  documents: 'neutral',
  incidents: 'warning',
}

/**
 * T-H6: the dossier's story on the shared history timeline — WHEN / WHO / WHAT, oldest first so it
 * reads like the flow it reconstructs (intake → planning → execution → confirmation). The server
 * merges dossier, order, trip, document, POD, incident and lifecycle events into Dutch sentences;
 * this panel only renders them. Readable for everyone who may open the dossier (dossiers.view).
 */
export function DossierTimelinePanel({ dossierId }: { dossierId: string }) {
  const { t } = useLocale()
  const [loaded, setLoaded] = useState<{ dossierId: string; events: DossierTimelineEvent[] | null; error: string | null } | null>(null)
  const [category, setCategory] = useState<string>('all')

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
  const visible = (events ?? []).filter((event) => category === 'all' || event.category === category)

  const actor = (event: DossierTimelineEvent) =>
    event.actorKind === 'System'
      ? t('dossiers.timeline.system')
      : (event.actorName ?? t('dossiers.timeline.unknownUser'))

  const items: HistoryItem[] | null = events
    ? visible.map((event) => ({
        id: event.id,
        timestamp: event.timestamp,
        actorName: actor(event),
        action: event.title,
        summary: event.title,
        detail: event.detail,
        category: { label: t(`dossiers.timeline.categories.${event.category}`), tone: TONE[event.category] ?? 'neutral' },
        changes: event.fields
          .filter((field) => field.before !== null)
          .map((field) => ({ field: field.label, before: field.before, after: field.value })),
        facts: event.fields
          .filter((field) => field.before === null && field.value !== null)
          .map((field) => ({ label: field.label, value: field.value as string })),
      }))
    : null

  return (
    <HistoryTimeline
      items={current?.error ? [] : items}
      loading={!current}
      error={current?.error ?? null}
      emptyMessage={events && events.length > 0 ? t('dossiers.timeline.emptyFiltered') : t('dossiers.timeline.empty')}
      loadingMessage={t('dossiers.timeline.loading')}
      categories={
        events && present.size > 1
          ? {
              label: t('dossiers.timeline.filterLabel'),
              value: category,
              options: ['all', ...DOSSIER_TIMELINE_CATEGORIES]
                .filter((code) => code === 'all' || present.has(code))
                .map((code) => ({ value: code, label: t(`dossiers.timeline.categories.${code}`) })),
              onChange: setCategory,
            }
          : undefined
      }
      // WHO only: the sentence itself (WHAT) is the summary line below.
      actorLine={(item) => item.actorName}
    />
  )
}
