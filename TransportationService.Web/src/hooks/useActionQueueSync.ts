import { useEffect, useState } from 'react'
import { actionQueue, type QueuedAction } from '../features/driver/actionQueue'
import { executeQueuedAction } from '../features/driver/offlineActions'
import { scanQueue, type QueuedScan } from '../features/scanning/scanQueue'
import { submitScan } from '../features/scanning/api/scanningApi'
import { useAuth } from '../features/auth/authContextValue'
import { useOnlineStatus } from './useOnlineStatus'

export interface QueueSyncState {
  actions: QueuedAction[]
  scans: QueuedScan[]
  unsyncedCount: number
}

/**
 * Global offline-queue orchestration: binds the action queue to the signed-in user,
 * replays both queues in order whenever the connection returns, and exposes the
 * unsynced counts for badges. Mounted once in the app shell.
 */
export function useActionQueueSync(): QueueSyncState {
  const { user } = useAuth()
  const online = useOnlineStatus()
  const [actions, setActions] = useState<QueuedAction[]>([])
  const [scans, setScans] = useState<QueuedScan[]>([])

  useEffect(() => {
    actionQueue.setUser(user?.id ?? null)
  }, [user?.id])

  useEffect(() => actionQueue.subscribe(setActions), [])
  useEffect(() => scanQueue.subscribe(setScans), [])

  useEffect(() => {
    if (!online || !user) return
    let cancelled = false
    const run = () => {
      if (cancelled) return
      void actionQueue.replay(executeQueuedAction)
      void scanQueue.replay(submitScan)
    }
    run()
    const handle = setInterval(run, Math.max(actionQueue.nextDelayMs(), scanQueue.nextDelayMs()))
    return () => {
      cancelled = true
      clearInterval(handle)
    }
  }, [online, user, actions.length, scans.length])

  return {
    actions,
    scans,
    unsyncedCount: actions.filter((a) => a.state === 'pending').length
      + scans.filter((s) => s.state === 'pending').length,
  }
}
