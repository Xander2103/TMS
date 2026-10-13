import {
  uploadEmployeeDocument,
  type EmployeeDocumentCategory,
} from '../api/employeeDocumentsApi'
import { saveEmployeeIssuedItem } from '../../issued-items/issuedItemsApi'
import { ApiError } from '../../../api/apiClient'
import { translate } from '../../../i18n/translations'
import type { TranslateFn } from '../../../i18n/localeContext'

/** Fallback translator (Dutch) for callers that don't pass their own `t`. */
const defaultT: TranslateFn = (key, params) => translate('nl', key, params)

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
  /** Whether the template requires a variant choice (stock + variants enabled). */
  variantsEnabled: boolean
  /** Chosen variant for variant-enabled templates; sent with the issuance. */
  variantId: string | null
  /** Display label of the chosen variant (client-side only). */
  variantLabel: string
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
  t: TranslateFn = defaultT,
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
        error: describe(err, t('employees.errors.uploadFailed')),
      })
    }
  }
  return results
}

/**
 * Creates each prepared issued-item record; never throws — returns a per-item result.
 * Runs only AFTER the employee row exists, so a failed employee create never consumes
 * stock; each issuance consumes stock in its own backend transaction.
 */
export async function createPreparedIssuedItems(
  employeeId: string,
  items: PreparedIssuedItem[],
  t: TranslateFn = defaultT,
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
        variantId: item.variantId,
      })
      results.push({ key: item.key, kind: 'issued-item', label: item.name || t('employees.create.issuedItemFallback'), ok: true })
    } catch (err) {
      results.push({
        key: item.key,
        kind: 'issued-item',
        label: item.name || t('employees.create.issuedItemFallback'),
        ok: false,
        error: describe(err, t('employees.errors.createFailed')),
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
  t: TranslateFn = defaultT,
): Promise<FollowUpResult[]> {
  const docResults = await uploadPreparedDocuments(employeeId, docs, t)
  const itemResults = await createPreparedIssuedItems(employeeId, items, t)
  return [...docResults, ...itemResults]
}
