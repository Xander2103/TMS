import { apiBaseUrl } from '../../../config/env'
import { apiClient } from '../../../api/apiClient'
import { getAccessToken } from '../../auth/authStorage'

/** GET /api/system-info — versie-, deploy- en gezondheidsinformatie van de applicatie. */
export interface SystemInfo {
  version: string
  buildCommit: string | null
  environment: string
  lastDeployedAtUtc: string | null
  apiStatus: string
  databaseStatus: string
  databaseLatencyMs: number | null
  schemaVersion: string | null
  pendingMigrations: number
  deploymentRef: string | null
}

export type BackupSource = 'Manual' | 'Automatic' | 'PreRestore' | 'PreDeployment'
export type BackupStatus = 'Completed' | 'Failed' | 'Restoring' | 'Restored'

export interface BackupDto {
  id: string
  fileName: string
  createdAtUtc: string
  sizeBytes: number
  source: BackupSource
  status: BackupStatus
  schemaVersion: string | null
  note: string | null
  restoredAtUtc: string | null
  /** Beveiligde back-ups (bv. veiligheidsback-ups) kunnen niet worden verwijderd. */
  protected: boolean
  /** Deploy-script-dumps: alleen zichtbaar, geen enkele actie mogelijk. */
  readOnly: boolean
}

export interface BackupListResult {
  backups: BackupDto[]
  automaticRetentionDays: number
  preRestoreRetentionDays: number
  automaticEnabled: boolean
  /** LEGACY Nederlandse weergavetekst van de server; UI-logica hoort op storageAvailable. */
  storageStatus: string
  /** Stabiele storage-healthcheck (i18n-wave): het label komt uit de vertaalbundel. */
  storageAvailable: boolean
}

/** Resultaat van POST .../restore: de vooraf gemaakte veiligheidsback-up + advies. */
export interface RestoreResult {
  safetyBackupId: string
  safetyBackupFileName: string
  databaseStatus: string
  advice: string
}

export function getSystemInfo(): Promise<SystemInfo> {
  return apiClient.getJson<SystemInfo>('/api/system-info')
}

export function listBackups(): Promise<BackupListResult> {
  return apiClient.getJson<BackupListResult>('/api/system-info/backups')
}

export function createBackup(note: string | null): Promise<BackupDto> {
  return apiClient.postJson<BackupDto, { note: string | null }>('/api/system-info/backups', { note })
}

export function deleteBackup(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/system-info/backups/${id}`)
}

/** De bevestiging MOET exact de bestandsnaam zijn die de beheerder heeft ingetypt. */
export function restoreBackup(id: string, confirmation: string): Promise<RestoreResult> {
  return apiClient.postJson<RestoreResult, { confirmation: string }>(
    `/api/system-info/backups/${id}/restore`,
    { confirmation },
  )
}

/** Downloadt het back-upbestand via bearer + object-URL (zelfde idioom als transportDocumentsApi). */
export async function downloadBackup(id: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/system-info/backups/${id}/download`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new Error('De back-up kon niet worden gedownload.')
  const blob = await response.blob()
  const disposition = response.headers.get('content-disposition')
  const downloadName = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? fileName
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = downloadName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}
