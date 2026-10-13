import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ActionQueue, type QueuedAction } from '../actionQueue'

function makeStorage(): Storage {
  const map = new Map<string, string>()
  return {
    get length() {
      return map.size
    },
    clear: () => map.clear(),
    getItem: (key: string) => map.get(key) ?? null,
    key: (index: number) => [...map.keys()][index] ?? null,
    removeItem: (key: string) => void map.delete(key),
    setItem: (key: string, value: string) => void map.set(key, value),
  }
}

function action(id: string, kind: QueuedAction['kind'] = 'stopTransition'): Omit<QueuedAction, 'queuedAt' | 'attempts' | 'state' | 'lastError'> {
  return { clientRequestId: id, kind, label: `actie ${id}`, payload: { id } }
}

describe('ActionQueue', () => {
  let queue: ActionQueue

  beforeEach(() => {
    queue = new ActionQueue(makeStorage())
    queue.setUser('user-1')
  })

  it('is empty (and write-proof) without a signed-in user', () => {
    const anonymous = new ActionQueue(makeStorage())
    expect(anonymous.list()).toEqual([])
    anonymous.enqueue(action('a'))
    expect(anonymous.list()).toEqual([])
  })

  it('deduplicates on clientRequestId', () => {
    queue.enqueue(action('a'))
    queue.enqueue(action('a'))
    expect(queue.list()).toHaveLength(1)
  })

  it('keeps queues isolated per user and clears on logout', () => {
    queue.enqueue(action('a'))
    queue.setUser('user-2')
    expect(queue.list()).toEqual([])
    queue.setUser('user-1')
    expect(queue.list()).toHaveLength(1)
    queue.clear()
    expect(queue.list()).toEqual([])
  })

  it('replays in order, removes successes, and stops at a network failure', async () => {
    queue.enqueue(action('a'))
    queue.enqueue(action('b'))
    queue.enqueue(action('c'))

    const executed: string[] = []
    const outcome = await queue.replay(async (item) => {
      executed.push(item.clientRequestId)
      if (item.clientRequestId === 'b') {
        throw new TypeError('offline') // no status → network failure
      }
    })

    // Ordered sync: c never ran because b hit a network failure.
    expect(executed).toEqual(['a', 'b'])
    expect(outcome.succeeded).toBe(1)
    expect(queue.list().map((i) => i.clientRequestId)).toEqual(['b', 'c'])
    expect(queue.list().every((i) => i.state === 'pending')).toBe(true)
  })

  it('marks server rejections failed but keeps them visible for retry', async () => {
    queue.enqueue(action('a'))
    const serverError = Object.assign(new Error('Ongeldige overgang.'), { status: 400 })
    await queue.replay(() => Promise.reject(serverError))

    const item = queue.list()[0]
    expect(item.state).toBe('failed')
    expect(item.lastError).toBe('Ongeldige overgang.')

    queue.retry('a')
    expect(queue.list()[0].state).toBe('pending')
  })

  it('notifies subscribers on every change', () => {
    const listener = vi.fn()
    queue.subscribe(listener)
    queue.enqueue(action('a'))
    queue.remove('a')
    expect(listener).toHaveBeenCalledTimes(3) // initial + enqueue + remove
  })
})
