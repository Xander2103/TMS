import { describe, expect, it } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { useDossierFilters } from '../pages/useDossierFilters'

type Api = ReturnType<typeof useDossierFilters>

function renderHookInRouter(initialEntry = '/dossiers') {
  const api: { current: Api | null } = { current: null }
  function Probe() {
    const value = useDossierFilters()
    api.current = value
    return <output>{JSON.stringify({ status: value.filters.status, postalCode: value.filters.postalCode, search: value.filters.search, page: value.filters.page })}</output>
  }
  const router = createMemoryRouter(
    [
      { path: '/dossiers', element: <Probe /> },
      { path: '/elders', element: <p>elders</p> },
    ],
    { initialEntries: [initialEntry] },
  )
  render(<RouterProvider router={router} />)
  return { router, apply: (...args: Parameters<Api['apply']>) => api.current!.apply(...args) }
}

const shown = () => JSON.parse(screen.getByRole('status').textContent ?? '{}')

describe('useDossierFilters', () => {
  it('leest de filters uit de URL', () => {
    renderHookInRouter('/dossiers?status=Closed&postalCode=2000&page=3')

    expect(shown()).toEqual({ status: 'Closed', postalCode: '2000', search: '', page: 3 })
  })

  it('opeenvolgende wijzigingen vóór de URL bij is, bouwen op elkaar voort — geen filter gaat verloren', () => {
    const { router, apply } = renderHookInRouter()

    // Same tick: the router has not committed the first change when the second is made.
    act(() => {
      apply({ status: 'Closed' })
      apply({ postalCode: '2000' })
      apply({ search: 'kraan' }, { replace: true })
    })

    expect(router.state.location.search).toBe('?search=kraan&status=Closed&postalCode=2000')
    expect(shown()).toEqual({ status: 'Closed', postalCode: '2000', search: 'kraan', page: 1 })
  })

  it('snelle toetsaanslagen: elke aanslag bouwt op de vorige, ook binnen één tick', () => {
    const { router, apply } = renderHookInRouter('/dossiers?status=Open')

    act(() => {
      for (const text of ['k', 'kr', 'kra']) apply({ search: text }, { replace: true })
    })

    expect(router.state.location.search).toBe('?search=kra&status=Open')
  })

  it('een filterwijziging gaat terug naar pagina 1; een expliciete pagina blijft', () => {
    const { router, apply } = renderHookInRouter('/dossiers?status=Closed&page=4')

    act(() => apply({ postalCode: '2000' }))
    expect(router.state.location.search).toBe('?status=Closed&postalCode=2000')

    act(() => apply({ page: 2 }))
    expect(router.state.location.search).toBe('?status=Closed&postalCode=2000&page=2')
  })

  it('een wijziging zonder effect navigeert niet (geen extra history-item)', async () => {
    const { router, apply } = renderHookInRouter('/dossiers?status=Closed')

    act(() => apply({ status: 'Closed' }))
    act(() => apply({ postalCode: '2000' }))
    await act(() => router.navigate(-1))

    // One step back lands on the start: the no-op left no entry behind.
    expect(router.state.location.search).toBe('?status=Closed')
    expect(shown().postalCode).toBe('')
  })

  it('Terug/Vooruit en links van buitenaf winnen: de URL is de bron van waarheid', async () => {
    const { router, apply } = renderHookInRouter()

    act(() => apply({ status: 'Open' }))
    act(() => apply({ postalCode: '9000' }))
    expect(shown()).toMatchObject({ status: 'Open', postalCode: '9000' })

    await act(() => router.navigate(-1))
    expect(shown()).toMatchObject({ status: 'Open', postalCode: '' })

    await act(() => router.navigate('/dossiers?status=Cancelled&page=2'))
    expect(shown()).toEqual({ status: 'Cancelled', postalCode: '', search: '', page: 2 })

    // A change after an external navigation builds on that URL, not on an older request.
    act(() => apply({ postalCode: '1000' }))
    expect(router.state.location.search).toBe('?status=Cancelled&postalCode=1000')
  })
})
