import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { createMemoryRouter, createRoutesFromElements, Outlet, Route, RouterProvider } from 'react-router-dom'
import { RouteErrorPage } from '../RouteErrorPage'

/** B02: a crashing page shows a recoverable Dutch screen, not the router's developer stack trace. */
function Shell() {
  return (
    <div>
      <nav>Zijbalk</nav>
      <main><Outlet /></main>
    </div>
  )
}

function Broken(): never {
  throw new Error('Cannot read properties of undefined (reading "split")')
}

describe('RouteErrorPage (B02)', () => {
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  it('keeps the shell and offers retry, back and home when a page throws during render', () => {
    const log = vi.spyOn(console, 'error').mockImplementation(() => {})
    const router = createMemoryRouter(
      createRoutesFromElements(
        <Route element={<Shell />}>
          <Route errorElement={<RouteErrorPage />}>
            <Route path="/kapot" element={<Broken />} />
          </Route>
        </Route>,
      ),
      { initialEntries: ['/kapot'] },
    )
    render(<RouterProvider router={router} />)

    expect(screen.getByText('Zijbalk')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('Er ging iets mis.')
    expect(screen.getByRole('button', { name: 'Opnieuw proberen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Terug' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Naar het startscherm' })).toBeInTheDocument()
    expect(screen.queryByText(/Unexpected Application Error/)).not.toBeInTheDocument()
    expect(log).toHaveBeenCalled()
  })

  it('renders the not-found page for a 404 route response', async () => {
    const router = createMemoryRouter(
      createRoutesFromElements(
        <Route errorElement={<RouteErrorPage />}>
          <Route path="/x" loader={() => { throw new Response('', { status: 404 }) }} element={<div />} />
        </Route>,
      ),
      { initialEntries: ['/x'] },
    )
    render(<RouterProvider router={router} />)
    expect(await screen.findByText('Pagina niet gevonden')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Naar het startscherm' })).toBeInTheDocument()
  })
})
