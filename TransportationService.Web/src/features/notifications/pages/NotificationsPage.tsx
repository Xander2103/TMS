import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import {
  NOTIFICATION_CATEGORIES,
  NOTIFICATION_CATEGORY_LABELS,
  NOTIFICATION_SEVERITY_ICONS,
  acknowledgeNotification,
  archiveNotification,
  getNotificationPreferences,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  setNotificationPreference,
  type Notification,
  type NotificationCategory,
  type NotificationPreference,
} from '../api/notificationsApi'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import './notifications.css'

function formatMoment(value: string): string {
  return formatDateTime(value)
}

export function NotificationsPage() {
  const navigate = useNavigate()
  const { showError, showSuccess } = useToast()
  const { t } = useLocale()
  const [notifications, setNotifications] = useState<Notification[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)
  const [categoryFilter, setCategoryFilter] = useState<NotificationCategory | ''>('')
  const [includeArchived, setIncludeArchived] = useState(false)
  // Opgeloste meldingen zijn ruis voor de dagelijkse werklijst; standaard verborgen (client-side).
  const [hideResolved, setHideResolved] = useState(true)
  const [preferences, setPreferences] = useState<NotificationPreference[] | null>(null)

  useEffect(() => {
    let mounted = true
    listNotifications({
      category: categoryFilter || undefined,
      includeArchived,
      take: 50,
    })
      .then((data) => {
        if (!mounted) return
        setNotifications(data)
        setLoadError(false)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [reloadToken, categoryFilter, includeArchived])

  useEffect(() => {
    let mounted = true
    getNotificationPreferences()
      .then((data) => {
        if (mounted) setPreferences(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  async function open(notification: Notification) {
    try {
      if (!notification.isRead) {
        await markNotificationRead(notification.id)
      }
      if (notification.linkPath) {
        // Producer dedupe markers ride as a #fragment; strip before navigating.
        navigate(notification.linkPath.split('#')[0])
      } else {
        setReloadToken((token) => token + 1)
      }
    } catch {
      showError(t('notificationCenter.errors.openFailed'))
    }
  }

  async function archive(notification: Notification) {
    try {
      await archiveNotification(notification.id)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('notificationCenter.errors.archiveFailed'))
    }
  }

  async function acknowledge(notification: Notification) {
    try {
      await acknowledgeNotification(notification.id)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('notificationCenter.errors.acknowledgeFailed'))
    }
  }

  async function markAll() {
    try {
      await markAllNotificationsRead()
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('notificationCenter.errors.markFailed'))
    }
  }

  async function togglePreference(preference: NotificationPreference) {
    try {
      await setNotificationPreference(preference.category, !preference.enabled)
      setPreferences((current) =>
        current?.map((p) => (p.category === preference.category ? { ...p, enabled: !p.enabled } : p)) ?? null,
      )
      showSuccess(t('notificationCenter.toasts.preferenceSaved'))
    } catch {
      showError(t('notificationCenter.errors.preferenceFailed'))
    }
  }

  const hasUnread = (notifications ?? []).some((n) => !n.isRead)
  const visibleNotifications = (notifications ?? []).filter((n) => !hideResolved || n.resolvedAt === null)

  return (
    <div>
      <Breadcrumbs items={[{ label: t('notificationCenter.page.title') }]} />
      <PageHeader
        title={t('notificationCenter.page.title')}
        action={
          hasUnread ? (
            <Button variant="secondary" onClick={() => void markAll()}>
              {t('notificationCenter.actions.markAllRead')}
            </Button>
          ) : undefined
        }
      />

      <div className="ntf-filters">
        <select
          value={categoryFilter}
          onChange={(e) => setCategoryFilter(e.target.value as NotificationCategory | '')}
          aria-label={t('notificationCenter.page.categoryAria')}
        >
          <option value="">{t('notificationCenter.page.allCategories')}</option>
          {NOTIFICATION_CATEGORIES.map((category) => (
            <option key={category} value={category}>
              {t(NOTIFICATION_CATEGORY_LABELS[category])}
            </option>
          ))}
        </select>
        <label>
          <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />{' '}
          {t('notificationCenter.page.showArchive')}
        </label>
        <label>
          <input type="checkbox" checked={hideResolved} onChange={(e) => setHideResolved(e.target.checked)} />{' '}
          {t('notificationCenter.page.hideResolved')}
        </label>
      </div>

      {loadError && <p className="placeholder-text">{t('notificationCenter.errors.loadFailed')}</p>}
      {!loadError && notifications === null && <p className="placeholder-text">{t('notificationCenter.page.loading')}</p>}
      {!loadError && notifications !== null && visibleNotifications.length === 0 && (
        <p className="placeholder-text">{t('notificationCenter.page.empty')}</p>
      )}

      {!loadError && notifications !== null && visibleNotifications.length > 0 && (
        <ul className="ntf-list">
          {visibleNotifications.map((notification) => (
            <li key={notification.id} className="ntf-row">
              <button
                type="button"
                className={[
                  'ntf-item',
                  !notification.isRead && 'ntf-unread',
                  notification.resolvedAt !== null && 'ntf-resolved',
                ]
                  .filter(Boolean)
                  .join(' ')}
                onClick={() => void open(notification)}
              >
                <span className="ntf-title">
                  {!notification.isRead && <span className="ntf-dot" aria-label={t('notificationCenter.bell.unreadDot')} />}
                  <span className={`ntf-severity ntf-severity-${notification.severity.toLowerCase()}`} aria-hidden="true">
                    {NOTIFICATION_SEVERITY_ICONS[notification.severity]}
                  </span>
                  {notification.title}
                  <Badge tone={notification.severity === 'Critical' ? 'danger' : 'neutral'}>
                    {t(NOTIFICATION_CATEGORY_LABELS[notification.category])}
                  </Badge>
                  {notification.resolvedAt !== null && <Badge tone="success">{t('notificationCenter.page.resolved')}</Badge>}
                  {notification.isArchived && <Badge tone="neutral">{t('notificationCenter.page.archived')}</Badge>}
                </span>
                <span className="ntf-message">{notification.message}</span>
                {notification.requiresAcknowledgement && notification.acknowledgedAt !== null && (
                  <span className="ntf-ack-info">{t('notificationCenter.page.acknowledgedAt', { dateTime: formatMoment(notification.acknowledgedAt) })}</span>
                )}
                <span className="ntf-time">{formatMoment(notification.createdAt)}</span>
              </button>
              {notification.requiresAcknowledgement && notification.acknowledgedAt === null && (
                <button
                  type="button"
                  className="ntf-ack"
                  onClick={() => void acknowledge(notification)}
                  aria-label={t('notificationCenter.page.acknowledgeAria', { title: notification.title })}
                >
                  {t('notificationCenter.page.acknowledge')}
                </button>
              )}
              {!notification.isArchived && (
                <button
                  type="button"
                  className="ntf-archive"
                  onClick={() => void archive(notification)}
                  aria-label={t('notificationCenter.page.archiveAria', { title: notification.title })}
                  title={t('notificationCenter.page.archiveTitle')}
                >
                  🗄
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {preferences && (
        <details className="ntf-preferences">
          <summary>{t('notificationCenter.page.preferencesSummary')}</summary>
          <p className="ntf-preferences-hint">
            {t('notificationCenter.page.preferencesHint')}
          </p>
          <ul>
            {preferences.map((preference) => (
              <li key={preference.category}>
                <label>
                  <input
                    type="checkbox"
                    checked={preference.enabled}
                    onChange={() => void togglePreference(preference)}
                  />{' '}
                  {t(NOTIFICATION_CATEGORY_LABELS[preference.category])}
                </label>
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  )
}
