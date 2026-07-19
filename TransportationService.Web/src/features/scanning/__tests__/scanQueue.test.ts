import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SubmitScanInput } from '../api/scanningApi'
import { ScanQueue } from '../scanQueue'

function input(overrides: Partial<SubmitScanInput> = {}): SubmitScanInput {
  return {
    scanType: 'Load',
    barcode: 'PKG-00001-AAAA',
    quantity: 1,
    damaged: false,
    damageNote: null,
    deviceInfo: 'test',
    clientEventId: crypto.randomUUID(),
    ...overrides,
  }
}

class ApiLikeError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

describe('ScanQueue', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('persists queued scans across instances', () => {
    const queue = new ScanQueue()
    queue.enqueue('trip', 'stop', input({ clientEventId: 'a'.padEnd(36, '0') }))

    const fresh = new ScanQueue()
    expect(fresh.list()).toHaveLength(1)
    expect(fresh.list()[0].input.barcode).toBe('PKG-00001-AAAA')
    expect(fresh.list()[0].state).toBe('pending')
  })

  it('deduplicates on clientEventId', () => {
    const queue = new ScanQueue()
    const item = input()
    queue.enqueue('trip', 'stop', item)
    queue.enqueue('trip', 'stop', item)
    expect(queue.list()).toHaveLength(1)
  })

  it('replay removes items the server accepted (including idempotent replays)', async () => {
    const queue = new ScanQueue()
    queue.enqueue('trip', 'stop', input())
    queue.enqueue('trip', 'stop', input())

    const submit = vi.fn().mockResolvedValue({ replayed: true })
    const outcome = await queue.replay(submit)

    expect(submit).toHaveBeenCalledTimes(2)
    expect(outcome.succeeded).toBe(2)
    expect(queue.list()).toHaveLength(0)
  })

  it('replay marks server-rejected scans failed and keeps them visible', async () => {
    const queue = new ScanQueue()
    queue.enqueue('trip', 'stop', input())

    const submit = vi.fn().mockRejectedValue(new ApiLikeError('Deze stop is al afgehandeld.', 400))
    const outcome = await queue.replay(submit)

    expect(outcome.failed).toBe(1)
    const [item] = queue.list()
    expect(item.state).toBe('failed')
    expect(item.lastError).toContain('afgehandeld')
    expect(item.attempts).toBe(1)
  })

  it('replay keeps items pending on network failure and stops the run', async () => {
    const queue = new ScanQueue()
    queue.enqueue('trip', 'stop', input())
    queue.enqueue('trip', 'stop', input())

    const submit = vi.fn().mockRejectedValue(new TypeError('fetch failed'))
    const outcome = await queue.replay(submit)

    // Only the first was attempted; both remain pending.
    expect(submit).toHaveBeenCalledTimes(1)
    expect(outcome.succeeded).toBe(0)
    expect(outcome.failed).toBe(0)
    expect(queue.list()).toHaveLength(2)
    expect(queue.list().every((i) => i.state === 'pending')).toBe(true)
    expect(queue.list()[0].attempts).toBe(1)
  })

  it('retry moves a failed item back to pending', async () => {
    const queue = new ScanQueue()
    const item = input()
    queue.enqueue('trip', 'stop', item)
    await queue.replay(vi.fn().mockRejectedValue(new ApiLikeError('nee', 400)))
    expect(queue.list()[0].state).toBe('failed')

    queue.retry(item.clientEventId)
    expect(queue.list()[0].state).toBe('pending')

    const submit = vi.fn().mockResolvedValue({})
    await queue.replay(submit)
    expect(queue.list()).toHaveLength(0)
  })

  it('notifies subscribers on every mutation', () => {
    const queue = new ScanQueue()
    const seen: number[] = []
    const unsubscribe = queue.subscribe((items) => seen.push(items.length))

    const item = input()
    queue.enqueue('trip', 'stop', item)
    queue.remove(item.clientEventId)
    unsubscribe()
    queue.enqueue('trip', 'stop', input())

    expect(seen).toEqual([0, 1, 0])
  })
})
