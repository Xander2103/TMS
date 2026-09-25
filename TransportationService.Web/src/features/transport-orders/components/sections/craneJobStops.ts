import { isEmptyStopRow, type StopFormRow } from './orderFormState'

/**
 * Stop sets of the two kinds of crane job (master sprint 2026-09-21, D2). They are disjoint: an
 * on-site lifting job is ONE work-site stop, every other order is laden → lossen. Shared by the
 * editors that offer "Soort kraanopdracht", so a switch behaves the same everywhere and never
 * invents a fictitious loading/unloading stop for an on-site job.
 */

type MakeStop = (stopType: StopFormRow['stopType']) => StopFormRow

/** Adds the placeholder row(s) the kind still lacks: a work site, or a loading + an unloading stop. */
export function ensureStopsForKind(rows: StopFormRow[], onSite: boolean, makeStop: MakeStop): StopFormRow[] {
  const next = [...rows]
  if (onSite) {
    if (!next.some((row) => row.stopType === 'Site')) next.push(makeStop('Site'))
    return next
  }
  if (!next.some((row) => row.stopType === 'Loading')) next.unshift(makeStop('Loading'))
  if (!next.some((row) => row.stopType === 'Unloading')) next.push(makeStop('Unloading'))
  return next
}

/** The stop list after switching kind: rows of the OTHER kind go, the new kind's rows are ensured. */
export function switchStopsToKind(rows: StopFormRow[], onSite: boolean, makeStop: MakeStop): StopFormRow[] {
  return ensureStopsForKind(rows.filter((row) => (row.stopType === 'Site') === onSite), onSite, makeStop)
}

/** True when the switch would drop a stop that holds data — the editor asks before it does that. */
export function kindSwitchDropsData(rows: StopFormRow[], onSite: boolean): boolean {
  return rows.some((row) => (row.stopType === 'Site') !== onSite && !isEmptyStopRow(row))
}
