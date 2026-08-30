import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderDocumentsPanel } from '../OrderDocumentsPanel'
import type { OrderDocument } from '../../api/orderDocumentsApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['orders.view', 'orders.manage']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const api = vi.hoisted(() => ({
  list: vi.fn(),
  create: vi.fn(),
  update: vi.fn(),
  upload: vi.fn(),
  remove: vi.fn(),
  download: vi.fn(),
}))
vi.mock('../../api/orderDocumentsApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/orderDocumentsApi')>()),
  listOrderDocuments: api.list,
  createOrderDocument: api.create,
  updateOrderDocument: api.update,
  uploadOrderDocumentFile: api.upload,
  deleteOrderDocument: api.remove,
  downloadOrderDocumentFile: api.download,
}))

function doc(overrides: Partial<OrderDocument> = {}): OrderDocument {
  return {
    id: 'd1',
    transportOrderId: 'o1',
    documentType: 'Cmr',
    customTypeName: null,
    title: 'CMR',
    hasAttachment: true,
    fileName: 'cmr.pdf',
    issueDate: null,
    notes: null,
    customerVisible: false,
    ...overrides,
  }
}

/**
 * H-14: order documents are internal by default and only reach the customer portal when the
 * uploader publishes them deliberately. The server enforces it; these tests pin the UI default
 * and the toggle so nobody re-introduces "share everything".
 */
describe('OrderDocumentsPanel customer visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['orders.view', 'orders.manage'])
  })

  it('uploads as internal unless the planner ticks "Zichtbaar voor de klant"', async () => {
    api.list.mockResolvedValue([])
    api.create.mockResolvedValue(doc({ id: 'new' }))
    api.upload.mockResolvedValue(doc({ id: 'new' }))
    const user = userEvent.setup()
    const { container } = render(<OrderDocumentsPanel orderId="o1" />)
    await screen.findByText('Nog geen documenten bij deze opdracht.')

    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement
    await user.upload(fileInput, new File(['x'], 'cmr.pdf', { type: 'application/pdf' }))

    await waitFor(() => expect(api.create).toHaveBeenCalled())
    expect(api.create.mock.calls[0][1]).toMatchObject({ customerVisible: false })
  })

  it('uploads as customer-visible once the checkbox is ticked', async () => {
    api.list.mockResolvedValue([])
    api.create.mockResolvedValue(doc({ id: 'new', customerVisible: true }))
    api.upload.mockResolvedValue(doc({ id: 'new', customerVisible: true }))
    const user = userEvent.setup()
    const { container } = render(<OrderDocumentsPanel orderId="o1" />)
    await screen.findByText('Nog geen documenten bij deze opdracht.')

    await user.click(screen.getByLabelText('Zichtbaar voor de klant'))
    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement
    await user.upload(fileInput, new File(['x'], 'cmr.pdf', { type: 'application/pdf' }))

    await waitFor(() => expect(api.create).toHaveBeenCalled())
    expect(api.create.mock.calls[0][1]).toMatchObject({ customerVisible: true })
  })

  it('publishes an existing document through the row toggle', async () => {
    api.list.mockResolvedValue([doc()])
    api.update.mockResolvedValue(doc({ customerVisible: true }))
    const user = userEvent.setup()
    render(<OrderDocumentsPanel orderId="o1" />)

    const toggle = await screen.findByLabelText('Zichtbaar voor de klant: CMR')
    expect(toggle).not.toBeChecked()

    await user.click(toggle)

    await waitFor(() => expect(api.update).toHaveBeenCalled())
    expect(api.update.mock.calls[0][0]).toBe('d1')
    expect(api.update.mock.calls[0][1]).toMatchObject({ customerVisible: true, title: 'CMR' })
  })

  it('shows a read-only state to orders.create-only users, who cannot call the update endpoint', async () => {
    // PUT /api/order-documents/{id} requires orders.edit or orders.manage; offering an enabled
    // checkbox to orders.create would just 403.
    auth.permissions = new Set(['orders.view', 'orders.create'])
    api.list.mockResolvedValue([doc()])
    render(<OrderDocumentsPanel orderId="o1" />)

    expect(await screen.findByText('Intern')).toBeInTheDocument()
    expect(screen.queryByLabelText('Zichtbaar voor de klant: CMR')).not.toBeInTheDocument()
  })
})
