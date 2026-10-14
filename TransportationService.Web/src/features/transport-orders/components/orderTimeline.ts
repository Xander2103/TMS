import type { OrderTimelineEvent } from '../api/transportOrdersApi'

/** Newest first — the same reading order as every other history (B25). */
export function newestFirst(events: OrderTimelineEvent[]): OrderTimelineEvent[] {
  return [...events].sort((a, b) => b.timestamp.localeCompare(a.timestamp))
}
