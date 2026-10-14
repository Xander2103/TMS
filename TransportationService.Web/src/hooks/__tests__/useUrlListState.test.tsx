import { describe, expect, it } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { parseListState, serializeListState, useUrlListState, type UrlListStateApi } from '../useUrlListState'

const OPTIONS = { defaults: { search: '', status: '', sort: 'date', dir: 'desc' }, allowed: { status: ['Open', 'Closed'], dir: ['asc', 'desc'] } }

describe('useUrlListState (B26)', () => {
  it('parses safely: invalid values and pages fall back to the defaults', () => {
    const state = parseListState(new URLSearchParams('status=Bogus&dir=sideways&page=-3&search=haven'), OPTIONS)
    expect(state).toEqual({ search: 'haven', status: '', sort: 'date', dir: 'desc', page: 1 })
  })

  it('serializes only what differs from the defaults (a clean URL is the default view)', () => {
    expect(serializeListState({ search: '', status: '', sort: 'date', dir: 'desc', page: 1 }, OPTIONS).toString()).toBe('')
    expect(serializeListState({ search: 'haven', status: 'Open', sort: 'date', dir: 'asc', page: 2 }, OPTIONS).toString()).toBe('search=haven&status=Open&dir=asc&page=2')
  })

  it('changes push to the URL, return to page 1, survive Back, and reset clears everything', async () => {
    const api: { current: UrlListStateApi<typeof OPTIONS.defaults> | null } = { current: null }
    function Probe() {
      api.current = useUrlListState(OPTIONS)
      return <output>{JSON.stringify(api.current.state)}</output>
    }
    const router = createMemoryRouter([{ path: '/list', element: <Probe /> }], { initialEntries: ['/list?page=3'] })
    render(<RouterProvider router={router} />)
    const shown = () => JSON.parse(screen.getByRole('status').textContent ?? '{}')
    expect(shown().page).toBe(3)

    act(() => {
      api.current!.apply({ status: 'Open' })
      api.current!.apply({ search: 'haven' }, { replace: true })
    })
    expect(router.state.location.search).toBe('?search=haven&status=Open')
    expect(shown()).toMatchObject({ status: 'Open', search: 'haven', page: 1 })
    expect(api.current!.isFiltered).toBe(true)

    await act(async () => {
      await router.navigate(-1)
    })
    expect(shown()).toMatchObject({ page: 3, status: '' })

    act(() => api.current!.reset())
    expect(router.state.location.search).toBe('')
  })
})
