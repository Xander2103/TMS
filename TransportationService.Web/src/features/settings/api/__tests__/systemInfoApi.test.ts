import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiClient, LONG_RUNNING_TIMEOUT_MS } from '../../../../api/apiClient'
import { createBackup, restoreBackup } from '../systemInfoApi'

describe('systemInfoApi deadlines', () => {
  afterEach(() => vi.restoreAllMocks())

  it('gives a backup set the long-running deadline, not the 30 s default', async () => {
    const post = vi.spyOn(apiClient, 'postJson').mockResolvedValue({})
    await createBackup('voor upgrade')
    expect(post).toHaveBeenCalledWith('/api/system-info/backups', { note: 'voor upgrade' }, { timeoutMs: LONG_RUNNING_TIMEOUT_MS })
  })

  it('gives a restore the long-running deadline', async () => {
    const post = vi.spyOn(apiClient, 'postJson').mockResolvedValue({})
    await restoreBackup('b1', 'backup.dump')
    expect(post).toHaveBeenCalledWith('/api/system-info/backups/b1/restore', { confirmation: 'backup.dump' }, { timeoutMs: LONG_RUNNING_TIMEOUT_MS })
  })
})
