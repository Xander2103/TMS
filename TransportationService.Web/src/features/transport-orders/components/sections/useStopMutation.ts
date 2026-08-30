import { useRef, type Dispatch, type SetStateAction } from 'react'
import { remapCargoStopIndices, type CargoFormRow, type StopFormRow } from './orderFormState'

/**
 * The single write path for the stop list in every editor that owns one (the transport-order form
 * and the dossier route drawer). It applies the mutation AND renumbers the goods lines' stop links
 * in the same step, because those links address stops by POSITION (A1a).
 *
 * Re-review N-1: the first version read `stops` from the render closure, so two mutations
 * dispatched before React re-rendered both started from the pre-batch list and the first was
 * silently dropped — along with the cargo remap computed against that same stale list. The
 * mutation therefore reads the LATEST list from a ref that is advanced synchronously here, so a
 * second call in the same tick sees the first one's result.
 *
 * Why not `setStops(previous => …)` with the remap nested inside it: React re-invokes state
 * updaters (StrictMode does so deliberately, twice), and `remapCargoStopIndices` is not idempotent
 * — applying it twice with the same (previous → next) pair maps an already-renumbered index a
 * second time and lands on the wrong stop. A nested `setCargoItems` would therefore corrupt the
 * links in development. Keeping both updates outside the updater, with the previous/next pair
 * computed exactly once, is what makes them agree.
 */
export function useStopMutation(
  stops: StopFormRow[],
  setStops: Dispatch<SetStateAction<StopFormRow[]>>,
  setCargoItems: Dispatch<SetStateAction<CargoFormRow[]>>,
  onMutated?: () => void,
): (mutate: (rows: StopFormRow[]) => StopFormRow[]) => void {
  // Re-synced on every render, so a stop list replaced elsewhere (the drawers' 409 rebase, a
  // re-seeded form) is picked up; advanced by the mutation itself for the same-tick case.
  const latest = useRef(stops)
  latest.current = stops

  return (mutate) => {
    const previous = latest.current
    const next = mutate(previous)
    if (next === previous) return
    latest.current = next
    setStops(next)
    setCargoItems((rows) => remapCargoStopIndices(rows, previous, next))
    onMutated?.()
  }
}
