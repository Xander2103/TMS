import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { ApiError } from '../../../../api/apiClient'
import { useRoleMutations } from '../useRoleMutations'

// MD-M7 (master-data remediation 2026-10-18): a duplicate role name (case-insensitive) is refused
// with a Dutch message — as a 400 ProblemDetails or, after a lost race, a 409 { message }. The
// form shows that message, not the generic "systeemrol" text the 409 used to map to.

const api = vi.hoisted(() => ({ createRole: vi.fn(), updateRole: vi.fn() }))
vi.mock('../../api/rolesApi', () => ({
  createRole: (...args: unknown[]) => api.createRole(...args),
  updateRole: (...args: unknown[]) => api.updateRole(...args),
  deactivateRole: vi.fn(),
  assignRolePermissions: vi.fn(),
}))

const DUPLICATE = 'Er bestaat al een rol met deze naam (hoofdletters maken geen verschil). Kies een andere naam.'

describe('useRoleMutations — server messages', () => {
  beforeEach(() => {
    api.createRole.mockReset()
    api.updateRole.mockReset()
  })

  it('shows the duplicate-name message of a 400', async () => {
    api.createRole.mockRejectedValue(
      new ApiError(DUPLICATE, 400, { detail: DUPLICATE, code: 'common.validation_failed', errors: { name: [DUPLICATE] } }),
    )
    const { result } = renderHook(() => useRoleMutations())

    await act(async () => {
      await result.current.create({ name: 'planner', description: null })
    })

    expect(result.current.error).toBe(DUPLICATE)
  })

  it('shows the duplicate-name message of a 409 race instead of the system-role text', async () => {
    api.updateRole.mockRejectedValue(new ApiError(DUPLICATE, 409, { message: DUPLICATE }))
    const { result } = renderHook(() => useRoleMutations())

    await act(async () => {
      await result.current.update('r-1', { name: 'Planner', description: null })
    })

    expect(result.current.error).toBe(DUPLICATE)
  })

  it('keeps the Dutch system-role text for a 409 without a message', async () => {
    api.updateRole.mockRejectedValue(new ApiError('Request to /api/roles/r-1 failed with status 409', 409, 'System roles cannot be modified.'))
    const { result } = renderHook(() => useRoleMutations())

    await act(async () => {
      await result.current.update('r-1', { name: 'Admin', description: null })
    })

    expect(result.current.error).toBe('Deze actie is niet toegestaan voor een systeemrol.')
  })
})
