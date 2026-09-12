import { createElement } from 'react'
import {
  AlertTriangle,
  Bell,
  Boxes,
  CalendarDays,
  CheckCircle2,
  ClipboardCheck,
  FileText,
  ListChecks,
  MessageSquare,
  OctagonAlert,
  Receipt,
  Route,
  Settings,
  Truck,
  Users,
  type LucideIcon,
} from 'lucide-react'
import type { Notification, NotificationCategory } from '../api/notificationsApi'
import { severityTone } from '../notificationView'

const CATEGORY_ICONS: Record<NotificationCategory, LucideIcon> = {
  General: Bell,
  Orders: FileText,
  Planning: CalendarDays,
  Execution: Route,
  Hr: Users,
  System: Settings,
  Inventory: Boxes,
  Task: ListChecks,
  CustomerPortal: MessageSquare,
  Fleet: Truck,
  Document: FileText,
  Approval: ClipboardCheck,
}

function iconFor(notification: Pick<Notification, 'category' | 'severity' | 'type'>): LucideIcon {
  if (notification.severity === 'Critical') return OctagonAlert
  if (notification.severity === 'Warning') return AlertTriangle
  if (notification.severity === 'Success') return CheckCircle2
  if (notification.type.startsWith('invoice')) return Receipt
  return CATEGORY_ICONS[notification.category] ?? Bell
}

/** Plain helper (not a component) so the icon type is resolved outside render. */
function renderGlyph(notification: Pick<Notification, 'category' | 'severity' | 'type'>, size: number) {
  return createElement(iconFor(notification), { size })
}

interface NotificationIconProps {
  notification: Pick<Notification, 'category' | 'severity' | 'type'>
  size?: 'sm' | 'lg'
}

/** Rounded category/severity glyph used by rows and the detail header (decorative). */
export function NotificationIcon({ notification, size = 'sm' }: NotificationIconProps) {
  return (
    <span className={`ntc-icon ntc-icon-${size} is-${severityTone(notification.severity)}`} aria-hidden="true">
      {renderGlyph(notification, size === 'lg' ? 22 : 16)}
    </span>
  )
}
