import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { getUnreadCount } from './api/notificationsApi'
import { NotificationsContext } from './notificationsContextValue'

const UNREAD_POLL_MS = 60_000

/**
 * Centraliseert het pollen van de ongelezen-teller (voorheen in Sidebar), zodat de
 * sidebar-badge en de NotificationBell dezelfde state delen zonder dubbel te pollen.
 * Alleen mounten binnen de ingelogde interne shell (AppLayout).
 */
export function NotificationsProvider({ children }: { children: ReactNode }) {
  const [unreadCount, setUnreadCount] = useState(0)
  const mountedRef = useRef(true)

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  // Light poll so the notification badge stays roughly current without a push channel.
  const refresh = useCallback(() => {
    getUnreadCount()
      .then((data) => {
        if (mountedRef.current) setUnreadCount(data.count)
      })
      .catch(() => {})
  }, [])

  useEffect(() => {
    refresh()
    const timer = window.setInterval(refresh, UNREAD_POLL_MS)
    return () => window.clearInterval(timer)
  }, [refresh])

  const value = useMemo(() => ({ unreadCount, refresh }), [unreadCount, refresh])

  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>
}
