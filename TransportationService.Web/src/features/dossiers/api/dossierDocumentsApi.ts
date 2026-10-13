import { apiClient } from '../../../api/apiClient'
import type { OrderDocumentType } from '../../transport-orders/api/orderDocumentsApi'

/**
 * Dossier documents (master sprint 2026-09-21, D6 / contract 4.2). ONE entity, ONE file: a document
 * hangs on the dossier as a whole (`scope: 'Dossier'`), on one of its orders (`'Order'`) or — dossier
 * UX correction sprint 2026-10-08 — on one of its STANDALONE activities (`'Activity'`,
 * `dossierActivityId` set, no order). The dossier list returns every scope, so every view is derived
 * from that single list — a dossier document is never copied to an order or an activity.
 */
export type DossierDocumentScope = 'Dossier' | 'Order' | 'Activity'

export interface DossierDocument {
  id: string
  dossierId: string
  transportOrderId: string | null
  orderNumber: string | null
  /** The standalone activity the document belongs to; null for dossier-wide and order documents. */
  dossierActivityId: string | null
  /** Planner label of that activity, or its type name (there is no activity number). */
  activityLabel: string | null
  scope: DossierDocumentScope
  documentType: OrderDocumentType
  customTypeName: string | null
  title: string
  fileName: string | null
  contentType: string | null
  issueDate: string | null
  notes: string | null
  /** Published in the customer portal — independent of the level the document hangs on. */
  customerVisible: boolean
  hasFile: boolean
  createdAt: string
  createdByName: string | null
}

export interface CreateDossierDocumentInput {
  /** The order the document belongs to; null together with `dossierActivityId` null = document of the whole dossier. */
  transportOrderId: string | null
  /** The standalone activity the document belongs to (never together with an order). */
  dossierActivityId: string | null
  documentType: OrderDocumentType
  customTypeName: string | null
  title: string
  issueDate: string | null
  notes: string | null
  customerVisible: boolean
}

/** PUT body of the existing `api/order-documents/{id}` route. `customerVisible` is tri-state: null = leave unchanged. */
export interface UpdateDossierDocumentInput {
  documentType: OrderDocumentType
  customTypeName: string | null
  title: string
  issueDate: string | null
  notes: string | null
  customerVisible: boolean | null
}

export const DOSSIER_DOCUMENT_MAX_BYTES = 10 * 1024 * 1024
export const DOSSIER_DOCUMENT_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png'] as const

const documentFilePath = (id: string) => `/api/order-documents/${id}/document`

export const listDossierDocuments = (dossierId: string): Promise<DossierDocument[]> =>
  apiClient.getJson(`/api/dossiers/${dossierId}/documents`)

export const createDossierDocument = (dossierId: string, input: CreateDossierDocumentInput): Promise<DossierDocument> =>
  apiClient.postJson(`/api/dossiers/${dossierId}/documents`, input)

/** Where a document is moved to inside its dossier: an order, a standalone activity, or (both null) the dossier level. */
export interface DossierDocumentMoveTarget {
  targetTransportOrderId: string | null
  targetDossierActivityId: string | null
}

/** Moves the record to another level of the SAME dossier. The file is not touched. */
export const moveDossierDocument = (id: string, target: DossierDocumentMoveTarget): Promise<DossierDocument> =>
  apiClient.postJson(`/api/order-documents/${id}/move`, target)

/**
 * Metadata update through the existing route. The response shape of that shared route is not part of
 * contract 4.2 — callers read nothing but `customerVisible` from it.
 */
export const updateDossierDocument = (id: string, input: UpdateDossierDocumentInput): Promise<{ customerVisible: boolean }> =>
  apiClient.putJson(`/api/order-documents/${id}`, input)

/**
 * Publishes to / withdraws from the customer portal. The PUT replaces the metadata, so the
 * document's current values travel along untouched and only the flag changes.
 */
export const setDossierDocumentCustomerVisible = (document: DossierDocument, customerVisible: boolean): Promise<{ customerVisible: boolean }> =>
  updateDossierDocument(document.id, {
    documentType: document.documentType,
    customTypeName: document.customTypeName,
    title: document.title,
    issueDate: document.issueDate,
    notes: document.notes,
    customerVisible,
  })

export const deleteDossierDocument = (id: string): Promise<void> => apiClient.deleteRequest(`/api/order-documents/${id}`)

/** Download through the shared auth path (bearer + one refresh-and-retry on 401). */
export const downloadDossierDocumentFile = (document: DossierDocument): Promise<void> =>
  apiClient.downloadFile(documentFilePath(document.id), document.fileName ?? document.title)

/** The file as a blob, for "Bekijken" in a new tab — same auth path as the download. */
export const fetchDossierDocumentBlob = async (document: DossierDocument): Promise<Blob> =>
  (await apiClient.fetchBlob(documentFilePath(document.id))).blob

/** True when the browser can show the file itself (pdf / image); anything else is download-only. */
export function isViewableDocument(document: Pick<DossierDocument, 'hasFile' | 'contentType' | 'fileName'>): boolean {
  if (!document.hasFile) return false
  const type = document.contentType?.toLowerCase() ?? ''
  if (type === 'application/pdf' || type.startsWith('image/')) return true
  return /\.(pdf|jpe?g|png)$/i.test(document.fileName ?? '')
}
