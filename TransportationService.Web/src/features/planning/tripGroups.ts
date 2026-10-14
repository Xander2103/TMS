import type { TripListItem } from './types'

/**
 * B20: the Ritlijst as an operational list. Three groups, each trip in exactly ONE of them:
 *  - running ("Lopend"): every InProgress trip, whatever its date — a trip that left days ago
 *    never drops out of sight; `since` marks one that started before today;
 *  - overdue ("Achterstallig"): a trip dated before today that never left (Draft/Planned);
 *  - day ("Geselecteerde dag"): the chosen date, minus trips already shown above
 *    (`movedUp` counts them so the day list says where they went).
 */
export interface TripGroups {
  running: { trip: TripListItem; startedBefore: boolean }[]
  overdue: TripListItem[]
  day: TripListItem[]
  movedUp: number
}

export function groupTrips(
  dayTrips: TripListItem[],
  runningTrips: TripListItem[],
  pastTrips: TripListItem[],
  todayIso: string,
): TripGroups {
  const running = runningTrips
    .filter((trip) => trip.status === 'InProgress')
    .sort((a, b) => a.tripDate.localeCompare(b.tripDate) || a.tripNumber.localeCompare(b.tripNumber))
    .map((trip) => ({ trip, startedBefore: trip.tripDate < todayIso }))
  const runningIds = new Set(running.map((entry) => entry.trip.id))
  const overdue = pastTrips
    .filter((trip) => trip.tripDate < todayIso && (trip.status === 'Draft' || trip.status === 'Planned') && !runningIds.has(trip.id))
    .sort((a, b) => a.tripDate.localeCompare(b.tripDate) || a.tripNumber.localeCompare(b.tripNumber))
  const shown = new Set([...runningIds, ...overdue.map((trip) => trip.id)])
  const day = dayTrips.filter((trip) => !shown.has(trip.id))
  return { running, overdue, day, movedUp: dayTrips.length - day.length }
}

/** yyyy-MM-dd of the local calendar day (the trip date is a plain date, not an instant). */
export function localIsoDate(date: Date = new Date()): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export function addDaysIso(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00`)
  date.setDate(date.getDate() + days)
  return localIsoDate(date)
}

/** How far back "Achterstallig" looks: older open trips are a data cleanup, not today's work. */
export const OVERDUE_LOOKBACK_DAYS = 14
