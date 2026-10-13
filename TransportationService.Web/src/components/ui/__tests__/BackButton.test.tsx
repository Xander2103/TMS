import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { BackButton } from '../BackButton'

describe('BackButton', () => {
  it('renders a link to the given destination with the default label', () => {
    render(
      <MemoryRouter>
        <BackButton to="/customers" />
      </MemoryRouter>,
    )
    const link = screen.getByRole('link', { name: /Terug/ })
    expect(link).toHaveAttribute('href', '/customers')
  })

  it('supports a custom label', () => {
    render(
      <MemoryRouter>
        <BackButton to="/transport-orders" label="Terug naar orders" />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Terug naar orders' })).toBeInTheDocument()
  })
})
