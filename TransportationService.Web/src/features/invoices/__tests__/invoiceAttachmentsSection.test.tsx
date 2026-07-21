import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { InvoiceAttachmentsSection } from '../pages/InvoiceDetailPage'
import type { InvoiceAttachment } from '../api/invoiceAttachmentsApi'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const attachments = vi.hoisted(() => ({ value: [] as InvoiceAttachment[] }))

vi.mock('../api/invoiceAttachmentsApi', () => ({
  INVOICE_ATTACHMENT_ACCEPT: '.pdf,.xml',
  MAX_INVOICE_ATTACHMENT_BYTES: 10 * 1024 * 1024,
  listInvoiceAttachments: () => Promise.resolve(attachments.value),
  updateInvoiceAttachment: vi.fn(),
  deleteInvoiceAttachment: vi.fn(),
  uploadInvoiceAttachment: vi.fn(),
  downloadInvoiceAttachment: vi.fn(),
}))

function makeAttachment(overrides: Partial<InvoiceAttachment>): InvoiceAttachment {
  return {
    id: 'att-1',
    fileName: 'cmr-0001.pdf',
    contentType: 'application/pdf',
    sizeBytes: 2048,
    includeWhenSending: true,
    notes: null,
    uploadedAt: '2026-07-21T10:00:00Z',
    uploadedByUserId: null,
    ...overrides,
  }
}

describe('InvoiceAttachmentsSection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    attachments.value = [makeAttachment({})]
  })

  it('renders the attachment list', async () => {
    render(<InvoiceAttachmentsSection invoiceId="inv-1" canManage isDraft />)

    expect(await screen.findByText('cmr-0001.pdf')).toBeInTheDocument()
    expect(screen.getByText('Download')).toBeInTheDocument()
  })

  it('shows Verwijderen for a draft invoice with manage rights', async () => {
    render(<InvoiceAttachmentsSection invoiceId="inv-1" canManage isDraft />)

    expect(await screen.findByText('cmr-0001.pdf')).toBeInTheDocument()
    expect(screen.getByText('Verwijderen')).toBeInTheDocument()
  })

  it('hides Verwijderen when the invoice is not a draft', async () => {
    render(<InvoiceAttachmentsSection invoiceId="inv-1" canManage isDraft={false} />)

    expect(await screen.findByText('cmr-0001.pdf')).toBeInTheDocument()
    expect(screen.queryByText('Verwijderen')).not.toBeInTheDocument()
  })
})
