import { useCallback, useState } from 'react'
import { ApiError } from '../../api/apiClient'
import type { SaveState } from './SaveStatus'

/** Wraps an in-place save: drives a SaveState and remembers the last action for "Opnieuw proberen". */
export function useSaveStatus() {
  const [state, setState] = useState<SaveState>({ kind: 'idle' })
  const [last, setLast] = useState<(() => Promise<unknown>) | null>(null)

  const run = useCallback(async <R,>(action: () => Promise<R>, describeError?: (err: unknown) => string | undefined): Promise<R | undefined> => {
    setLast(() => action)
    setState({ kind: 'saving' })
    try {
      const result = await action()
      setState({ kind: 'saved', at: new Date().toISOString() })
      return result
    } catch (err) {
      setState({ kind: 'error', message: describeError?.(err) })
      return undefined
    }
  }, [])

  const retry = useCallback(() => {
    if (last) void run(last)
  }, [last, run])

  return { state, run, retry, setState }
}

/**
 * B31: whether "Opslaan mislukt · Opnieuw proberen" helps. A network/server failure may pass on a
 * retry; a refusal of the input (400/422) or a version conflict (409) will not — those are shown
 * on the fields or in a conflict banner instead, so the inline status goes back to neutral.
 */
export function isRetryableSaveError(err: unknown): boolean {
  if (err instanceof ApiError) {
    return err.status === undefined || err.status >= 500 || err.status === 408 || err.status === 429
  }
  return true
}
