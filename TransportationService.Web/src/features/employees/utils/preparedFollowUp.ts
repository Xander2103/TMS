import {
  uploadEmployeeDocument,
  type EmployeeDocumentCategory,
} from '../api/employeeDocumentsApi'
import { saveEmployeeIssuedItem } from '../../issued-items/issuedItemsApi'
import { ApiError } from '../../../api/apiClient'

/**
 * Documents and company assets a user prepared while CREATING an employee, held in
 * client-side form state until the employee exists. Nothing is persisted for a
 * not-yet-created employee; the follow-ups run only after employee creation succeeds,
 * using the returned EmployeeId. On partial failure the employee and the prepared files
 * are never lost — the failed items stay retryable.
 */

export interface PreparedEmployeeDocument {
  key: string
  file: File
  category: EmployeeDocumentCategory
  customLabel: string
  expiryDate: string
  notes: string
}

export interface PreparedIssuedItem {
  key: string
  templateId: string | null
  name: string
  category: string
  quantity: number
  serialNumber: string
  issuedDate: string
  notes: string
  /** Whether returning the item is expected (copied from the template; informational). */
  returnRequired: boolean
  /** Whether a serial number is required (copied from the template; drives validation). */
  requiresSerialNumber: boolean
}

export interface FollowUpResult {
  key: string
  kind: 'document' | 'issued-item'
  label: string
  ok: boolean
  error?: string
}

function describe(err: unknown, fallback: string): string {
  if (err instanceof ApiError) return err.message
  if (err instanceof Error && err.message) return err.message
  return fallback
}

/** Uploads each prepared document; never throws — returns a per-item result. */
export async function uploadPreparedDocuments(
  employeeId: string,
  docs: PreparedEmployeeDocument[],
): Promise<FollowUpResult[]> {
  const results: FollowUpResult[] = []
  for (const doc of docs) {
    try {
      await uploadEmployeeDocument(employeeId, {
        file: doc.file,
        category: doc.category,
        customLabel: doc.customLabel.trim() || null,
        expiryDate: doc.expiryDate || null,
        notes: doc.notes.trim() || null,
      })
      results.push({ key: doc.key, kind: 'document', label: doc.file.name, ok: true })
    } catch (err) {
      results.push({
        key: doc.key,
        kind: 'document',
        label: doc.file.name,
        ok: false,
        error: describe(err, 'Uploaden is mislukt.'),
      })
    }
  }
  return results
}

/**
 * Creates each prepared issued-item record; never throws — returns a per-item result.
 * Stock movements are intentionally NOT emitted here: the stock subsystem is a later wave;
 * when it lands, the issuance already exists to attach a movement to.
 */
export async function createPreparedIssuedItems(
  employeeId: string,
  items: PreparedIssuedItem[],
): Promise<FollowUpResult[]> {
  const results: FollowUpResult[] = []
  for (const item of items) {
    try {
      await saveEmployeeIssuedItem(employeeId, null, {
        templateId: item.templateId,
        name: item.name.trim() || null,
        category: item.category.trim() || null,
        status: 'Issued',
        issuedDate: item.issuedDate || null,
        quantity: item.quantity,
        serialNumber: item.serialNumber.trim() || null,
        notes: item.notes.trim() || null,
        returnedDate: null,
        returnCondition: null,
      })
      results.push({ key: item.key, kind: 'issued-item', label: item.name || 'Bedrijfsmiddel', ok: true })
    } catch (err) {
      results.push({
        key: item.key,
        kind: 'issued-item',
        label: item.name || 'Bedrijfsmiddel',
        ok: false,
        error: describe(err, 'Aanmaken is mislukt.'),
      })
    }
  }
  return results
}

/**
 * Runs all follow-ups for a freshly-created employee. Returns every result; the caller
 * decides success vs partial-failure and offers retry for the `!ok` entries.
 */
export async function runEmployeeCreateFollowUps(
  employeeId: string,
  docs: PreparedEmployeeDocument[],
  items: PreparedIssuedItem[],
): Promise<FollowUpResult[]> {
  const docResults = await uploadPreparedDocuments(employeeId, docs)
  const itemResults = await createPreparedIssuedItems(employeeId, items)
  return [...docResults, ...itemResults]
}
