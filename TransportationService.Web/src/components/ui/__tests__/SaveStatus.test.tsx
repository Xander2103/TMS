import { describe, expect, it, vi } from 'vitest'
import { act, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../api/apiClient'
import { SaveStatus } from '../SaveStatus'
import { isRetryableSaveError, useSaveStatus } from '../useSaveStatus'

/** B31: the inline save state of an in-place edit. */
describe('SaveStatus', () => {
  it('shows "✓ Opgeslagen · hh:mm" after a save', () => {
    render(<SaveStatus state={{ kind: 'saved', at: '2026-10-15T12:22:00Z' }} />)
    expect(screen.getByRole('status')).toHaveTextContent(/^✓ Opgeslagen · \d{2}:\d{2}$/)
  })

  it('shows "Opslaan mislukt" with a retry that re-runs the save', async () => {
    const user = userEvent.setup()
    const retry = vi.fn()
    render(<SaveStatus state={{ kind: 'error' }} onRetry={retry} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Opslaan mislukt · Opnieuw proberen')
    await user.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))
    expect(retry).toHaveBeenCalledTimes(1)
  })

  it('pending edits win over an older "Opgeslagen", but never hide a failure', () => {
    const { rerender } = render(<SaveStatus state={{ kind: 'saved', at: '2026-10-15T12:22:00Z' }} dirty />)
    expect(screen.getByRole('status')).toHaveTextContent('Niet-opgeslagen wijzigingen')
    rerender(<SaveStatus state={{ kind: 'error' }} dirty onRetry={() => {}} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Opslaan mislukt')
  })

  it('useSaveStatus drives saving → saved, and error → retry → saved', async () => {
    const action = vi.fn().mockRejectedValueOnce(new Error('netwerk')).mockResolvedValueOnce('ok')
    const { result } = renderHook(() => useSaveStatus())
    await act(async () => {
      await result.current.run(action)
    })
    expect(result.current.state.kind).toBe('error')
    await act(async () => {
      result.current.retry()
    })
    expect(action).toHaveBeenCalledTimes(2)
    expect(result.current.state.kind).toBe('saved')
  })

  it('offers a retry only where one can help', () => {
    expect(isRetryableSaveError(new Error('offline'))).toBe(true)
    expect(isRetryableSaveError(new ApiError('boom', 500))).toBe(true)
    expect(isRetryableSaveError(new ApiError('invoer', 400))).toBe(false)
    expect(isRetryableSaveError(new ApiError('conflict', 409))).toBe(false)
  })
})
