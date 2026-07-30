import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IncomingTab } from '../IncomingTab'
import type { PeppolIncomingDocument } from '../../api/peppolApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listPeppolIncoming: vi.fn(),
  reviewPeppolIncoming: vi.fn(),
  rejectPeppolIncoming: vi.fn(),
}))
vi.mock('../../api/peppolApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/peppolApi')>()),
  listPeppolIncoming: api.listPeppolIncoming,
  reviewPeppolIncoming: api.reviewPeppolIncoming,
  rejectPeppolIncoming: api.rejectPeppolIncoming,
}))

function doc(overrides: Partial<PeppolIncomingDocument> = {}): PeppolIncomingDocument {
  return {
    id: 'in-1',
    documentKind: 'Invoice',
    supplierParticipant: '0208:BE0123456789',
    supplierName: 'Leverancier NV',
    documentNumber: 'LEV-2026-042',
    documentDate: '2026-07-28',
    amount: 500,
    currency: 'EUR',
    status: 'Received',
    reviewNote: null,
    receivedAt: '2026-07-30T09:00:00Z',
    ...overrides,
  }
}

describe('IncomingTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listPeppolIncoming.mockResolvedValue({ items: [doc()], totalCount: 1, page: 1, pageSize: 25 })
  })

  it('renders incoming documents with the Dutch status label', async () => {
    render(<IncomingTab />)

    expect(await screen.findByText('Leverancier NV')).toBeInTheDocument()
    const table = within(screen.getByRole('table'))
    expect(table.getByText('LEV-2026-042')).toBeInTheDocument()
    // "Ontvangen" is zowel kolomkop als statuslabel; het badge-element is de statuscel.
    expect(table.getAllByText('Ontvangen').length).toBeGreaterThan(1)
  })

  it('marks a document as processed via /review with the note', async () => {
    const user = userEvent.setup()
    api.reviewPeppolIncoming.mockResolvedValue(doc({ status: 'Linked' }))
    render(<IncomingTab />)

    await user.click(await screen.findByRole('button', { name: 'Markeer als verwerkt' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Notitie (optioneel)'), 'Geboekt in de boekhouding')
    await user.click(within(dialog).getByRole('button', { name: 'Markeer als verwerkt' }))

    await waitFor(() => expect(api.reviewPeppolIncoming).toHaveBeenCalledWith('in-1', 'Geboekt in de boekhouding'))
  })

  it('rejects a document via /reject', async () => {
    const user = userEvent.setup()
    api.rejectPeppolIncoming.mockResolvedValue(doc({ status: 'Rejected' }))
    render(<IncomingTab />)

    await user.click(await screen.findByRole('button', { name: 'Afwijzen' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Afwijzen' }))

    await waitFor(() => expect(api.rejectPeppolIncoming).toHaveBeenCalledWith('in-1', null))
  })

  it('hides the actions for a Linked document', async () => {
    api.listPeppolIncoming.mockResolvedValue({
      items: [doc({ status: 'Linked', reviewNote: 'Verwerkt' })],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    render(<IncomingTab />)

    await screen.findByText('LEV-2026-042')
    expect(screen.queryByRole('button', { name: 'Markeer als verwerkt' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Afwijzen' })).not.toBeInTheDocument()
  })
})
