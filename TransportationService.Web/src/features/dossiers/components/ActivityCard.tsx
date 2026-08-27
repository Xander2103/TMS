import { createElement } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, type TransportOrderStatus } from '../../transport-orders/types'
import { activityTypeIcon } from '../activityTypeIcons'
import { formatDate, formatDuration } from '../dossierDisplay'
import type { DossierActivity } from '../types'

interface ActivityCardProps {
  activity: DossierActivity
  /** All activities of the dossier, to resolve the accompaniment label. */
  activities: DossierActivity[]
  /** hasStops + linked order → navigate; anything else → drawer. */
  onOpen: (activity: DossierActivity) => void
}

/** §11 activity card: icon, type + label, contextual line, one [Openen] action. */
export function ActivityCard({ activity, activities, onOpen }: ActivityCardProps) {
  const { t } = useLocale()
  const status = activity.linkedOrderStatus as TransportOrderStatus | null
  const linkedTo = activity.linkedActivityId
    ? activities.find((a) => a.id === activity.linkedActivityId)
    : undefined

  const contextParts: string[] = []
  if (activity.plannedDate) contextParts.push(formatDate(activity.plannedDate))
  if (activity.durationHours != null) contextParts.push(formatDuration(activity.durationHours))
  if (linkedTo) contextParts.push(t('dossiers.card.linkedTo', { name: linkedTo.label ?? linkedTo.activityTypeName }))
  if (activity.hasStops && !activity.linkedTransportOrderId) contextParts.push(t('dossiers.card.noOrder'))

  return (
    <li className="dossier-activity-card">
      <div className="dossier-activity-main">
        <span className="dossier-activity-title">
          {createElement(activityTypeIcon(activity.icon), { size: 18, 'aria-hidden': true })}
          <strong>{activity.activityTypeName}</strong>
          {activity.label && <span className="dossier-activity-label">{activity.label}</span>}
          {activity.linkedOrderNumber && status && (
            <>
              <code>{activity.linkedOrderNumber}</code>
              <Badge tone={ORDER_STATUS_TONE[status] ?? 'neutral'}>
                {ORDER_STATUS_LABELS[status] ? t(ORDER_STATUS_LABELS[status]) : status}
              </Badge>
            </>
          )}
        </span>
        {contextParts.length > 0 && <span className="dossier-activity-context">{contextParts.join(' · ')}</span>}
      </div>
      <Button variant="secondary" onClick={() => onOpen(activity)}>
        {t('dossiers.card.open')}
      </Button>
    </li>
  )
}
