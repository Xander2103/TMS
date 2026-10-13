import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '../../../../api/apiClient'
import type { IssuedTransportDocument } from '../../api/issuedDocumentsApi'
import { IssuedTransportDocumentsBlock } from '../IssuedTransportDocumentsBlock'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({ list: vi.fn(), issue: vi.fn(), pdf: vi.fn() }))
vi.mock('../../api/issuedDocumentsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/issuedDocumentsApi')>()),
  listIssuedTransportDocuments: api.list,
  issueTransportDocument: api.issue,
  downloadIssuedTransportDocumentPdf: api.pdf,
}))

const strategy = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('../../api/transportDocumentsApi', () => ({ getOrderDocumentStrategy: strategy.get }))

function issued(overrides: Partial<IssuedTransportDocument> = {}): IssuedTransportDocument {
  return {
    id: 'i-1',
    transportOrderId: 'o-1',
    kind: 'Cmr',
    documentNumber: 'CMR-2026-00051',
    externalNumber: null,
    issuedAt: '2026-09-21T08:30:00Z',
    issuedByName: 'Planner',
    ...overrides,
  }
}

describe('IssuedTransportDocumentsBlock', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['orders.view', 'orders.edit'])
    api.list.mockResolvedValue([])
    strategy.get.mockResolvedValue({ kind: 'Cmr' })
  })

  it('shows the empty state and never a number the server did not issue', async () => {
    render(<IssuedTransportDocumentsBlock orderId="o-1" />)
    expect(await screen.findByText('Nog geen transportdocument aangemaakt.')).toBeInTheDocument()
    expect(screen.queryByText(/CMR-/)).not.toBeInTheDocument()
  })

  it('lists issued documents with their count and a PDF link each', async () => {
    const user = userEvent.setup()
    api.list.mockResolvedValue([issued(), issued({ id: 'i-2', documentNumber: 'CMR-2026-00052', externalNumber: 'NL-778' })])
    api.pdf.mockResolvedValue(undefined)
    render(<IssuedTransportDocumentsBlock orderId="o-1" />)

    expect(await screen.findByText('CMR-2026-00051')).toBeInTheDocument()
    expect(screen.getByText('CMR-2026-00052')).toBeInTheDocument()
    expect(screen.getByText('extern nr. NL-778')).toBeInTheDocument()
    expect(within(screen.getByRole('heading', { name: /Transportdocumenten/ })).getByText('2')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'PDF van CMR-2026-00052 downloaden' }))
    expect(api.pdf).toHaveBeenCalledWith(expect.objectContaining({ id: 'i-2' }))
  })

  it('a double click issues ONE document with ONE requestId, and shows the returned number', async () => {
    const user = userEvent.setup()
    let resolve: (doc: IssuedTransportDocument) => void = () => {}
    api.issue.mockReturnValue(new Promise<IssuedTransportDocument>((r) => (resolve = r)))
    render(<IssuedTransportDocumentsBlock orderId="o-1" />)
    const button = await screen.findByRole('button', { name: 'Vrachtbrief aanmaken' })

    await user.dblClick(button)
    expect(api.issue).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Bezig...' })).toBeDisabled()
    const sent = api.issue.mock.calls[0]
    expect(sent[0]).toBe('o-1')
    expect(sent[1]).toMatchObject({ kind: 'Cmr', externalNumber: null })
    expect(sent[1].requestId).toMatch(/^[0-9a-f-]{36}$/)

    resolve(issued({ documentNumber: 'CMR-2026-00077' }))
    expect(await screen.findByText('CMR-2026-00077')).toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalledWith('Transportdocument CMR-2026-00077 aangemaakt.')
  })

  it('shows the error on failure and retries the same intent with the same requestId', async () => {
    const user = userEvent.setup()
    api.issue.mockRejectedValueOnce(new ApiError('Nummerreeks niet geconfigureerd.', 409)).mockResolvedValueOnce(issued())
    render(<IssuedTransportDocumentsBlock orderId="o-1" />)

    await user.type(await screen.findByLabelText('Extern / voorgedrukt nummer (optioneel)'), 'NL-778')
    await user.click(screen.getByRole('button', { name: 'Vrachtbrief aanmaken' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Nummerreeks niet geconfigureerd.')
    expect(toast.showSuccess).not.toHaveBeenCalled()
    expect(screen.queryByText(/CMR-2026/)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Vrachtbrief aanmaken' }))
    await screen.findByText('CMR-2026-00051')
    expect(api.issue).toHaveBeenCalledTimes(2)
    expect(api.issue.mock.calls[1][1]).toEqual(api.issue.mock.calls[0][1])
    expect(api.issue.mock.calls[0][1].externalNumber).toBe('NL-778')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('uses a fresh requestId for the next document', async () => {
    const user = userEvent.setup()
    api.issue.mockResolvedValueOnce(issued()).mockResolvedValueOnce(issued({ id: 'i-2', documentNumber: 'CMR-2026-00052' }))
    render(<IssuedTransportDocumentsBlock orderId="o-1" />)

    await user.click(await screen.findByRole('button', { name: 'Vrachtbrief aanmaken' }))
    await screen.findByText('CMR-2026-00051')
    await user.click(screen.getByRole('button', { name: 'Vrachtbrief aanmaken' }))
    await screen.findByText('CMR-2026-00052')
    expect(api.issue.mock.calls[1][1].requestId).not.toBe(api.issue.mock.calls[0][1].requestId)
  })

  it('offers "Werkbon aanmaken" for an on-site lifting job or a WorkOrder strategy', async () => {
    const { unmount } = render(<IssuedTransportDocumentsBlock orderId="o-1" craneJobKind="OnSiteLifting" />)
    expect(await screen.findByRole('button', { name: 'Werkbon aanmaken' })).toBeInTheDocument()
    unmount()

    strategy.get.mockResolvedValue({ kind: 'WorkOrder' })
    render(<IssuedTransportDocumentsBlock orderId="o-2" />)
    expect(await screen.findByRole('button', { name: 'Werkbon aanmaken' })).toBeInTheDocument()
  })

  it('hides issuing without orders.edit/orders.manage', async () => {
    auth.permissions = new Set(['orders.view'])
    api.list.mockResolvedValue([issued()])
    render(<IssuedTransportDocumentsBlock orderId="o-1" />)

    expect(await screen.findByText('CMR-2026-00051')).toBeInTheDocument()
    await waitFor(() => expect(strategy.get).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: /aanmaken/ })).not.toBeInTheDocument()
  })
})
