import { Badge, type BadgeTone } from './Badge'
import './StatusBadges.css'

export interface StatusBadgesProps {
  /** Administrative lifecycle state (in administratie actief of niet). */
  active: boolean
  activeLabels?: { active: string; inactive: string }
  /** Operational status (e.g. Beschikbaar / In onderhoud). Label must differ from the active label. */
  operational?: { label: string; tone: BadgeTone; reason?: string | null }
  blocked?: { isBlocked: boolean; reason?: string | null }
}

/**
 * The one status treatment used across customers, employees, drivers, vehicles,
 * trailers and lookups: a single administrative badge, an optional operational
 * badge, an optional blocked badge — never two identical "Actief" pills.
 */
export function StatusBadges({ active, activeLabels, operational, blocked }: StatusBadgesProps) {
  const labels = activeLabels ?? { active: 'Actief', inactive: 'Inactief' }
  const reason = blocked?.isBlocked ? blocked.reason : operational?.reason
  return (
    <span className="ui-status-badges">
      <Badge tone={active ? 'success' : 'neutral'}>{active ? labels.active : labels.inactive}</Badge>
      {operational && <Badge tone={operational.tone}>{operational.label}</Badge>}
      {blocked?.isBlocked && <Badge tone="danger">Geblokkeerd</Badge>}
      {reason && <span className="ui-status-badges-reason">— {reason}</span>}
    </span>
  )
}
