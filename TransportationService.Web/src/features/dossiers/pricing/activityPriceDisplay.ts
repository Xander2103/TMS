import type { DossierActivity, DossierDetail } from '../types'

/** "Plateau" / "Kraanwerk · Werf Gent" — type name plus the planner's free label. */
export function activityUnitName(activity: DossierActivity): string {
  return activity.label ? `${activity.activityTypeName} · ${activity.label}` : activity.activityTypeName
}

/**
 * What the price of one billable activity reads as. Driven by the server: `priceStatus` first,
 * and on a payload without it the server's `isPriced` provenance flag (older payloads: the linked
 * order's flag). The amount is only ever shown for a priced unit — a missing price is `none`,
 * never 0, and `free` only when the server says Free.
 */
export type ActivityPriceReading = { kind: 'amount'; amount: number } | { kind: 'free' } | { kind: 'none' }

export function readActivityPrice(dossier: DossierDetail, activity: DossierActivity): ActivityPriceReading {
  if (activity.priceStatus === 'Free') return { kind: 'free' }
  if (activity.priceStatus === 'NotPriced') return { kind: 'none' }
  const link = activity.linkedTransportOrderId ? dossier.orders.find((o) => o.orderId === activity.linkedTransportOrderId) : undefined
  // Priced = the status says so; PartiallyPriced / no status lean on the server's provenance flag.
  const flagged = typeof activity.isPriced === 'boolean' ? activity.isPriced : (link?.isPriced ?? false)
  const priced = activity.priceStatus === 'Priced' || flagged
  const amount = activity.agreedPrice ?? link?.agreedPrice ?? null
  return priced && amount != null ? { kind: 'amount', amount } : { kind: 'none' }
}

/** Activities that still need a (complete) price: the server says NotPriced/PartiallyPriced, or — without a status — not priced. */
export function needsPricing(dossier: DossierDetail, activity: DossierActivity): boolean {
  if (activity.priceStatus != null) return activity.priceStatus === 'NotPriced' || activity.priceStatus === 'PartiallyPriced'
  return readActivityPrice(dossier, activity).kind === 'none'
}
