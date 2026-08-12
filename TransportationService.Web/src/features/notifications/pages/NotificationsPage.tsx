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
import { formatDateTime } from '../../../utils/dates'
import './notifications.css'

function formatMoment(value: string): string {
  return formatDateTime(value)
}

export function NotificationsPage() {
  const navigate = useNavigate()
  const { showError, showSuccess } = useToast()
  const [notifications, setNotifications] = useState<Notification[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
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
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Meldingen konden niet worden geladen.')
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
        setReloadToken((t) => t + 1)
      }
    } catch {
      showError('De melding kon niet worden geopend.')
    }
  }

  async function archive(notification: Notification) {
    try {
      await archiveNotification(notification.id)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De melding kon niet worden gearchiveerd.')
    }
  }

  async function acknowledge(notification: Notification) {
    try {
      await acknowledgeNotification(notification.id)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De melding kon niet worden bevestigd.')
    }
  }

  async function markAll() {
    try {
      await markAllNotificationsRead()
      setReloadToken((t) => t + 1)
    } catch {
      showError('Meldingen konden niet worden gemarkeerd.')
    }
  }

  async function togglePreference(preference: NotificationPreference) {
    try {
      await setNotificationPreference(preference.category, !preference.enabled)
      setPreferences((current) =>
        current?.map((p) => (p.category === preference.category ? { ...p, enabled: !p.enabled } : p)) ?? null,
      )
      showSuccess('Voorkeur opgeslagen.')
    } catch {
      showError('De voorkeur kon niet worden opgeslagen.')
    }
  }

  const hasUnread = (notifications ?? []).some((n) => !n.isRead)
  const visibleNotifications = (notifications ?? []).filter((n) => !hideResolved || n.resolvedAt === null)

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Meldingen' }]} />
      <PageHeader
        title="Meldingen"
        action={
          hasUnread ? (
            <Button variant="secondary" onClick={() => void markAll()}>
              Alles gelezen
            </Button>
          ) : undefined
        }
      />

      <div className="ntf-filters">
        <select
          value={categoryFilter}
          onChange={(e) => setCategoryFilter(e.target.value as NotificationCategory | '')}
          aria-label="Categorie"
        >
          <option value="">Alle categorieën</option>
          {NOTIFICATION_CATEGORIES.map((category) => (
            <option key={category} value={category}>
              {NOTIFICATION_CATEGORY_LABELS[category]}
            </option>
          ))}
        </select>
        <label>
          <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />{' '}
          Archief tonen
        </label>
        <label>
          <input type="checkbox" checked={hideResolved} onChange={(e) => setHideResolved(e.target.checked)} />{' '}
          Opgeloste verbergen
        </label>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && notifications === null && <p className="placeholder-text">Meldingen laden…</p>}
      {!loadError && notifications !== null && visibleNotifications.length === 0 && (
        <p className="placeholder-text">Geen meldingen.</p>
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
                  {!notification.isRead && <span className="ntf-dot" aria-label="Ongelezen" />}
                  <span className={`ntf-severity ntf-severity-${notification.severity.toLowerCase()}`} aria-hidden="true">
                    {NOTIFICATION_SEVERITY_ICONS[notification.severity]}
                  </span>
                  {notification.title}
                  <Badge tone={notification.severity === 'Critical' ? 'danger' : 'neutral'}>
                    {NOTIFICATION_CATEGORY_LABELS[notification.category]}
                  </Badge>
                  {notification.resolvedAt !== null && <Badge tone="success">Opgelost</Badge>}
                  {notification.isArchived && <Badge tone="neutral">gearchiveerd</Badge>}
                </span>
                <span className="ntf-message">{notification.message}</span>
                {notification.requiresAcknowledgement && notification.acknowledgedAt !== null && (
                  <span className="ntf-ack-info">Bevestigd op {formatMoment(notification.acknowledgedAt)}</span>
                )}
                <span className="ntf-time">{formatMoment(notification.createdAt)}</span>
              </button>
              {notification.requiresAcknowledgement && notification.acknowledgedAt === null && (
                <button
                  type="button"
                  className="ntf-ack"
                  onClick={() => void acknowledge(notification)}
                  aria-label={`Melding "${notification.title}" bevestigen`}
                >
                  Bevestigen
                </button>
              )}
              {!notification.isArchived && (
                <button
                  type="button"
                  className="ntf-archive"
                  onClick={() => void archive(notification)}
                  aria-label={`Melding "${notification.title}" archiveren`}
                  title="Archiveren"
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
          <summary>Voorkeuren per categorie</summary>
          <p className="ntf-preferences-hint">
            Uitgeschakelde categorieën bezorgen geen meldingen meer; kritieke systeemmeldingen komen altijd door.
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
                  {NOTIFICATION_CATEGORY_LABELS[preference.category]}
                </label>
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  )
}
