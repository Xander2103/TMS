import type { SubmitScanInput } from './api/scanningApi'

export type QueuedScanState = 'pending' | 'failed'

export interface QueuedScan {
  clientEventId: string
  tripId: string
  stopId: string
  input: SubmitScanInput
  queuedAt: string
  attempts: number
  state: QueuedScanState
  lastError: string | null
}

export interface ReplayOutcome {
  processed: number
  succeeded: number
  failed: number
  remaining: number
}

type Submit = (tripId: string, stopId: string, input: SubmitScanInput) => Promise<unknown>
type Listener = (items: QueuedScan[]) => void

const STORAGE_KEY = 'ts.scanQueue.v1'
const MAX_ATTEMPTS_BEFORE_BACKOFF_MS = 5_000

/**
 * Offline scan queue: every queued item carries its ClientEventId, so replaying after a
 * flaky network can never double-register a scan — the server's idempotent replay returns
 * the original outcome. Server rejections (4xx) mark the item failed and KEEP it visible;
 * a failed scan is never silently dropped. Network failures stay pending and retry.
 */
export class ScanQueue {
  private readonly storage: Storage
  private readonly listeners = new Set<Listener>()
  private replaying = false

  constructor(storage: Storage = window.localStorage) {
    this.storage = storage
  }

  list(): QueuedScan[] {
    try {
      const raw = this.storage.getItem(STORAGE_KEY)
      if (!raw) return []
      const parsed = JSON.parse(raw) as QueuedScan[]
      return Array.isArray(parsed) ? parsed : []
    } catch {
      return []
    }
  }

  enqueue(tripId: string, stopId: string, input: SubmitScanInput): QueuedScan {
    const items = this.list()
    const existing = items.find((i) => i.clientEventId === input.clientEventId)
    if (existing) {
      return existing
    }
    const item: QueuedScan = {
      clientEventId: input.clientEventId,
      tripId,
      stopId,
      input,
      queuedAt: new Date().toISOString(),
      attempts: 0,
      state: 'pending',
      lastError: null,
    }
    this.save([...items, item])
    return item
  }

  remove(clientEventId: string): void {
    this.save(this.list().filter((i) => i.clientEventId !== clientEventId))
  }

  /** Moves a failed item back to pending for one more attempt. */
  retry(clientEventId: string): void {
    this.save(this.list().map((i) =>
      i.clientEventId === clientEventId ? { ...i, state: 'pending' as const, lastError: null } : i))
  }

  /**
   * Replays pending items in queue order. A server response (2xx or replayed) removes the
   * item; a 4xx/5xx response marks it failed with the server's message; a network error
   * stops the run (still offline) and leaves the rest pending.
   */
  async replay(submit: Submit): Promise<ReplayOutcome> {
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
          await submit(item.tripId, item.stopId, item.input)
          succeeded++
          this.remove(item.clientEventId)
        } catch (err) {
          const status = (err as { status?: number }).status
          if (typeof status === 'number') {
            // The server judged the scan; keep it visible as failed with the reason.
            failed++
            this.save(this.list().map((i) =>
              i.clientEventId === item.clientEventId
                ? {
                    ...i,
                    state: 'failed' as const,
                    attempts: i.attempts + 1,
                    lastError: err instanceof Error ? err.message : 'Afgekeurd door de server.',
                  }
                : i))
          } else {
            // Still offline: count the attempt and stop; the rest waits for the next replay.
            this.save(this.list().map((i) =>
              i.clientEventId === item.clientEventId ? { ...i, attempts: i.attempts + 1 } : i))
            break
          }
        }
      }
    } finally {
      this.replaying = false
    }
    return { processed, succeeded, failed, remaining: this.list().length }
  }

  /** Simple linear backoff hint for callers scheduling automatic replays. */
  nextDelayMs(): number {
    const maxAttempts = Math.max(0, ...this.list().map((i) => i.attempts))
    return Math.min(60_000, MAX_ATTEMPTS_BEFORE_BACKOFF_MS * Math.max(1, maxAttempts))
  }

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener)
    listener(this.list())
    return () => this.listeners.delete(listener)
  }

  private save(items: QueuedScan[]): void {
    try {
      this.storage.setItem(STORAGE_KEY, JSON.stringify(items))
    } catch {
      // Storage full/blocked: the in-memory flow still works for this session.
    }
    for (const listener of this.listeners) {
      listener(items)
    }
  }
}

export const scanQueue = new ScanQueue()
