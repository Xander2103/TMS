import { useEffect, useRef, useState } from 'react'
import { getScheduleForSelection } from '../api/employeePlanningApi'
import type { PlanningSelection } from '../planningFilters'
import type { ScheduleGrid } from '../types'

export interface PlanningScheduleState {
  /** Last successfully loaded grid; kept on screen while a newer selection is loading. */
  grid: ScheduleGrid | null
  /** True while the grid on screen does not (yet) belong to the current selection. */
  isFetching: boolean
  /** True when the CURRENT selection failed to load (the stale grid, if any, stays visible). */
  error: boolean
  reload: () => void
}

function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError'
}

/**
 * Loads the grid for the selection with stale-response protection: every change aborts the
 * request in flight, and a response is only applied when it belongs to the latest request
 * (filter A → filter B → A answers late: A is dropped, B stays). Previous data remains visible
 * while the next selection loads, so the page never flashes empty on a filter change.
 */
export function usePlanningSchedule(selection: PlanningSelection, onError: () => void): PlanningScheduleState {
  const [state, setState] = useState<{ grid: ScheduleGrid | null; loadedKey: string; errorKey: string | null }>({
    grid: null,
    loadedKey: '',
    errorKey: null,
  })
  const [reloadToken, setReloadToken] = useState(0)
  const seqRef = useRef(0)
  const onErrorRef = useRef(onError)
  useEffect(() => {
    onErrorRef.current = onError
  })

  // The list view is presentation only: it must not refetch.
  const requestKey = JSON.stringify({ ...selection, listView: undefined, reloadToken })

  useEffect(() => {
    const controller = new AbortController()
    const seq = ++seqRef.current
    getScheduleForSelection(selection, controller.signal).then(
      (grid) => {
        if (seq !== seqRef.current) return
        setState({ grid, loadedKey: requestKey, errorKey: null })
      },
      (error: unknown) => {
        if (isAbortError(error) || seq !== seqRef.current) return
        setState((current) => ({ ...current, errorKey: requestKey }))
        onErrorRef.current()
      },
    )
    return () => controller.abort()
    // The key captures every selection field the request depends on.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  return {
    grid: state.grid,
    isFetching: state.loadedKey !== requestKey && state.errorKey !== requestKey,
    error: state.errorKey === requestKey,
    reload: () => setReloadToken((token) => token + 1),
  }
}
