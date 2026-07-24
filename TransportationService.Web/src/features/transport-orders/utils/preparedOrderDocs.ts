import { ApiError } from '../../../api/apiClient'
import { createOrderDocument, uploadOrderDocumentFile, type OrderDocumentType } from '../api/orderDocumentsApi'

/**
 * Documents staged while CREATING a transport order (customer delivery note, CMR, ...),
 * held in client state until the order exists. Parent-first ordering: a failed order
 * create never leaves orphaned document records; failed uploads stay retryable and reuse
 * the already-created metadata record.
 */
export interface PreparedOrderDocument {
  key: string
  file: File
  documentType: OrderDocumentType
  title: string
  issueDate: string
  notes: string
  createdDocumentId?: string
}

export interface OrderFollowUpResult {
  key: string
  label: string
  ok: boolean
  error?: string
  createdDocumentId?: string
}

function describe(err: unknown, fallback: string): string {
  if (err instanceof ApiError) return err.message
  if (err instanceof Error && err.message) return err.message
  return fallback
}

export async function uploadPreparedOrderDocuments(
  orderId: string,
  docs: PreparedOrderDocument[],
): Promise<OrderFollowUpResult[]> {
  const results: OrderFollowUpResult[] = []
  for (const doc of docs) {
    let createdId = doc.createdDocumentId
    try {
      if (!createdId) {
        const created = await createOrderDocument(orderId, {
          documentType: doc.documentType,
          customTypeName: null,
          title: doc.title.trim() || doc.file.name,
          issueDate: doc.issueDate || null,
          notes: doc.notes.trim() || null,
        })
        createdId = created.id
      }

      await uploadOrderDocumentFile(createdId, doc.file)
      results.push({ key: doc.key, label: doc.file.name, ok: true, createdDocumentId: createdId })
    } catch (err) {
      results.push({
        key: doc.key,
        label: doc.file.name,
        ok: false,
        error: describe(err, 'Uploaden is mislukt.'),
        createdDocumentId: createdId,
      })
    }
  }
  return results
}
