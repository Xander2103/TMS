import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { DataTable } from '../../ui/DataTable'
import { ErrorState } from '../ErrorState'

// SEC-M5: a load failure follows its HTTP status — a permission refusal says "Geen toegang", a
// missing record "Niet gevonden", and "Opnieuw proberen" only appears where a retry can help.

function renderState(error: unknown, onRetry = vi.fn()) {
  render(
    <MemoryRouter>
      <ErrorState message="Gegevens konden niet worden geladen." error={error} onRetry={onRetry} backTo="/lijst" />
    </MemoryRouter>,
  )
  return onRetry
}

const retryButton = () => screen.queryByRole('button', { name: 'Opnieuw proberen' })

describe('ErrorState', () => {
  it('renders "Geen toegang" for a 403, without the server text or a retry', () => {
    renderState(new ApiError('Missing permission: operations.view', 403))
    expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Naar het startscherm' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('link', { name: 'Terug naar de lijst' })).toHaveAttribute('href', '/lijst')
    expect(screen.queryByText(/Missing permission/)).not.toBeInTheDocument()
    expect(screen.queryByText('Gegevens konden niet worden geladen.')).not.toBeInTheDocument()
    expect(retryButton()).not.toBeInTheDocument()
  })

  it('renders "Niet gevonden" for a 404 with the way back, without a retry', () => {
    renderState(new ApiError('Not found', 404))
    expect(screen.getByRole('heading', { name: 'Niet gevonden' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Terug naar de lijst' })).toBeInTheDocument()
    expect(retryButton()).not.toBeInTheDocument()
  })

  it.each([400, 409, 422])('offers no retry for a %i', (status) => {
    renderState(new ApiError('Bad request', status))
    expect(screen.getByText('Gegevens konden niet worden geladen.')).toBeInTheDocument()
    expect(retryButton()).not.toBeInTheDocument()
  })

  it.each([500, 503, 408, 429])('keeps "Opnieuw proberen" for a %i', async (status) => {
    const onRetry = renderState(new ApiError('Server error', status))
    expect(screen.getByText('Gegevens konden niet worden geladen.')).toBeInTheDocument()
    await userEvent.click(retryButton()!)
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('keeps "Opnieuw proberen" for a network failure (no status)', () => {
    renderState(new ApiError('Unable to reach /api/operations/overview'))
    expect(retryButton()).toBeInTheDocument()
  })

  it('renders exactly as before without an error object', () => {
    render(
      <MemoryRouter>
        <ErrorState message="Kon niet laden." onRetry={() => {}} />
      </MemoryRouter>,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('Kon niet laden.')
    expect(retryButton()).toBeInTheDocument()
  })
})

describe('DataTable error state', () => {
  it('passes the error cause on, so a refused list says "Geen toegang" without a retry', () => {
    render(
      <MemoryRouter>
        <DataTable
          columns={[{ key: 'name', header: 'Naam', render: (row: { id: string }) => row.id }]}
          rows={[]}
          rowKey={(row) => row.id}
          error="Locaties konden niet worden geladen."
          errorCause={new ApiError('Forbidden', 403)}
          onRetry={() => {}}
        />
      </MemoryRouter>,
    )
    expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
    expect(retryButton()).not.toBeInTheDocument()
  })
})
