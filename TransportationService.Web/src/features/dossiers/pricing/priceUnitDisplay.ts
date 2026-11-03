import { Truck, type LucideIcon } from 'lucide-react'
import { issueActivity } from '../activityDisplay'
import { activityTypeIcon } from '../activityTypeIcons'
import type { DossierActivity, DossierDetail, ReadinessIssue } from '../types'
import { needsAttention } from './activityPriceDisplay'

/** The activity type's own icon; a transport unit without one reads as a truck. */
export function priceUnitIcon(activity: DossierActivity): LucideIcon {
  return !activity.icon && activity.hasStops ? Truck : activityTypeIcon(activity.icon)
}

export interface PriceAttention {
  /** Billable units without a settled price. */
  openUnits: DossierActivity[]
  /** Pricing warnings that no open unit already represents (an outdated price on a priced order, …). */
  otherIssues: ReadinessIssue[]
  count: number
}

/** What still needs attention on the price tab, counted the same way everywhere on it. */
export function priceAttention(dossier: DossierDetail, billableActivities: DossierActivity[]): PriceAttention {
  const openUnits = billableActivities.filter((activity) => needsAttention(dossier, activity))
  const openIds = new Set(openUnits.map((activity) => activity.id))
  const otherIssues = dossier.readiness.filter((issue) => {
    if (issue.section !== 'prijs' || issue.severity === 'Info') return false
    const activity = issueActivity(issue, billableActivities)
    return !(activity && openIds.has(activity.id))
  })
  return { openUnits, otherIssues, count: openUnits.length + otherIssues.length }
}
