import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { InvoiceHistoryPanel } from '../components/InvoiceHistoryPanel'

const api = vi.hoisted(() => ({ getInvoiceHistory: vi.fn() }))
vi.mock('../api/invoicesApi', () => api)

// ACC-M7: the invoice's story — WHEN / WHO / WHAT — rendered from the server's Dutch sentences.
describe('InvoiceHistoryPanel', () => {
  it('renders user and system events with their actors, oldest first', async () => {
    api.getInvoiceHistory.mockResolvedValue([
      { id: 'audit:1', timestamp: '2026-10-17T12:54:05Z', actorKind: 'User', actorName: 'Bea Boekhouding', type: 'invoice.created', title: 'Conceptfactuur aangemaakt', detail: '6 lijnen' },
      { id: 'audit:2', timestamp: '2026-10-17T13:03:13Z', actorKind: 'User', actorName: 'Bea Boekhouding', type: 'invoice.issued', title: 'Factuur definitief gemaakt en verzonden', detail: 'Nummer FAC-2026100006' },
      { id: 'peppol:3', timestamp: '2026-10-17T15:29:00Z', actorKind: 'System', actorName: null, type: 'peppol.delivered', title: 'Factuur afgeleverd via Peppol', detail: 'Provider-referentie sbx-1' },
    ])
    render(<InvoiceHistoryPanel invoiceId="inv-1" />)
    expect(await screen.findByText('Conceptfactuur aangemaakt door Bea Boekhouding')).toBeInTheDocument()
    expect(screen.getByText('Factuur definitief gemaakt en verzonden door Bea Boekhouding')).toBeInTheDocument()
    expect(screen.getByText('Factuur afgeleverd via Peppol door Systeem')).toBeInTheDocument()
    expect(screen.getByText('Provider-referentie sbx-1')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Historiek' })).toBeInTheDocument()
  })

  it('shows the empty state and the load error', async () => {
    api.getInvoiceHistory.mockResolvedValueOnce([])
    const { unmount } = render(<InvoiceHistoryPanel invoiceId="inv-1" />)
    expect(await screen.findByText('Nog geen gebeurtenissen voor deze factuur.')).toBeInTheDocument()
    unmount()
    api.getInvoiceHistory.mockRejectedValueOnce(new Error('boom'))
    render(<InvoiceHistoryPanel invoiceId="inv-2" />)
    expect(await screen.findByText('De historiek kon niet worden geladen.')).toBeInTheDocument()
  })
})
