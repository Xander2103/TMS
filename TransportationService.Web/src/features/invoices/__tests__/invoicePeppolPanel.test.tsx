import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InvoicePeppolPanel } from '../components/InvoicePeppolPanel'
import type { PeppolTransmission } from '../../peppol/api/peppolApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.has(code)),
  }),
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listInvoicePeppolTransmissions: vi.fn(),
  validateInvoicePeppol: vi.fn(),
  getInvoicePeppolPreview: vi.fn(),
  sendInvoicePeppol: vi.fn(),
  downloadInvoicePeppolXml: vi.fn(),
}))
vi.mock('../../peppol/api/peppolApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../peppol/api/peppolApi')>()),
  listInvoicePeppolTransmissions: api.listInvoicePeppolTransmissions,
  validateInvoicePeppol: api.validateInvoicePeppol,
  getInvoicePeppolPreview: api.getInvoicePeppolPreview,
  sendInvoicePeppol: api.sendInvoicePeppol,
  downloadInvoicePeppolXml: api.downloadInvoicePeppolXml,
}))

function transmission(overrides: Partial<PeppolTransmission> = {}): PeppolTransmission {
  return {
    id: 'tx-1',
    invoiceId: 'inv-1',
    documentKind: 'Invoice',
    status: 'Delivered',
    environment: 'Sandbox',
    providerKey: 'sandbox',
    providerMessageId: 'prov-1',
    payloadVersion: 1,
    payloadHash: 'abc',
    errorDetail: null,
    retryCount: 0,
    createdAt: '2026-07-30T10:00:00Z',
    events: [{ status: 'Delivered', timestamp: '2026-07-30T10:05:00Z', detail: null }],
    ...overrides,
  }
}

describe('InvoicePeppolPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['peppol.view', 'peppol.validate', 'peppol.send'])
    api.listInvoicePeppolTransmissions.mockResolvedValue([])
  })

  it('shows the latest transmission status with provider reference', async () => {
    api.listInvoicePeppolTransmissions.mockResolvedValue([transmission()])
    render(<InvoicePeppolPanel invoiceId="inv-1" invoiceNumber="F2026-0001" invoiceStatus="Sent" />)

    expect((await screen.findAllByText('Afgeleverd')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('prov-1').length).toBeGreaterThan(0)
  })

  it('validate shows the issue list', async () => {
    const user = userEvent.setup()
    api.validateInvoicePeppol.mockResolvedValue({
      isValid: false,
      issues: [
        { code: 'seller_peppol_id_missing', message: 'Het eigen bedrijf heeft geen Peppol-ID.' },
        { code: 'buyer_vat_missing', message: 'De klant heeft geen BTW-nummer.' },
      ],
    })
    render(<InvoicePeppolPanel invoiceId="inv-1" invoiceNumber="F2026-0001" invoiceStatus="Draft" />)

    await user.click(await screen.findByRole('button', { name: 'Valideren' }))

    expect(await screen.findByText('Het eigen bedrijf heeft geen Peppol-ID.')).toBeInTheDocument()
    expect(screen.getByText('De klant heeft geen BTW-nummer.')).toBeInTheDocument()
    expect(api.validateInvoicePeppol).toHaveBeenCalledWith('inv-1')
  })

  it('validate shows "Klaar voor verzending" when valid', async () => {
    const user = userEvent.setup()
    api.validateInvoicePeppol.mockResolvedValue({ isValid: true, issues: [] })
    render(<InvoicePeppolPanel invoiceId="inv-1" invoiceNumber="F2026-0001" invoiceStatus="Sent" />)

    await user.click(await screen.findByRole('button', { name: 'Valideren' }))

    expect(await screen.findByText('Klaar voor verzending')).toBeInTheDocument()
  })

  it('disables the send button for a Draft invoice', async () => {
    render(<InvoicePeppolPanel invoiceId="inv-1" invoiceNumber="F2026-0001" invoiceStatus="Draft" />)

    expect(await screen.findByRole('button', { name: 'Versturen via Peppol' })).toBeDisabled()
  })

  it('sends a Sent invoice via the send endpoint and refreshes the list', async () => {
    const user = userEvent.setup()
    api.sendInvoicePeppol.mockResolvedValue(transmission({ status: 'Queued' }))
    render(<InvoicePeppolPanel invoiceId="inv-1" invoiceNumber="F2026-0001" invoiceStatus="Sent" />)

    const sendButton = await screen.findByRole('button', { name: 'Versturen via Peppol' })
    await waitFor(() => expect(sendButton).toBeEnabled())
    await user.click(sendButton)

    await waitFor(() => expect(api.sendInvoicePeppol).toHaveBeenCalledWith('inv-1'))
    await waitFor(() => expect(api.listInvoicePeppolTransmissions).toHaveBeenCalledTimes(2))
  })

  it('hides the send button without peppol.send', async () => {
    auth.permissions = new Set(['peppol.view'])
    render(<InvoicePeppolPanel invoiceId="inv-1" invoiceNumber="F2026-0001" invoiceStatus="Sent" />)

    await screen.findByRole('button', { name: 'Voorbeeld' })
    expect(screen.queryByRole('button', { name: 'Versturen via Peppol' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Valideren' })).not.toBeInTheDocument()
  })
})
