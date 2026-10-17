import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { completeMyActivity, listMyActivities, startMyActivity } from '../api/myTripsApi'
import type { MyActivity, MyActivityStatus } from '../types'
import '../pages/my-trips.css'

const STATUS_TONE: Record<MyActivityStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Planned: 'info',
  InProgress: 'warning',
  Completed: 'success',
  Cancelled: 'danger',
}

/** "HH:mm" from a local wall-clock time or date-time; these carry no time zone, so no projection. */
function hhmm(value: string | null): string | null {
  const match = value?.match(/(\d{2}):(\d{2})/)
  return match ? `${match[1]}:${match[2]}` : null
}

/**
 * T-H7 driver app: standalone work assigned to me (crane work on site, plateau …). The operator
 * sees the site and the work description and starts / completes the job; prices never show.
 * Hidden entirely when there is nothing to do, so a pure transport driver sees no extra block.
 */
export function MyActivitiesSection() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [activities, setActivities] = useState<MyActivity[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<MyActivity | null>(null)

  useEffect(() => {
    let mounted = true
    listMyActivities()
      .then((data) => {
        if (mounted) setActivities(data)
      })
      .catch(() => {
        if (mounted) setLoadError(t('myTrips.activities.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [t])

  async function run(activity: MyActivity, action: 'start' | 'complete') {
    setBusyId(activity.id)
    try {
      const updated = await (action === 'start' ? startMyActivity(activity.id) : completeMyActivity(activity.id))
      setActivities((current) => current?.map((a) => (a.id === updated.id ? updated : a)) ?? null)
      showSuccess(t(action === 'start' ? 'myTrips.activities.started' : 'myTrips.activities.completed'))
      setConfirming(null)
    } catch (err) {
      showError(describeApiError(err, t('myTrips.activities.actionFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!activities || activities.length === 0) return null

  return (
    <section className="mt-activities" aria-labelledby="mt-activities-title">
      <h2 id="mt-activities-title" className="mt-section-title">{t('myTrips.activities.title')}</h2>
      <p className="mt-section-subtitle">{t('myTrips.activities.subtitle')}</p>
      <div className="mt-cards">
        {activities.map((activity) => {
          const title = activity.label || activity.typeName
          const start = hhmm(activity.startTime)
          const end = hhmm(activity.plannedEnd)
          const open = activity.executionStatus === 'Planned' || activity.executionStatus === 'InProgress'
          return (
            <article key={activity.id} className="mt-card mt-activity-card" aria-label={title}>
              <div className="mt-card-head">
                <strong>{title}</strong>
                <Badge tone={STATUS_TONE[activity.executionStatus]}>
                  {t(`myTrips.activities.status.${activity.executionStatus}`)}
                </Badge>
              </div>
              <div className="mt-card-date">
                {activity.plannedDate ? formatDate(activity.plannedDate) : '—'}
                {start ? ` · ${end ? t('myTrips.activities.time', { start, end }) : start}` : ''}
              </div>
              <div className="mt-card-meta">
                {activity.typeName} · <code>{activity.dossierNumber}</code>
                {activity.customerName ? ` · ${activity.customerName}` : ''}
              </div>
              <dl className="mt-activity-facts">
                {activity.siteAddress && (
                  <>
                    <dt>{t('myTrips.activities.site')}</dt>
                    <dd>{activity.siteAddress}</dd>
                  </>
                )}
                {activity.vehicleNumber && (
                  <>
                    <dt>{t('myTrips.activities.vehicle')}</dt>
                    <dd>{`${activity.vehicleNumber}${activity.vehiclePlate ? ` (${activity.vehiclePlate})` : ''}`}</dd>
                  </>
                )}
                {activity.workDescription && (
                  <>
                    <dt>{t('myTrips.activities.work')}</dt>
                    <dd className="mt-activity-work">{activity.workDescription}</dd>
                  </>
                )}
              </dl>
              {open && (
                <div className="mt-activity-actions">
                  {activity.executionStatus === 'Planned' && (
                    <Button variant="secondary" disabled={busyId === activity.id} onClick={() => void run(activity, 'start')}>
                      {t('myTrips.activities.start')}
                    </Button>
                  )}
                  <Button disabled={busyId === activity.id} onClick={() => setConfirming(activity)}>
                    {t('myTrips.activities.complete')}
                  </Button>
                </div>
              )}
            </article>
          )
        })}
      </div>
      {confirming && (
        <ConfirmDialog
          title={t('myTrips.activities.completeTitle')}
          message={t('myTrips.activities.completeMessage', { label: confirming.label || confirming.typeName })}
          confirmLabel={t('myTrips.activities.complete')}
          busy={busyId === confirming.id}
          onConfirm={() => void run(confirming, 'complete')}
          onCancel={() => setConfirming(null)}
        />
      )}
    </section>
  )
}
