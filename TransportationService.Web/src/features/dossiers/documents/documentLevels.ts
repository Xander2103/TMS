import type { DossierActivity, DossierOrder } from '../types'
import type { DossierDocument } from '../api/dossierDocumentsApi'

/**
 * Documenten workspace (master sprint 2026-09-21, D6; activity scope 2026-10-08): ONE document model
 * with three kinds of level. The dossier level holds the documents of the dossier as a whole; every
 * linked order of the dossier is a level of its own; and every STANDALONE activity (no linked
 * order — plateau, crane on site, …) is a level of its own. Which activities qualify is decided by
 * their link, never by a name or type code: an order-backed activity shows its documents through
 * its order level.
 */
export interface DossierDocumentOrderLevel {
  orderId: string
  orderNumber: string
  /** Type name (or planner label) of the activity the order executes; null for a legacy link. */
  activityName: string | null
}

export interface DossierDocumentActivityLevel {
  activityId: string
  /** Planner label, falling back to the type name — there is no activity number. */
  label: string
  activityTypeName: string
}

export interface DossierDocumentLevels {
  orders: DossierDocumentOrderLevel[]
  activities: DossierDocumentActivityLevel[]
}

export const DOSSIER_LEVEL = 'dossier'
const ACTIVITY_PREFIX = 'activity:'

/** The level a document hangs on: `DOSSIER_LEVEL`, an order id, or `activity:<activity id>`. */
export type DocumentLevelId = typeof DOSSIER_LEVEL | string

export const activityLevelId = (activityId: string): DocumentLevelId => `${ACTIVITY_PREFIX}${activityId}`

export type DocumentLevelRef =
  | { kind: 'dossier' }
  | { kind: 'order'; orderId: string }
  | { kind: 'activity'; activityId: string }

export function parseLevel(id: DocumentLevelId): DocumentLevelRef {
  if (id === DOSSIER_LEVEL) return { kind: 'dossier' }
  if (id.startsWith(ACTIVITY_PREFIX)) return { kind: 'activity', activityId: id.slice(ACTIVITY_PREFIX.length) }
  return { kind: 'order', orderId: id }
}

/** The level of a document record (server scope first, never guessed from a label). */
export function levelOfDocument(document: Pick<DossierDocument, 'scope' | 'transportOrderId' | 'dossierActivityId'>): DocumentLevelId {
  if (document.scope === 'Order' && document.transportOrderId) return document.transportOrderId
  if (document.scope === 'Activity' && document.dossierActivityId) return activityLevelId(document.dossierActivityId)
  return DOSSIER_LEVEL
}

export function buildDocumentLevels(activities: DossierActivity[], legacyOrders: DossierOrder[]): DossierDocumentLevels {
  const orders: DossierDocumentOrderLevel[] = []
  const activityLevels: DossierDocumentActivityLevel[] = []
  const seen = new Set<string>()
  for (const activity of [...activities].sort((a, b) => a.sequence - b.sequence)) {
    if (activity.linkedTransportOrderId) {
      if (seen.has(activity.linkedTransportOrderId)) continue
      seen.add(activity.linkedTransportOrderId)
      orders.push({
        orderId: activity.linkedTransportOrderId,
        orderNumber: activity.linkedOrderNumber ?? activity.linkedTransportOrderId,
        activityName: activity.label?.trim() || activity.activityTypeName,
      })
    } else if (!activity.hasStops) {
      // A standalone activity: documents hang on the activity itself. A transport-shaped
      // activity that merely has no order yet gets its level once the order exists.
      activityLevels.push({
        activityId: activity.id,
        label: activity.label?.trim() || activity.activityTypeName,
        activityTypeName: activity.activityTypeName,
      })
    }
  }
  for (const order of legacyOrders) {
    if (seen.has(order.orderId)) continue
    seen.add(order.orderId)
    orders.push({ orderId: order.orderId, orderNumber: order.orderNumber, activityName: null })
  }
  return { orders, activities: activityLevels }
}

/** "ORD-0001 · Direct transport" / "ORD-0001". */
export function orderLevelLabel(level: DossierDocumentOrderLevel): string {
  return level.activityName ? `${level.orderNumber} · ${level.activityName}` : level.orderNumber
}

/** True when the level still exists (an unlinked order or a removed activity falls back to the dossier level). */
export function isKnownLevel(id: DocumentLevelId, levels: DossierDocumentLevels): boolean {
  const ref = parseLevel(id)
  if (ref.kind === 'dossier') return true
  if (ref.kind === 'order') return levels.orders.some((level) => level.orderId === ref.orderId)
  return levels.activities.some((level) => level.activityId === ref.activityId)
}

/** The level to open on: `?opdracht=` (an order id) or `?activiteit=` (an activity id) when known, else the dossier level. */
export function resolveInitialLevel(
  requested: { orderId?: string | null; activityId?: string | null },
  levels: DossierDocumentLevels,
): DocumentLevelId {
  if (requested.orderId && levels.orders.some((level) => level.orderId === requested.orderId)) return requested.orderId
  if (requested.activityId && levels.activities.some((level) => level.activityId === requested.activityId)) {
    return activityLevelId(requested.activityId)
  }
  return DOSSIER_LEVEL
}
