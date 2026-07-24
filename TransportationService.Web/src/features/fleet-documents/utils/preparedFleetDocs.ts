import { ApiError } from '../../../api/apiClient'
import { createFleetDocument, uploadFleetDocumentFile, type FleetDocumentOwnerType } from '../api/fleetDocumentsApi'
import type { FleetDocumentType } from '../types'

/**
 * Documents a user prepared while CREATING a vehicle/trailer, held in client-side form
 * state until the asset exists. Nothing is persisted for a not-yet-created asset; the
 * uploads run only after creation succeeds (parent-first ordering = no orphans). On
 * partial failure the asset and the prepared files are never lost — failed items stay
 * retryable, and an already-created metadata record is remembered so a retry only
 * re-uploads the file instead of duplicating the record.
 */
export interface PreparedFleetDocument {
  key: string
  file: File
  documentType: FleetDocumentType
  /** Custom title; required when documentType is Other. */
  customTypeName: string
  documentNumber: string
  issueDate: string
  expiryDate: string
  notes: string
  /** Set when the metadata record was created but the file upload failed (retry state). */
  createdDocumentId?: string
}

export interface FleetFollowUpResult {
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

/** Creates metadata + uploads the file per prepared document; never throws. */
export async function uploadPreparedFleetDocuments(
  ownerType: FleetDocumentOwnerType,
  ownerId: string,
  docs: PreparedFleetDocument[],
): Promise<FleetFollowUpResult[]> {
  const results: FleetFollowUpResult[] = []
  for (const doc of docs) {
    let createdId = doc.createdDocumentId
    try {
      if (!createdId) {
        const created = await createFleetDocument(ownerType, ownerId, {
          documentType: doc.documentType,
          customTypeName: doc.customTypeName.trim() || null,
          documentNumber: doc.documentNumber.trim() || null,
          issueDate: doc.issueDate || null,
          expiryDate: doc.expiryDate || null,
          warningDays: null,
          issuingAuthority: null,
          notes: doc.notes.trim() || null,
        })
        createdId = created.id
      }

      await uploadFleetDocumentFile(createdId, doc.file)
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
