import { apiClient } from '../../../api/apiClient'

/**
 * Issued transport documents (master sprint 2026-09-21, D6 / contract 4.3): a consignment note,
 * delivery note or work order with a UNIQUE server-issued number. Several per order are allowed.
 * The client never invents a number — it only shows what the server returned.
 */
export type IssuedTransportDocumentKind = 'Cmr' | 'DeliveryNote' | 'WorkOrder'

export const ISSUED_DOCUMENT_KINDS: readonly IssuedTransportDocumentKind[] = ['Cmr', 'DeliveryNote', 'WorkOrder']

export interface IssuedTransportDocument {
  id: string
  transportOrderId: string
  kind: IssuedTransportDocumentKind
  documentNumber: string
  externalNumber: string | null
  issuedAt: string
  issuedByName: string | null
}

export interface IssueTransportDocumentInput {
  kind: IssuedTransportDocumentKind
  /** Idempotency key: the same requestId returns the same record, never a second number. */
  requestId: string
  externalNumber?: string | null
}

export const listIssuedTransportDocuments = (orderId: string): Promise<IssuedTransportDocument[]> =>
  apiClient.getJson(`/api/transport-orders/${orderId}/issued-documents`)

export const issueTransportDocument = (orderId: string, input: IssueTransportDocumentInput): Promise<IssuedTransportDocument> =>
  apiClient.postJson(`/api/transport-orders/${orderId}/issued-documents`, input)

export const downloadIssuedTransportDocumentPdf = (document: Pick<IssuedTransportDocument, 'id' | 'documentNumber'>): Promise<void> =>
  apiClient.downloadFile(`/api/issued-transport-documents/${document.id}/pdf`, `${document.documentNumber}.pdf`)
