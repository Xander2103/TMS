import { describe, expect, it } from 'vitest'
import type { Notification } from '../api/notificationsApi'
import {
  DEFAULT_LIST_FILTERS,
  computeStats,
  computeSummary,
  matchesKind,
  filterNotifications,
  groupKeyFor,
  groupNotifications,
  resolveNotificationLink,
} from '../notificationView'

function make(overrides: Partial<Notification> = {}): Notification {
  return {
    id: 'n',
    type: 'General',
    category: 'General',
    severity: 'Info',
    title: 'Titel',
    message: 'Bericht',
    linkPath: null,
    isRead: false,
    isArchived: false,
    createdAt: '2026-09-12T08:00:00Z',
    requiresAcknowledgement: false,
    acknowledgedAt: null,
    resolvedAt: null,
    expiresAt: null,
    ...overrides,
  }
}

describe('resolveNotificationLink', () => {
  it('rewrites the legacy /orders/{id} producer path to the SPA route', () => {
    expect(resolveNotificationLink('/orders/abc-123')).toEqual({ path: '/transport-orders/abc-123', kind: 'order' })
    expect(resolveNotificationLink('/orders/abc-123?tab=x')).toEqual({ path: '/transport-orders/abc-123?tab=x', kind: 'order' })
  })

  it('strips producer dedupe fragments and classifies known targets', () => {
    expect(resolveNotificationLink('/settings/issued-item-templates/1?tab=varianten#dedupe')).toEqual({
      path: '/settings/issued-item-templates/1?tab=varianten',
      kind: 'inventory',
    })
    expect(resolveNotificationLink('/invoices/9')?.kind).toBe('invoice')
    expect(resolveNotificationLink('/dossiers/9')?.kind).toBe('dossier')
    expect(resolveNotificationLink('/exceptions/9')?.kind).toBe('exception')
    expect(resolveNotificationLink('/something-else')?.kind).toBe('other')
  })

  it('returns null without a link', () => {
    expect(resolveNotificationLink(null)).toBeNull()
    expect(resolveNotificationLink('#only-fragment')).toBeNull()
  })
})

describe('grouping', () => {
  const now = new Date('2026-09-12T15:00:00')

  it('splits today / this week / earlier on local calendar day and a 7-day window', () => {
    expect(groupKeyFor(new Date('2026-09-12T00:30:00').toISOString(), now)).toBe('today')
    expect(groupKeyFor(new Date('2026-09-11T23:00:00').toISOString(), now)).toBe('week')
    expect(groupKeyFor(new Date('2026-09-06T00:00:00').toISOString(), now)).toBe('week')
    expect(groupKeyFor(new Date('2026-09-04T15:00:00').toISOString(), now)).toBe('earlier')
    expect(groupKeyFor('not-a-date', now)).toBe('earlier')
  })

  it('keeps fixed group order and drops empty groups', () => {
    const groups = groupNotifications(
      [
        make({ id: 'old', createdAt: '2026-08-01T10:00:00Z' }),
        make({ id: 'today', createdAt: new Date('2026-09-12T09:00:00').toISOString() }),
      ],
      now,
    )
    expect(groups.map((g) => g.key)).toEqual(['today', 'earlier'])
    expect(groups[0].items[0].id).toBe('today')
  })
})

describe('filterNotifications', () => {
  const items = [
    make({ id: 'a', title: 'Opdracht aangemaakt', category: 'Orders', createdAt: '2026-09-12T08:00:00Z' }),
    make({ id: 'b', title: 'Lage voorraad', severity: 'Warning', isRead: true, createdAt: '2026-09-11T08:00:00Z' }),
    make({ id: 'c', title: 'Opgelost', resolvedAt: '2026-09-10T08:00:00Z', createdAt: '2026-09-10T08:00:00Z' }),
    make({ id: 'd', title: 'Bevestig mij', requiresAcknowledgement: true, createdAt: '2026-09-09T08:00:00Z' }),
  ]
  const label = (category: string) => (category === 'Orders' ? 'Opdrachten' : 'Algemeen')

  it('hides resolved by default and sorts newest first', () => {
    expect(filterNotifications(items, DEFAULT_LIST_FILTERS, label).map((n) => n.id)).toEqual(['a', 'b', 'd'])
  })

  it('supports oldest-first, unread/read/ack status and warnings-only', () => {
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, sort: 'oldest' }, label).map((n) => n.id)).toEqual(['d', 'b', 'a'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, status: 'unread' }, label).map((n) => n.id)).toEqual(['a', 'd'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, status: 'read' }, label).map((n) => n.id)).toEqual(['b'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, status: 'ack' }, label).map((n) => n.id)).toEqual(['d'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, warningsOnly: true }, label).map((n) => n.id)).toEqual(['b'])
  })

  it('searches title, message and the translated category label', () => {
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, search: 'voorraad' }, label).map((n) => n.id)).toEqual(['b'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, search: 'opdrachten' }, label).map((n) => n.id)).toEqual(['a'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, search: 'opgelost', hideResolved: false }, label).map((n) => n.id)).toEqual(['c'])
  })
})

describe('computeStats', () => {
  it('counts open, unread and warnings, ignoring archived rows', () => {
    const stats = computeStats([
      make({ id: 'a' }),
      make({ id: 'b', severity: 'Critical', isRead: true }),
      make({ id: 'c', severity: 'Warning', resolvedAt: '2026-09-01T00:00:00Z' }),
      make({ id: 'd', isArchived: true, severity: 'Warning' }),
    ])
    expect(stats).toEqual({ open: 2, unread: 2, warnings: 1 })
  })
})

describe('kind filter and summary chips', () => {
  const label = (category: string) => category
  const items = [
    make({ id: 'o1', category: 'Orders', type: 'order_created' }),
    make({ id: 'o2', category: 'Orders', type: 'order_created', isRead: true }),
    make({ id: 'p1', category: 'Planning', type: 'planning_missing_date' }),
    make({ id: 'i1', category: 'Orders', type: 'invoice_due_soon' }),
    make({ id: 'w1', category: 'Execution', type: 'incident', severity: 'Critical' }),
    make({ id: 'x1', category: 'Orders', type: 'order_created', isArchived: true }),
  ]

  it('classifies orders, planning and invoices (invoice types never count as orders)', () => {
    expect(items.filter((n) => matchesKind(n, 'orders')).map((n) => n.id)).toEqual(['o1', 'o2', 'x1'])
    expect(items.filter((n) => matchesKind(n, 'planning')).map((n) => n.id)).toEqual(['p1'])
    expect(items.filter((n) => matchesKind(n, 'invoices')).map((n) => n.id)).toEqual(['i1'])
    expect(items.filter((n) => matchesKind(n, 'all'))).toHaveLength(items.length)
  })

  it('narrows the list by kind', () => {
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, kind: 'invoices' }, label).map((n) => n.id)).toEqual(['i1'])
    expect(filterNotifications(items, { ...DEFAULT_LIST_FILTERS, kind: 'orders' }, label).map((n) => n.id)).toEqual(['o1', 'o2', 'x1'])
  })

  it('counts per chip with the unread share, ignoring archived rows', () => {
    expect(computeSummary(items)).toEqual({
      total: 5,
      unread: 4,
      orders: { count: 2, unread: 1 },
      planning: { count: 1, unread: 1 },
      invoices: { count: 1, unread: 1 },
      warnings: { count: 1, unread: 1 },
    })
  })
})
