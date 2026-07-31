import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Bell } from 'lucide-react'
import {
  NOTIFICATION_SEVERITY_ICONS,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type Notification,
} from '../api/notificationsApi'
import { useUnreadNotifications } from '../notificationsContextValue'
import { formatRelativeTime } from '../relativeTime'
import './NotificationBell.css'

const DROPDOWN_TAKE = 8

/**
 * Bel rechtsboven in de interne shell: badge met ongelezen-teller (gedeeld via
 * NotificationsProvider) en een dropdown met de recentste meldingen.
 */
export function NotificationBell() {
  const navigate = useNavigate()
  const { unreadCount, refresh } = useUnreadNotifications()
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<Notification[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)

  // De lijst wordt pas bij openen opgehaald; de badge zelf drijft op de gedeelde poll.
  useEffect(() => {
    if (!open) return
    let mounted = true
    listNotifications({ take: DROPDOWN_TAKE })
      .then((data) => {
        if (mounted) setItems(data)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [open])

  // Escape sluit met focus terug op de bel; een klik buiten sluit zonder focus te stelen.
  useEffect(() => {
    if (!open) return
    function onMouseDown(event: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false)
        buttonRef.current?.focus()
      }
    }
    document.addEventListener('mousedown', onMouseDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onMouseDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  async function openNotification(notification: Notification) {
    try {
      if (!notification.isRead) {
        await markNotificationRead(notification.id)
      }
    } catch {
      // Navigeren mag ook als markeren mislukt; de teller herstelt zich bij de volgende poll.
    }
    setOpen(false)
    refresh()
    if (notification.linkPath) {
      // Producer dedupe markers ride as a #fragment; strip before navigating.
      navigate(notification.linkPath.split('#')[0])
    }
  }

  async function markAll() {
    try {
      await markAllNotificationsRead()
      setItems((current) => current?.map((n) => ({ ...n, isRead: true })) ?? null)
      refresh()
    } catch {
      // Stil: de knop blijft beschikbaar voor een nieuwe poging.
    }
  }

  function toggleOpen() {
    if (!open) {
      // Verse lijst per opening; reset hier (niet in het effect) zodat er geen stale items flitsen.
      setItems(null)
      setLoadError(false)
    }
    setOpen(!open)
  }

  const badge = unreadCount > 99 ? '99+' : String(unreadCount)

  return (
    <div className="ntf-bell" ref={rootRef}>
      <button
        type="button"
        ref={buttonRef}
        className="ntf-bell-button"
        aria-label="Meldingen"
        aria-expanded={open}
        aria-haspopup="true"
        onClick={toggleOpen}
      >
        <Bell size={18} aria-hidden="true" />
        {unreadCount > 0 && <span className="ntf-bell-badge">{badge}</span>}
      </button>

      {open && (
        <div className="ntf-bell-dropdown">
          {loadError && <p className="ntf-bell-empty">Meldingen konden niet worden geladen.</p>}
          {!loadError && items === null && <p className="ntf-bell-empty">Meldingen laden…</p>}
          {!loadError && items !== null && items.length === 0 && <p className="ntf-bell-empty">Geen meldingen.</p>}
          {!loadError && items !== null && items.length > 0 && (
            <ul className="ntf-bell-list">
              {items.map((notification) => (
                <li key={notification.id}>
                  <button type="button" className="ntf-bell-item" onClick={() => void openNotification(notification)}>
                    <span
                      className={`ntf-severity ntf-severity-${notification.severity.toLowerCase()}`}
                      aria-hidden="true"
                    >
                      {NOTIFICATION_SEVERITY_ICONS[notification.severity]}
                    </span>
                    <span className="ntf-bell-item-body">
                      <span className="ntf-bell-item-title">
                        {!notification.isRead && <span className="ntf-dot" aria-label="Ongelezen" />}
                        {notification.title}
                      </span>
                      <span className="ntf-bell-item-time">{formatRelativeTime(notification.createdAt)}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="ntf-bell-footer">
            <Link to="/notifications" onClick={() => setOpen(false)}>
              Alle meldingen
            </Link>
            <button type="button" className="ntf-bell-mark-all" onClick={() => void markAll()}>
              Alles gelezen
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
