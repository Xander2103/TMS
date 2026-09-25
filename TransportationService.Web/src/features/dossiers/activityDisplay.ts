import type { BadgeTone } from '../../components/ui/Badge'
import type { TranslateFn } from '../../i18n/localeContext'
import { euro } from '../invoices/types'
import { readActivityPrice } from './pricing/activityPriceDisplay'
import type { ActivityPriceStatus, DossierActivity, DossierDetail, ReadinessIssue } from './types'

/**
 * Read-only display rules of one activity, shared by the activity card and the Overzicht so both
 * always say the same thing. Nothing is derived that the DTO does not state: a missing driver,
 * vehicle, document or price is shown as missing. The price itself is read through the SAME
 * reading the Verkoop & prijs tab uses (`pricing/activityPriceDisplay`), so the two cannot disagree.
 */

/** Vertaalsleutels per prijsstatus — renderen als t(PRICE_STATUS_LABEL_KEYS[status]). */
export const PRICE_STATUS_LABEL_KEYS: Record<ActivityPriceStatus, string> = {
  NotPriced: 'dossierActivities.priceStatus.NotPriced',
  PartiallyPriced: 'dossierActivities.priceStatus.PartiallyPriced',
  Priced: 'dossierActivities.priceStatus.Priced',
  Free: 'dossierActivities.priceStatus.Free',
}

export const PRICE_STATUS_TONE: Record<ActivityPriceStatus, BadgeTone> = {
  NotPriced: 'danger',
  PartiallyPriced: 'warning',
  Priced: 'success',
  Free: 'info',
}

/**
 * The backend's `priceStatus`; an older payload without it falls back to the price tab's reading
 * (the server's provenance flag). null = a non-billable activity: it has no commercial status.
 */
export function activityPriceStatus(dossier: DossierDetail, activity: DossierActivity): ActivityPriceStatus | null {
  if (activity.isBillable === false) return null
  if (activity.priceStatus) return activity.priceStatus
  const reading = readActivityPrice(dossier, activity)
  return reading.kind === 'free' ? 'Free' : reading.kind === 'amount' ? 'Priced' : 'NotPriced'
}

/**
 * "€ 450,00", "Gratis" or "Nog niet geprijsd"; null for a non-billable activity. A missing price
 * is NEVER rendered as € 0,00 — an amount only shows when the backend says the unit is priced.
 */
export function activityPriceText(t: TranslateFn, dossier: DossierDetail, activity: DossierActivity): string | null {
  if (activity.isBillable === false) return null
  const reading = readActivityPrice(dossier, activity)
  if (reading.kind === 'free') return t('dossierActivities.price.free')
  return reading.kind === 'amount' ? euro(reading.amount) : t('dossierActivities.price.notPriced')
}

/** Order number when there is one, else the planner's label, else the type name. */
export function activityReference(activity: DossierActivity): string {
  return activity.linkedOrderNumber ?? activity.label ?? activity.activityTypeName
}

/** The activity a readiness issue is about (by activity id, else by its linked order). */
export function issueActivity(issue: ReadinessIssue, activities: DossierActivity[]): DossierActivity | undefined {
  return (
    (issue.activityId ? activities.find((a) => a.id === issue.activityId) : undefined) ??
    (issue.transportOrderId ? activities.find((a) => a.linkedTransportOrderId === issue.transportOrderId) : undefined)
  )
}
