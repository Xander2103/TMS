import type { DossierActivity, DossierOrder } from '../types'

/**
 * Documenten workspace (master sprint 2026-09-21, D6): ONE document model with two levels.
 * `null` is the dossier level ("Dossier (algemeen)"); every linked order of the dossier is a
 * level of its own. The level list is derived from the activities (in their planner order) plus
 * the legacy linked orders that no activity represents — each order exactly once.
 */
export interface DossierDocumentOrderLevel {
  orderId: string
  orderNumber: string
  /** Type name (or planner label) of the activity the order executes; null for a legacy link. */
  activityName: string | null
}

export const DOSSIER_LEVEL = 'dossier'

/** The level a document hangs on: `DOSSIER_LEVEL` for the dossier itself, else the order id. */
export type DocumentLevelId = typeof DOSSIER_LEVEL | string

export function buildOrderLevels(activities: DossierActivity[], legacyOrders: DossierOrder[]): DossierDocumentOrderLevel[] {
  const levels: DossierDocumentOrderLevel[] = []
  const seen = new Set<string>()
  for (const activity of [...activities].sort((a, b) => a.sequence - b.sequence)) {
    if (!activity.linkedTransportOrderId || seen.has(activity.linkedTransportOrderId)) continue
    seen.add(activity.linkedTransportOrderId)
    levels.push({
      orderId: activity.linkedTransportOrderId,
      orderNumber: activity.linkedOrderNumber ?? activity.linkedTransportOrderId,
      activityName: activity.label?.trim() || activity.activityTypeName,
    })
  }
  for (const order of legacyOrders) {
    if (seen.has(order.orderId)) continue
    seen.add(order.orderId)
    levels.push({ orderId: order.orderId, orderNumber: order.orderNumber, activityName: null })
  }
  return levels
}

/** "ORD-0001 · Direct transport" / "ORD-0001". */
export function orderLevelLabel(level: DossierDocumentOrderLevel): string {
  return level.activityName ? `${level.orderNumber} · ${level.activityName}` : level.orderNumber
}

/** The level to open on: a known order id, else the dossier level. */
export function resolveInitialLevel(requested: string | null | undefined, orderLevels: DossierDocumentOrderLevel[]): DocumentLevelId {
  if (requested && orderLevels.some((level) => level.orderId === requested)) return requested
  return DOSSIER_LEVEL
}
