import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

/** Mirrors InvoiceAttachmentDto (camelCase JSON). */
export interface InvoiceAttachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  includeWhenSending: boolean
  notes: string | null
  uploadedAt: string
  uploadedByUserId: string | null
}

export interface UpdateInvoiceAttachmentInput {
  includeWhenSending: boolean
  notes: string | null
}

/** Toegelaten extensies + maximumgrootte, gespiegeld aan InvoiceAttachmentsController. */
export const INVOICE_ATTACHMENT_ACCEPT = '.pdf,.xml,.xlsx,.xls,.csv,.jpg,.jpeg,.png'
export const MAX_INVOICE_ATTACHMENT_BYTES = 10 * 1024 * 1024

export function listInvoiceAttachments(invoiceId: string): Promise<InvoiceAttachment[]> {
  return apiClient.getJson<InvoiceAttachment[]>(`/api/invoices/${invoiceId}/attachments`)
}

export function updateInvoiceAttachment(
  invoiceId: string,
  attachmentId: string,
  input: UpdateInvoiceAttachmentInput,
): Promise<InvoiceAttachment> {
  return apiClient.putJson<InvoiceAttachment, UpdateInvoiceAttachmentInput>(
    `/api/invoices/${invoiceId}/attachments/${attachmentId}`,
    input,
  )
}

export function deleteInvoiceAttachment(invoiceId: string, attachmentId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/invoices/${invoiceId}/attachments/${attachmentId}`)
}

// Multipart upload and blob download fall outside the JSON apiClient; both attach the
// bearer token manually (same idiom as the qualification-document upload).
export async function uploadInvoiceAttachment(invoiceId: string, file: File): Promise<InvoiceAttachment> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/invoices/${invoiceId}/attachments`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    let message = 'De bijlage kon niet worden geüpload.'
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as InvoiceAttachment
}

export async function downloadInvoiceAttachment(
  invoiceId: string,
  attachmentId: string,
  fileName: string,
): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/invoices/${invoiceId}/attachments/${attachmentId}/content`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('De bijlage kon niet worden gedownload.', response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
