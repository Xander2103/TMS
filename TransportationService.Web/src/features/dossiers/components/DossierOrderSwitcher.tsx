import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity } from '../types'

interface DossierOrderSwitcherProps {
  /** Transport-shaped activities (hasStops) in dossier order; each owns (or will own) a transport order. */
  activities: DossierActivity[]
  selectedActivityId: string
  onSelect: (activityId: string) => void
  /** True while the route editor holds unsaved changes: switching is locked so edits can never land on another order. */
  locked: boolean
}

/**
 * Hardening 2026-09-10: a dossier can hold several transport orders, and the inline route /
 * price editors must never silently work on "the first one". When more than one transport
 * activity exists this segmented control makes the target explicit; both sections render it
 * with the same selection. Rendered as a toolbar of toggle buttons (aria-pressed) so screen
 * readers announce the current target.
 */
export function DossierOrderSwitcher({ activities, selectedActivityId, onSelect, locked }: DossierOrderSwitcherProps) {
  const { t } = useLocale()
  if (activities.length < 2) return null
  return (
    <div className="dossier-order-switch">
      <div role="group" aria-label={t('dossierSheet.orderSwitch.label')} className="dossier-order-switch-group">
        <span className="dossier-order-switch-caption">{t('dossierSheet.orderSwitch.label')}:</span>
        {activities.map((activity) => {
          const selected = activity.id === selectedActivityId
          const primary = activity.linkedOrderNumber ?? t('dossierSheet.orderSwitch.noOrderYet')
          const secondary = activity.label ?? activity.activityTypeName
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
              <span className="dossier-order-switch-secondary">{secondary}</span>
            </button>
          )
        })}
      </div>
      {locked && <p className="dossier-order-switch-hint">{t('dossierSheet.orderSwitch.lockedWhileDirty')}</p>}
    </div>
  )
}
