import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OutgoingTab } from '../OutgoingTab'
import type { PeppolTransmissionRow } from '../../api/peppolApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listPeppolTransmissions: vi.fn(),
  retryPeppolTransmission: vi.fn(),
  cancelPeppolTransmission: vi.fn(),
}))
vi.mock('../../api/peppolApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/peppolApi')>()),
  listPeppolTransmissions: api.listPeppolTransmissions,
  retryPeppolTransmission: api.retryPeppolTransmission,
  cancelPeppolTransmission: api.cancelPeppolTransmission,
}))

function row(overrides: Partial<PeppolTransmissionRow> = {}): PeppolTransmissionRow {
  return {
    id: 'tx-1',
    invoiceId: 'inv-1',
    invoiceNumber: 'F2026-0001',
    documentKind: 'Invoice',
    customerName: 'Haven BV',
    total: 1210,
    currency: 'EUR',
    invoiceDate: '2026-07-01',
    status: 'Failed',
    environment: 'Sandbox',
    providerMessageId: 'prov-1',
    errorDetail: 'Ontvanger niet gevonden.',
    retryCount: 1,
    payloadVersion: 2,
    createdAt: '2026-07-30T10:00:00Z',
    ...overrides,
  }
}

describe('OutgoingTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listPeppolTransmissions.mockResolvedValue({ items: [row()], totalCount: 1, page: 1, pageSize: 25 })
  })

  it('renders the transmission rows with Dutch status labels', async () => {
    render(<OutgoingTab canRetry canCancel />)

    expect(await screen.findByText('F2026-0001')).toBeInTheDocument()
    const table = within(screen.getByRole('table'))
    expect(table.getByText('Haven BV')).toBeInTheDocument()
    expect(table.getByText('Mislukt')).toBeInTheDocument()
    expect(table.getByText('prov-1')).toBeInTheDocument()
    expect(table.getByText('Ontvanger niet gevonden.')).toBeInTheDocument()
    expect(table.getByText('2× / v2')).toBeInTheDocument()
  })

  it('shows Opnieuw for a Failed row when canRetry is true and calls the retry endpoint', async () => {
    const user = userEvent.setup()
    api.retryPeppolTransmission.mockResolvedValue(row({ status: 'Queued' }))
    render(<OutgoingTab canRetry canCancel={false} />)

    await user.click(await screen.findByRole('button', { name: 'Opnieuw' }))

    await waitFor(() => expect(api.retryPeppolTransmission).toHaveBeenCalledWith('tx-1'))
  })

  it('hides Opnieuw without canRetry', async () => {
    render(<OutgoingTab canRetry={false} canCancel={false} />)

    await screen.findByText('F2026-0001')
    expect(screen.queryByRole('button', { name: 'Opnieuw' })).not.toBeInTheDocument()
  })

  it('does not show Opnieuw for a Delivered row even with canRetry', async () => {
    api.listPeppolTransmissions.mockResolvedValue({
      items: [row({ status: 'Delivered', errorDetail: null })],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    render(<OutgoingTab canRetry canCancel />)

    await screen.findByText('F2026-0001')
    expect(screen.queryByRole('button', { name: 'Opnieuw' })).not.toBeInTheDocument()
  })

  it('shows Annuleren only for Queued rows with canCancel and cancels after confirmation', async () => {
    const user = userEvent.setup()
    api.listPeppolTransmissions.mockResolvedValue({
      items: [row({ status: 'Queued', errorDetail: null })],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })
    api.cancelPeppolTransmission.mockResolvedValue(row({ status: 'Cancelled' }))
    render(<OutgoingTab canRetry canCancel />)

    await user.click(await screen.findByRole('button', { name: 'Annuleren' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toBeInTheDocument()

    const confirm = screen.getAllByRole('button', { name: 'Annuleren' }).at(-1)!
    await user.click(confirm)

    await waitFor(() => expect(api.cancelPeppolTransmission).toHaveBeenCalledWith('tx-1'))
  })

  it('passes the status filter through as a query param', async () => {
    const user = userEvent.setup()
    render(<OutgoingTab canRetry canCancel />)
    await screen.findByText('F2026-0001')

    await user.selectOptions(screen.getByLabelText('Status'), 'Delivered')

    await waitFor(() =>
      expect(api.listPeppolTransmissions).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Delivered' })),
    )
  })
})
