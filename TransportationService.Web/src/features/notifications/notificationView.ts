import type { Notification, NotificationCategory, NotificationSeverity } from './api/notificationsApi'

/**
 * Pure view helpers for the notification centre: link resolution, grouping,
 * filtering and the contextual action per notification. No React, no I/O.
 */

/** Kind of the related object a notification links to; drives the primary action label. */
export type NotificationLinkKind =
  | 'order'
  | 'dossier'
  | 'invoice'
  | 'planning'
  | 'customer'
  | 'employee'
  | 'absences'
  | 'messaging'
  | 'exception'
  | 'pod'
  | 'package'
  | 'fleet'
  | 'inventory'
  | 'attendance'
  | 'qualifications'
  | 'other'

export interface ResolvedNotificationLink {
  /** Navigable in-app path (fragment stripped, legacy paths rewritten). */
  path: string
  kind: NotificationLinkKind
}

/**
 * Backend producers historically wrote `/orders/{id}` while the SPA route is
 * `/transport-orders/:id`; persisted rows keep the old path, so rewrite here.
 * Producer dedupe markers ride as a `#fragment` and are stripped before navigating.
 */
export function resolveNotificationLink(linkPath: string | null): ResolvedNotificationLink | null {
  if (!linkPath) return null
  let path = linkPath.split('#')[0]
  if (!path) return null
  const legacyOrder = /^\/orders\/([^/?]+)(.*)$/.exec(path)
  if (legacyOrder) path = `/transport-orders/${legacyOrder[1]}${legacyOrder[2]}`
  return { path, kind: linkKindFor(path) }
}

function linkKindFor(path: string): NotificationLinkKind {
  if (path.startsWith('/transport-orders/') || path.startsWith('/portal/orders/')) return 'order'
  if (path.startsWith('/dossiers/')) return 'dossier'
  if (path.startsWith('/invoices/')) return 'invoice'
  if (path.startsWith('/planning')) return 'planning'
  if (path.startsWith('/customers/')) return 'customer'
  if (path.startsWith('/employees/')) return 'employee'
  if (path.startsWith('/absences') || path.startsWith('/portal/absences')) return 'absences'
  if (path.startsWith('/messaging')) return 'messaging'
  if (path.startsWith('/exceptions/')) return 'exception'
  if (path.startsWith('/pods/')) return 'pod'
  if (path.startsWith('/packages/')) return 'package'
  if (path.startsWith('/vehicles') || path.startsWith('/trailers') || path.startsWith('/tank-cards')) return 'fleet'
  if (path.startsWith('/settings/issued-item-templates')) return 'inventory'
  if (path.startsWith('/attendance')) return 'attendance'
  if (path.startsWith('/portal/qualifications')) return 'qualifications'
  return 'other'
}

/** Translation key of the primary "Open …" action per link kind. */
export const NOTIFICATION_LINK_LABELS: Record<NotificationLinkKind, string> = {
  order: 'notificationCenter.links.order',
  dossier: 'notificationCenter.links.dossier',
  invoice: 'notificationCenter.links.invoice',
  planning: 'notificationCenter.links.planning',
  customer: 'notificationCenter.links.customer',
  employee: 'notificationCenter.links.employee',
  absences: 'notificationCenter.links.absences',
  messaging: 'notificationCenter.links.messaging',
  exception: 'notificationCenter.links.exception',
  pod: 'notificationCenter.links.pod',
  package: 'notificationCenter.links.package',
  fleet: 'notificationCenter.links.fleet',
  inventory: 'notificationCenter.links.inventory',
  attendance: 'notificationCenter.links.attendance',
  qualifications: 'notificationCenter.links.qualifications',
  other: 'notificationCenter.links.other',
}

export type NotificationGroupKey = 'today' | 'week' | 'earlier'

export const NOTIFICATION_GROUP_ORDER: NotificationGroupKey[] = ['today', 'week', 'earlier']

const DAY_MS = 24 * 60 * 60 * 1000

function isSameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

