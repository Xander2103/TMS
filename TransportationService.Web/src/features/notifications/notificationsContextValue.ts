import { createContext, useContext } from 'react'

export interface UnreadNotificationsValue {
  /** Aantal ongelezen meldingen, gepolld door de NotificationsProvider. */
  unreadCount: number
  /** Haalt de teller direct opnieuw op (bv. na "alles gelezen" of het openen van een melding). */
  refresh: () => void
}

export const NotificationsContext = createContext<UnreadNotificationsValue | null>(null)

export function useUnreadNotifications(): UnreadNotificationsValue {
  const context = useContext(NotificationsContext)
  if (!context) {
    throw new Error('useUnreadNotifications must be used within a NotificationsProvider')
  }
  return context
}
