import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import {
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type Notification,
} from '../api/notificationsApi'
import './notifications.css'

function formatMoment(value: string): string {
  return value.slice(0, 16).replace('T', ' ')
}

export function NotificationsPage() {
  const navigate = useNavigate()
  const { showError } = useToast()
  const [notifications, setNotifications] = useState<Notification[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let mounted = true
    listNotifications()
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
  }, [reloadToken])

  async function open(notification: Notification) {
    try {
      if (!notification.isRead) {
        await markNotificationRead(notification.id)
      }
      if (notification.linkPath) {
        navigate(notification.linkPath)
      } else {
        setReloadToken((t) => t + 1)
      }
    } catch {
      showError('De melding kon niet worden geopend.')
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

  const hasUnread = (notifications ?? []).some((n) => !n.isRead)

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

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && notifications === null && <p className="placeholder-text">Meldingen laden…</p>}
      {!loadError && notifications !== null && notifications.length === 0 && (
        <p className="placeholder-text">Geen meldingen.</p>
      )}

      {!loadError && notifications !== null && notifications.length > 0 && (
        <ul className="ntf-list">
          {notifications.map((notification) => (
            <li key={notification.id}>
              <button
                type="button"
                className={notification.isRead ? 'ntf-item' : 'ntf-item ntf-unread'}
                onClick={() => void open(notification)}
              >
                <span className="ntf-title">
                  {!notification.isRead && <span className="ntf-dot" aria-label="Ongelezen" />}
                  {notification.title}
                </span>
                <span className="ntf-message">{notification.message}</span>
                <span className="ntf-time">{formatMoment(notification.createdAt)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