/** Vandaag = same local calendar day; Deze week = within the last 7 days; Eerder = the rest. */
export function groupKeyFor(createdAt: string, now: Date): NotificationGroupKey {
  const created = new Date(createdAt)
  if (Number.isNaN(created.getTime())) return 'earlier'
  if (isSameLocalDay(created, now)) return 'today'
  if (now.getTime() - created.getTime() < 7 * DAY_MS) return 'week'
  return 'earlier'
}

export interface NotificationGroup {
  key: NotificationGroupKey
  items: Notification[]
}

/** Groups in fixed order; empty groups are omitted. Items keep the incoming order. */
export function groupNotifications(items: Notification[], now: Date): NotificationGroup[] {
  const buckets: Record<NotificationGroupKey, Notification[]> = { today: [], week: [], earlier: [] }
  for (const item of items) buckets[groupKeyFor(item.createdAt, now)].push(item)
  return NOTIFICATION_GROUP_ORDER.filter((key) => buckets[key].length > 0).map((key) => ({ key, items: buckets[key] }))
}

export type NotificationStatusFilter = 'all' | 'unread' | 'read' | 'ack'
export type NotificationSort = 'newest' | 'oldest'

export interface NotificationListFilters {
  search: string
  status: NotificationStatusFilter
  warningsOnly: boolean
  hideResolved: boolean
  sort: NotificationSort
}

export const DEFAULT_LIST_FILTERS: NotificationListFilters = {
  search: '',
  status: 'all',
  warningsOnly: false,
  hideResolved: true,
  sort: 'newest',
}

export function isWarningSeverity(severity: NotificationSeverity): boolean {
  return severity === 'Warning' || severity === 'Critical'
}

export function needsAcknowledgement(notification: Notification): boolean {
  return notification.requiresAcknowledgement && notification.acknowledgedAt === null
}

/**
 * Client-side filtering over the fetched page (category/archive are server-side).
 * `categoryLabel` lets the search also hit the translated category name.
 */
export function filterNotifications(
  items: Notification[],
  filters: NotificationListFilters,
  categoryLabel: (category: NotificationCategory) => string,
): Notification[] {
  const needle = filters.search.trim().toLowerCase()
  const filtered = items.filter((n) => {
    if (filters.hideResolved && n.resolvedAt !== null) return false
    if (filters.status === 'unread' && n.isRead) return false
    if (filters.status === 'read' && !n.isRead) return false
    if (filters.status === 'ack' && !needsAcknowledgement(n)) return false
    if (filters.warningsOnly && !isWarningSeverity(n.severity)) return false
    if (needle) {
      const haystack = `${n.title} ${n.message} ${categoryLabel(n.category)}`.toLowerCase()
      if (!haystack.includes(needle)) return false
    }
    return true
  })
  const direction = filters.sort === 'newest' ? -1 : 1
  return [...filtered].sort((a, b) => direction * a.createdAt.localeCompare(b.createdAt))
}

export interface NotificationStats {
  open: number
  unread: number
  warnings: number
}

/** Header counters over the fetched list (archived rows never count as open). */
export function computeStats(items: Notification[]): NotificationStats {
  let open = 0
  let unread = 0
  let warnings = 0
  for (const n of items) {
    if (n.isArchived) continue
    if (n.resolvedAt === null) open += 1
    if (!n.isRead) unread += 1
    if (n.resolvedAt === null && isWarningSeverity(n.severity)) warnings += 1
  }
  return { open, unread, warnings }
}

export const NOTIFICATION_SEVERITY_LABELS: Record<NotificationSeverity, string> = {
  Info: 'notificationCenter.severity.Info',
  Warning: 'notificationCenter.severity.Warning',
  Critical: 'notificationCenter.severity.Critical',
  Success: 'notificationCenter.severity.Success',
}

export type NotificationTone = 'info' | 'warning' | 'danger' | 'success'

/** Visual tone per severity (icons, badges). */
export function severityTone(severity: NotificationSeverity): NotificationTone {
  if (severity === 'Critical') return 'danger'
  if (severity === 'Warning') return 'warning'
  if (severity === 'Success') return 'success'
  return 'info'
}
