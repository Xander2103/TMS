import { createElement } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, type TransportOrderStatus } from '../../transport-orders/types'
import { PRICE_STATUS_LABEL_KEYS, PRICE_STATUS_TONE, activityPriceStatus, activityPriceText } from '../activityDisplay'
import { activityTypeIcon } from '../activityTypeIcons'
import { ActivityDocumentStatus } from '../documents/ActivityDocumentStatus'
import { formatDate, formatDuration } from '../dossierDisplay'
import { NotePreview } from '../notes/NotePreview'
import type { DossierActivity, DossierDetail } from '../types'
import './activity-card.css'

/** Which part of the activity detail a card action opens. */
export type ActivityDetailFocus = 'planning' | 'notes'

interface ActivityCardProps {
  activity: DossierActivity
  /** All activities of the dossier, to resolve the accompaniment label. */
  activities: DossierActivity[]
  /** The dossier: its number, and the price reading shared with the Verkoop & prijs tab. */
  dossier: DossierDetail
  /** hasStops + linked order → navigate; anything else → drawer. */
  onOpen: (activity: DossierActivity) => void
  /** Opens the activity detail on its planning block or its notes. */
  onOpenDetail?: (activity: DossierActivity, focus: ActivityDetailFocus) => void
  /** Opens the Documenten tab on the activity's order. */
  onOpenDocuments?: (activity: DossierActivity) => void
  /** Attention jump target: drawn with the accent outline. */
  highlighted?: boolean
}

/**
 * §16 activity card: header (icon, type, number, status, [Openen]) + a compact info grid. Driver,
 * vehicle, document and price come straight from the DTO; whatever is missing is shown as missing
 * — never a made-up number, never € 0,00 for an absent price.
 */
export function ActivityCard({ activity, activities, dossier, onOpen, onOpenDetail, onOpenDocuments, highlighted = false }: ActivityCardProps) {
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

  const assignment = activity.assignment ?? null
  const priceStatus = activityPriceStatus(dossier, activity)
  const priceText = activityPriceText(t, dossier, activity)
  const canPlan = Boolean(activity.linkedTransportOrderId) && onOpenDetail !== undefined

  return (
    <li
      className={`dossier-activity-card${highlighted ? ' is-highlighted' : ''}`}
      data-activity-id={activity.id}
      aria-label={activity.linkedOrderNumber ?? activity.label ?? activity.activityTypeName}
    >
      <div className="dossier-activity-head">
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
        <span className="dossier-activity-actions">
          {canPlan && (
            <button type="button" className="link-button" onClick={() => onOpenDetail!(activity, 'planning')}>
              {t('dossierActivities.card.planning')}
            </button>
          )}
          <Button variant="secondary" onClick={() => onOpen(activity)}>
            {t('dossiers.card.open')}
          </Button>
        </span>
      </div>

      <dl className="dossier-activity-info">
        <div>
          <dt>{t('dossierActivities.card.dossier')}</dt>
          <dd>{dossier.dossierNumber}</dd>
        </div>
        <div>
          <dt>{t('dossierActivities.card.document')}</dt>
          <dd>
            <ActivityDocumentStatus
              issued={activity.issuedDocuments ?? []}
              documentCount={activity.documentCount ?? 0}
              onOpen={activity.linkedTransportOrderId && onOpenDocuments ? () => onOpenDocuments(activity) : undefined}
            />
          </dd>
        </div>
        <div>
          <dt>{t('dossierActivities.card.driver')}</dt>
          <dd className={assignment?.driverName ? undefined : 'is-missing'}>
            {assignment?.driverName ?? t('dossierActivities.card.notAssigned')}
          </dd>
        </div>
        <div>
          <dt>{t('dossierActivities.card.plate')}</dt>
          <dd className={assignment?.vehiclePlate ? undefined : 'is-missing'} title={assignment?.vehicleNumber ?? undefined}>
            {assignment?.vehiclePlate ?? '—'}
            {/* A proposal from the fixed driver-vehicle link is not a definitive assignment. */}
            {assignment?.vehicleId && assignment.vehicleSelectionSource === 'Suggested' && (
              <Badge tone="info">{t('dossierActivities.card.suggested')}</Badge>
            )}
          </dd>
        </div>
        <div>
          <dt>{t('dossierActivities.card.price')}</dt>
          <dd className={priceStatus === 'NotPriced' ? 'is-missing' : undefined}>
            {priceText ?? t('dossierActivities.price.notBillable')}
          </dd>
        </div>
        <div>
          <dt>{t('dossierActivities.card.priceStatus')}</dt>
          <dd>{priceStatus ? <Badge tone={PRICE_STATUS_TONE[priceStatus]}>{t(PRICE_STATUS_LABEL_KEYS[priceStatus])}</Badge> : '—'}</dd>
        </div>
      </dl>

      {assignment && assignment.otherOrderCount > 0 && (
        <p className="dossier-activity-shared">
          {t('dossierActivities.card.sharedTrip', { tripNumber: assignment.tripNumber, count: assignment.otherOrderCount })}
        </p>
      )}

      <NotePreview
        preview={activity.latestNotePreview ?? null}
        count={activity.noteCount ?? 0}
        onOpen={onOpenDetail ? () => onOpenDetail(activity, 'notes') : undefined}
      />
    </li>
  )
}
