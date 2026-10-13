import { ChevronRight } from 'lucide-react'
import { Badge } from '../../../components/ui/Badge'
import { NOTIFICATION_CATEGORY_LABELS, type Notification } from '../api/notificationsApi'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate, formatTime } from '../../../utils/dates'
import { groupKeyFor, needsAcknowledgement, severityTone } from '../notificationView'
import { NotificationIcon } from './NotificationIcon'

interface NotificationRowProps {
  notification: Notification
  selected: boolean
  now: Date
  onSelect: (notification: Notification) => void
}

/**
 * Compact inbox row: unread dot · icon · title + category · one-line message · time · chevron.
 * The whole row is one button so it is keyboard reachable; selection state is exposed via aria-pressed.
 */
export function NotificationRow({ notification, selected, now, onSelect }: NotificationRowProps) {
  const { t } = useLocale()
  const isToday = groupKeyFor(notification.createdAt, now) === 'today'
  const time = isToday ? formatTime(notification.createdAt) : formatDate(notification.createdAt)
  const className = [
    'ntc-row',
    !notification.isRead && 'is-unread',
    selected && 'is-selected',
    notification.resolvedAt !== null && 'is-resolved',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <li className="ntc-row-item">
      <button
        type="button"
        className={className}
        aria-pressed={selected}
        data-notification-id={notification.id}
        onClick={() => onSelect(notification)}
      >
        <span className="ntc-row-dot" aria-hidden="true" />
        {!notification.isRead && <span className="sr-only">{t('notificationCenter.bell.unreadDot')}</span>}
        <NotificationIcon notification={notification} />
        <span className="ntc-row-body">
          <span className="ntc-row-head">
            <span className="ntc-row-title">{notification.title}</span>
            <Badge tone={notification.severity === 'Critical' ? 'danger' : severityTone(notification.severity) === 'warning' ? 'warning' : 'info'}>
              {t(NOTIFICATION_CATEGORY_LABELS[notification.category])}
            </Badge>
            {needsAcknowledgement(notification) && <Badge tone="warning">{t('notificationCenter.page.pendingAck')}</Badge>}
            {notification.resolvedAt !== null && <Badge tone="success">{t('notificationCenter.page.resolved')}</Badge>}
            {notification.isArchived && <Badge tone="neutral">{t('notificationCenter.page.archived')}</Badge>}
          </span>
          <span className="ntc-row-message">{notification.message}</span>
        </span>
        <time className="ntc-row-time" dateTime={notification.createdAt}>
          {time}
        </time>
        <ChevronRight className="ntc-row-chevron" size={16} aria-hidden="true" />
      </button>
    </li>
  )
}
