import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, MemoryRouter, RouterProvider, createMemoryRouter } from 'react-router-dom'
import { UnsavedChangesGuard } from '../UnsavedChangesGuard'

describe('UnsavedChangesGuard (B05)', () => {
  it('is inert under a plain router: no crash, only the tab-close warning', () => {
    const add = vi.spyOn(window, 'addEventListener')
    const { container } = render(
      <MemoryRouter>
        <UnsavedChangesGuard when />
      </MemoryRouter>,
    )
    expect(container).toBeEmptyDOMElement()
    expect(add.mock.calls.some(([type]) => type === 'beforeunload')).toBe(true)
    add.mockRestore()
  })

  it('asks before leaving the page under the data router, and stays on "Blijven bewerken"', async () => {
    const router = createMemoryRouter(
      [
        {
          path: '/form',
          element: (
            <>
              <UnsavedChangesGuard when />
              <Link to="/elsewhere">Weg</Link>
            </>
          ),
        },
        { path: '/elsewhere', element: <p>Elders</p> },
      ],
      { initialEntries: ['/form'] },
    )
    render(<RouterProvider router={router} />)
    await userEvent.click(screen.getByRole('link', { name: 'Weg' }))
    const dialog = await screen.findByRole('dialog', { name: 'Niet-opgeslagen wijzigingen' })
    await userEvent.click(screen.getByRole('button', { name: 'Blijven bewerken' }))
    expect(dialog).not.toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/form')

    await userEvent.click(screen.getByRole('link', { name: 'Weg' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Pagina verlaten' }))
    expect(await screen.findByText('Elders')).toBeInTheDocument()
  })
})
