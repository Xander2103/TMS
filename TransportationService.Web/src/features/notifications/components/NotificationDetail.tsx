import { useRef, type ReactNode } from 'react'
import { Archive, ArrowRight, Check, ExternalLink, Info, Inbox, X } from 'lucide-react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { NOTIFICATION_CATEGORY_LABELS, type Notification } from '../api/notificationsApi'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime, formatTime } from '../../../utils/dates'
import {
  NOTIFICATION_LINK_LABELS,
  NOTIFICATION_SEVERITY_LABELS,
  groupKeyFor,
  needsAcknowledgement,
  resolveNotificationLink,
  severityTone,
} from '../notificationView'
import { NotificationIcon } from './NotificationIcon'

interface NotificationDetailProps {
  notification: Notification | null
  now: Date
  busy: boolean
  onOpenLink: (notification: Notification) => void
  onMarkRead: (notification: Notification) => void
  onAcknowledge: (notification: Notification) => void
  onArchive: (notification: Notification) => void
  onNavigate: (path: string) => void
  onClose: () => void
}

/**
 * Right-hand detail panel of the master-detail layout: full text, metadata, and the
 * contextual actions (primary "Open …", mark read, acknowledge, archive, follow-up).
 */
export function NotificationDetail({
  notification,
  now,
  busy,
  onOpenLink,
  onMarkRead,
  onAcknowledge,
  onArchive,
  onNavigate,
  onClose,
}: NotificationDetailProps) {
  const { t } = useLocale()
  const sectionRef = useRef<HTMLElement>(null)

  /** Actions that remove their own button (mark read, acknowledge, archive) keep focus in the panel. */
  function keepFocus(run: () => void) {
    sectionRef.current?.focus()
    run()
  }

  if (!notification) {
    return (
      <section className="ntc-detail ntc-detail-empty" aria-label={t('notificationCenter.detail.ariaLabel')}>
        <Inbox size={28} aria-hidden="true" />
        <h2>{t('notificationCenter.detail.emptyTitle')}</h2>
        <p>{t('notificationCenter.detail.empty')}</p>
      </section>
    )
  }

  const link = resolveNotificationLink(notification.linkPath)
  const isToday = groupKeyFor(notification.createdAt, now) === 'today'
  const moment = isToday
    ? t('notificationCenter.detail.todayAt', { time: formatTime(notification.createdAt) })
    : formatDateTime(notification.createdAt)
  const pendingAck = needsAcknowledgement(notification)
  const tone = severityTone(notification.severity)

  const rows: Array<{ key: string; label: string; value: ReactNode }> = [
    { key: 'type', label: t('notificationCenter.detail.type'), value: <code className="ntc-code">{notification.type}</code> },
    { key: 'category', label: t('notificationCenter.detail.category'), value: t(NOTIFICATION_CATEGORY_LABELS[notification.category]) },
    {
      key: 'severity',
      label: t('notificationCenter.detail.severity'),
      value: <Badge tone={tone === 'info' ? 'neutral' : tone}>{t(NOTIFICATION_SEVERITY_LABELS[notification.severity])}</Badge>,
    },
    { key: 'createdAt', label: t('notificationCenter.detail.createdAt'), value: formatDateTime(notification.createdAt) },
    {
      key: 'status',
      label: t('notificationCenter.detail.status'),
      value: (
        <span className="ntc-detail-status-cell">
          <Badge tone={notification.isRead ? 'neutral' : 'info'}>
            {notification.isRead ? t('notificationCenter.page.readStatus') : t('notificationCenter.page.unreadStatus')}
          </Badge>
          {notification.isArchived && <Badge tone="neutral">{t('notificationCenter.detail.archivedStatus')}</Badge>}
        </span>
      ),
    },
  ]
  if (notification.requiresAcknowledgement) {
    rows.push({
      key: 'ack',
      label: t('notificationCenter.detail.acknowledgement'),
      value: notification.acknowledgedAt
        ? t('notificationCenter.detail.acknowledgedAt', { dateTime: formatDateTime(notification.acknowledgedAt) })
        : t('notificationCenter.detail.pendingAck'),
    })
  }
  if (notification.resolvedAt) {
    rows.push({ key: 'resolvedAt', label: t('notificationCenter.detail.resolvedAt'), value: formatDateTime(notification.resolvedAt) })
  }
  if (notification.expiresAt) {
    rows.push({ key: 'expiresAt', label: t('notificationCenter.detail.expiresAt'), value: formatDateTime(notification.expiresAt) })
  }
  if (link) {
    rows.push({ key: 'link', label: t('notificationCenter.detail.link'), value: <code className="ntc-code">{link.path}</code> })
  }

  const followUp = pendingAck
    ? { text: t('notificationCenter.detail.followUpAck'), action: t('notificationCenter.detail.acknowledge'), run: () => onAcknowledge(notification) }
    : link?.kind === 'order' && notification.resolvedAt === null
      ? { text: t('notificationCenter.detail.followUpOrder'), action: t('notificationCenter.detail.toPlanning'), run: () => onNavigate('/planning') }
      : null

  return (
    <section ref={sectionRef} tabIndex={-1} className="ntc-detail" aria-label={t('notificationCenter.detail.ariaLabel')} aria-busy={busy}>
      <header className="ntc-detail-head">
        <NotificationIcon notification={notification} size="lg" />
        <div className="ntc-detail-heading">
          <Badge tone={tone === 'info' ? 'neutral' : tone}>{t(NOTIFICATION_CATEGORY_LABELS[notification.category])}</Badge>
          <h2 className="ntc-detail-title">{notification.title}</h2>
          <p className="ntc-detail-meta">
            <span>{moment}</span>
            <span className={`ntc-detail-readstate ${notification.isRead ? 'is-read' : 'is-unread'}`}>
              {notification.isRead ? t('notificationCenter.page.readStatus') : t('notificationCenter.page.unreadStatus')}
            </span>
          </p>
        </div>
        <button type="button" className="ntc-detail-close" onClick={onClose} aria-label={t('notificationCenter.detail.close')}>
          <X size={18} aria-hidden="true" />
        </button>
      </header>

      <div className="ntc-detail-actions">
        {link && (
          <Button variant="primary" onClick={() => onOpenLink(notification)} disabled={busy}>
            <ExternalLink size={16} aria-hidden="true" /> {t(NOTIFICATION_LINK_LABELS[link.kind])}
          </Button>
        )}
        {!notification.isRead && (
          <Button variant="secondary" onClick={() => keepFocus(() => onMarkRead(notification))} disabled={busy}>
            <Check size={16} aria-hidden="true" /> {t('notificationCenter.detail.markRead')}
          </Button>
        )}
        {pendingAck && (
          <Button
            variant="secondary"
            onClick={() => keepFocus(() => onAcknowledge(notification))}
            disabled={busy}
            aria-label={t('notificationCenter.page.acknowledgeAria', { title: notification.title })}
          >
            <Check size={16} aria-hidden="true" /> {t('notificationCenter.detail.acknowledge')}
          </Button>
        )}
        {!notification.isArchived && (
          <Button
            variant="secondary"
            className="ntc-detail-archive"
            onClick={() => keepFocus(() => onArchive(notification))}
            disabled={busy}
            aria-label={t('notificationCenter.page.archiveAria', { title: notification.title })}
          >
            <Archive size={16} aria-hidden="true" /> {t('notificationCenter.detail.archive')}
          </Button>
        )}
      </div>

      <div className="ntc-detail-body">
        <p className="ntc-detail-message">{notification.message}</p>
        <dl className="ntc-detail-facts">
          {rows.map((row) => (
            <div key={row.key} className="ntc-detail-fact">
              <dt>{row.label}</dt>
              <dd>{row.value}</dd>
            </div>
          ))}
        </dl>
        {followUp && (
          <div className="ntc-followup">
            <Info size={18} aria-hidden="true" />
            <div className="ntc-followup-text">
              <strong>{t('notificationCenter.detail.followUp')}</strong>
              <span>{followUp.text}</span>
            </div>
            <Button variant="secondary" onClick={followUp.run} disabled={busy}>
              {followUp.action} <ArrowRight size={16} aria-hidden="true" />
            </Button>
          </div>
        )}
      </div>
    </section>
  )
}
