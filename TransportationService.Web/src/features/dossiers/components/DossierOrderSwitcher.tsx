import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity } from '../types'

interface DossierOrderSwitcherProps {
  /**
   * Route: the transport-shaped activities (hasStops), each owning (or about to own) an order.
   * Price: every billable unit — transport activities AND standalone ones (Opslag, Kraan, …).
   */
  activities: DossierActivity[]
  selectedActivityId: string
  onSelect: (activityId: string) => void
  /** True while the route editor holds unsaved changes: switching is locked so edits can never land on another order. */
  locked: boolean
  /** Accessible group name / caption; defaults to "Opdracht" (route). The price section passes "Eenheid". */
  label?: string
}

/**
 * Hardening 2026-09-10: a dossier can hold several transport orders, and the inline route /
 * price editors must never silently work on "the first one". When more than one target exists
 * this segmented control makes it explicit; both sections render it with the same selection.
 * Rendered as a toolbar of toggle buttons (aria-pressed) so screen readers announce the current
 * target. Stap 13: a standalone billable activity is labelled by its type name (it has no order
 * number) with the free label as its secondary line.
 */
export function DossierOrderSwitcher({ activities, selectedActivityId, onSelect, locked, label }: DossierOrderSwitcherProps) {
  const { t } = useLocale()
  if (activities.length < 2) return null
  const caption = label ?? t('dossierSheet.orderSwitch.label')
  return (
    <div className="dossier-order-switch">
      <div role="group" aria-label={caption} className="dossier-order-switch-group">
        <span className="dossier-order-switch-caption">{caption}:</span>
        {activities.map((activity) => {
          const selected = activity.id === selectedActivityId
          const primary = activity.hasStops
            ? (activity.linkedOrderNumber ?? t('dossierSheet.orderSwitch.noOrderYet'))
            : activity.activityTypeName
          const secondary = activity.hasStops ? (activity.label ?? activity.activityTypeName) : activity.label
          return (
            <button
              key={activity.id}
              type="button"
              className={`dossier-order-switch-button${selected ? ' is-selected' : ''}`}
              aria-pressed={selected}
              disabled={locked && !selected}
              onClick={() => {
                if (!selected) onSelect(activity.id)
              }}
            >
              <code>{primary}</code>
              {secondary && <span className="dossier-order-switch-secondary">{secondary}</span>}
            </button>
          )
        })}
      </div>
      {locked && <p className="dossier-order-switch-hint">{t('dossierSheet.orderSwitch.lockedWhileDirty')}</p>}
    </div>
  )
}
