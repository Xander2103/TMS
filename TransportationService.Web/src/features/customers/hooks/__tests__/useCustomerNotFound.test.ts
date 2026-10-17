import { describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { ApiError } from '../../../../api/apiClient'
import { CUSTOMER_NOT_FOUND, useCustomer } from '../useCustomer'

const api = vi.hoisted(() => ({ getCustomer: vi.fn() }))
vi.mock('../../api/customersApi', () => api)

// M14: an unknown customer (404) is "Klant niet gevonden", not "kon niet worden geladen".
describe('useCustomer load failures', () => {
  it('maps a 404 to the not-found key and other failures to the load error', async () => {
    api.getCustomer.mockRejectedValueOnce(new ApiError('Not found', 404))
    const missing = renderHook(() => useCustomer('c-404'))
    await waitFor(() => expect(missing.result.current.error).toBe(CUSTOMER_NOT_FOUND))

    api.getCustomer.mockRejectedValueOnce(new ApiError('Boom', 500))
    const broken = renderHook(() => useCustomer('c-500'))
    await waitFor(() => expect(broken.result.current.error).toBe('customers.detail.loadFailed'))
  })
})
