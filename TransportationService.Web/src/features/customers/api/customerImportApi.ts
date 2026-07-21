import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { CustomerImportCommit, CustomerImportPreview } from '../types'

/** Downloads the Excel import template as "klanten-import.xlsx". */
export async function downloadCustomerImportTemplate(): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/customers/import/template`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new Error('Het sjabloon kon niet worden gedownload.')
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'klanten-import.xlsx'
  anchor.click()
  URL.revokeObjectURL(url)
}

async function postWorkbook<T>(path: string, file: File): Promise<T> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body: form,
  })
  const data = (await response.json().catch(() => null)) as T | { message?: string } | null
  if (!response.ok) {
    throw new Error((data as { message?: string } | null)?.message ?? 'Import mislukt.')
  }

  return data as T
}

export function previewCustomerImport(file: File): Promise<CustomerImportPreview> {
  return postWorkbook<CustomerImportPreview>('/api/customers/import/preview', file)
}

export function commitCustomerImport(
  file: File,
  options: { allOrNothing: boolean; allowUpdates: boolean },
): Promise<CustomerImportCommit> {
  return postWorkbook<CustomerImportCommit>(
    `/api/customers/import/commit?allOrNothing=${options.allOrNothing}&allowUpdates=${options.allowUpdates}`,
    file,
  )
}

/** Saves the base64 error workbook returned by a commit as "klanten-import-fouten.xlsx". */
export function downloadCustomerImportErrorWorkbook(base64: string): void {
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
  const blob = new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'klanten-import-fouten.xlsx'
  anchor.click()
  URL.revokeObjectURL(url)
}
