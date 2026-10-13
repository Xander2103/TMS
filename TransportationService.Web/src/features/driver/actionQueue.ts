/**
 * Generalized offline action queue for the driver app (stop transitions, POD finalisation,
 * incident reports). Modeled after the scan queue: every action carries a clientRequestId,
 * so replay after a flaky network can never double-apply — the backend's idempotent replay
 * returns the current state. Server rejections (4xx/5xx) mark the item failed and KEEP it
 * visible; network failures stay pending and retry in order. Ordering matters (a completion
 * may depend on an earlier arrival), so a network failure stops the run.
 */

export type QueuedActionKind = 'stopTransition' | 'completeStop' | 'skipStop' | 'incidentCreate'

export type QueuedActionState = 'pending' | 'failed'

export interface QueuedAction {
  clientRequestId: string
  kind: QueuedActionKind
  /** Short human description shown in the unsynced list. */
  label: string
  /** Endpoint parameters, shape depends on kind. */
  payload: Record<string, unknown>
  queuedAt: string
  attempts: number
  state: QueuedActionState
  lastError: string | null
}

export interface ActionReplayOutcome {
  processed: number
  succeeded: number
  failed: number
  remaining: number
}

export type ActionExecutor = (action: QueuedAction) => Promise<unknown>
type Listener = (items: QueuedAction[]) => void

const STORAGE_PREFIX = 'ts.actionQueue.v1'
const BACKOFF_STEP_MS = 5_000

export class ActionQueue {
  private readonly storage: Storage
  private readonly listeners = new Set<Listener>()
  private userId: string | null = null
  private replaying = false

  constructor(storage: Storage = window.localStorage) {
    this.storage = storage
  }

  /** Namespacing per user keeps queues isolated between accounts on a shared device. */
  setUser(userId: string | null): void {
    this.userId = userId
    this.notify()
  }

  private key(): string | null {
    return this.userId ? `${STORAGE_PREFIX}.${this.userId}` : null
  }

  list(): QueuedAction[] {
    const key = this.key()
    if (!key) return []
    try {
      const raw = this.storage.getItem(key)
      if (!raw) return []
      const parsed = JSON.parse(raw) as QueuedAction[]
      return Array.isArray(parsed) ? parsed : []
    } catch {
      return []
    }
  }

  enqueue(action: Omit<QueuedAction, 'queuedAt' | 'attempts' | 'state' | 'lastError'>): QueuedAction {
    const items = this.list()
    const existing = items.find((i) => i.clientRequestId === action.clientRequestId)
    if (existing) return existing
    const item: QueuedAction = {
      ...action,
      queuedAt: new Date().toISOString(),
      attempts: 0,
      state: 'pending',
      lastError: null,
    }
    this.save([...items, item])
    return item
  }

  remove(clientRequestId: string): void {
    this.save(this.list().filter((i) => i.clientRequestId !== clientRequestId))
  }

  retry(clientRequestId: string): void {
    this.save(this.list().map((i) =>
      i.clientRequestId === clientRequestId ? { ...i, state: 'pending' as const, lastError: null } : i))
  }

  /** Removes everything for the current user — part of the secure logout cleanup. */
  clear(): void {
    const key = this.key()
    if (key) {
      try {
        this.storage.removeItem(key)
      } catch {
        // ignore
      }
    }
    this.notify()
  }

  async replay(execute: ActionExecutor): Promise<ActionReplayOutcome> {
    if (this.replaying) {
      return { processed: 0, succeeded: 0, failed: 0, remaining: this.list().length }
    }
    this.replaying = true
    let processed = 0
    let succeeded = 0
    let failed = 0
    try {
      for (const item of this.list().filter((i) => i.state === 'pending')) {
        processed++
        try {
          await execute(item)
          succeeded++
          this.remove(item.clientRequestId)
        } catch (err) {
          const status = (err as { status?: number }).status
          if (typeof status === 'number') {
            failed++
            this.save(this.list().map((i) =>
              i.clientRequestId === item.clientRequestId
                ? {
                    ...i,
                    state: 'failed' as const,
                    attempts: i.attempts + 1,
                    lastError: err instanceof Error ? err.message : 'Afgekeurd door de server.',
                  }
                : i))
          } else {
            // Still offline: ordered sync stops here; the rest waits for the next replay.
            this.save(this.list().map((i) =>
              i.clientRequestId === item.clientRequestId ? { ...i, attempts: i.attempts + 1 } : i))
            break
          }
        }
      }
    } finally {
      this.replaying = false
    }
    return { processed, succeeded, failed, remaining: this.list().length }
  }

  nextDelayMs(): number {
    const maxAttempts = Math.max(0, ...this.list().map((i) => i.attempts))
    return Math.min(60_000, BACKOFF_STEP_MS * Math.max(1, maxAttempts))
  }

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener)
    listener(this.list())
    return () => this.listeners.delete(listener)
  }

  private save(items: QueuedAction[]): void {
    const key = this.key()
    if (key) {
      try {
        this.storage.setItem(key, JSON.stringify(items))
      } catch {
        // Storage full/blocked: the in-memory flow still works for this session.
      }
    }
    this.notify(items)
  }

  private notify(items: QueuedAction[] = this.list()): void {
    for (const listener of this.listeners) {
      listener(items)
    }
  }
}

export const actionQueue = new ActionQueue()
