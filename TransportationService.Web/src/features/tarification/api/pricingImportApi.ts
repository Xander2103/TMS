import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { PricingAgreement } from './pricingApi'

// --- Preview ---

export interface PricingImportRowMessage {
  row: number
  message: string
}

/**
 * One classified rule change. Added rows carry a human `summary`; Updated/Removed rows carry
 * the matched `ruleId` and (for Updated) the `fieldChanges` list of "Label: old → new" strings.
 */
export interface PricingImportRuleChange {
  name: string
  summary: string | null
  ruleId: string | null
  fieldChanges: string[] | null
}

export interface PricingImportPreview {
  rowsFound: number
  rowsValid: number
  warnings: PricingImportRowMessage[]
  errors: PricingImportRowMessage[]
  added: PricingImportRuleChange[]
  updated: PricingImportRuleChange[]
  removed: PricingImportRuleChange[]
}

// --- Commit ---

export type PricingImportMode = 'UpdateAgreement' | 'DuplicateAsNewVersion'

export interface PricingImportCommitOptions {
  mode: PricingImportMode
  applyRemovals: boolean
  /** Required for DuplicateAsNewVersion. */
  newName?: string | null
  newEffectiveFrom?: string | null
}

export interface PricingImportCommitResult {
  /** The agreement the import actually landed on: the source for UpdateAgreement, the fresh copy for DuplicateAsNewVersion. */
  agreementId: string
  agreement: PricingAgreement
  added: number
  updated: number
  removed: number
}

/** Downloads the rate table as "tarieventabel-{naam}.xlsx" — export doubles as the import template. */
export async function downloadAgreementExport(agreementId: string, agreementName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/pricing/agreements/${agreementId}/export`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  // Geen hardgecodeerde NL-boodschap: een lege message laat de vertaalde fallback van de caller winnen.
  if (!response.ok) throw new Error()
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  const slug = agreementName.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'tabel'
  anchor.download = `tarieventabel-${slug}.xlsx`
  anchor.click()
  URL.revokeObjectURL(url)
}

async function postWorkbook<T>(path: string, file: File, fields?: Record<string, string>): Promise<T> {
  const form = new FormData()
  form.append('file', file)
  for (const [key, value] of Object.entries(fields ?? {})) {
    form.append(key, value)
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body: form,
  })
  const data = (await response.json().catch(() => null)) as T | { message?: string } | null
  if (!response.ok) {
    // Servermessage wint; zonder message laat een lege Error de vertaalde fallback van de caller winnen.
    throw new Error((data as { message?: string } | null)?.message ?? '')
  }

  return data as T
}

export function previewPricingImport(agreementId: string, file: File): Promise<PricingImportPreview> {
  return postWorkbook<PricingImportPreview>(`/api/pricing/agreements/${agreementId}/import/preview`, file)
}

export function commitPricingImport(
  agreementId: string,
  file: File,
  options: PricingImportCommitOptions,
): Promise<PricingImportCommitResult> {
  return postWorkbook<PricingImportCommitResult>(`/api/pricing/agreements/${agreementId}/import/commit`, file, {
    mode: options.mode,
    applyRemovals: String(options.applyRemovals),
    ...(options.newName ? { newName: options.newName } : {}),
    ...(options.newEffectiveFrom ? { newEffectiveFrom: options.newEffectiveFrom } : {}),
  })
}
