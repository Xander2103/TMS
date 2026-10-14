import { useCallback, useState } from 'react'
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
