import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { TabPanel, Tabs, type TabItem } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { formatDateTime } from '../../../utils/dates'
import {
  createBackup,
  deleteBackup,
  downloadBackup,
  getSystemInfo,
  listBackups,
  restoreBackup,
  type BackupDto,
  type BackupListResult,
  type BackupSource,
  type BackupStatus,
  type RestoreResult,
  type SystemInfo,
} from '../api/systemInfoApi'
import './settings.css'

const TAB_IDS = ['systeem', 'backups'] as const
type TabId = (typeof TAB_IDS)[number]

const SOURCE_LABELS: Record<BackupSource, string> = {
  Manual: 'Manueel',
  Automatic: 'Automatisch',
  PreRestore: 'Vóór terugzetten',
  PreDeployment: 'Pre-deployment',
}

const STATUS_LABELS: Record<BackupStatus, string> = {
  Completed: 'Voltooid',
  Failed: 'Mislukt',
  Restoring: 'Bezig met terugzetten',
  Restored: 'Teruggezet',
}

const STATUS_TONES: Record<BackupStatus, BadgeTone> = {
  Completed: 'success',
  Failed: 'danger',
  Restoring: 'warning',
  Restored: 'info',
}

/** Leesbare bestandsgrootte (KB/MB/GB) — back-ups zijn nooit zinvol in losse bytes. */
function formatBackupSize(sizeBytes: number): string {
  const gb = 1024 * 1024 * 1024
  const mb = 1024 * 1024
  if (sizeBytes >= gb) return `${(sizeBytes / gb).toLocaleString('nl-BE', { maximumFractionDigits: 1 })} GB`
  if (sizeBytes >= mb) return `${(sizeBytes / mb).toLocaleString('nl-BE', { maximumFractionDigits: 1 })} MB`
  return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`
}

function healthTone(status: string): BadgeTone {
  return status === 'Healthy' ? 'success' : 'danger'
}

function healthLabel(status: string): string {
  return status === 'Healthy' ? 'In orde' : status
}

/** Eén rij van de definitielijst; waarden zijn nooit alleen kleur (badges dragen tekst). */
function InfoRow({ term, children }: { term: string; children: ReactNode }) {
  return (
    <>
      <dt>{term}</dt>
      <dd>{children}</dd>
    </>
  )
}

function SystemInfoTab() {
  const [info, setInfo] = useState<SystemInfo | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getSystemInfo()
      .then((data) => {
        if (mounted) setInfo(data)
      })
      .catch(() => {
        if (mounted) setLoadError('De systeeminformatie kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!info) return <p className="placeholder-text">Systeeminformatie laden…</p>

  const frontendBuild = import.meta.env.VITE_BUILD_COMMIT

  return (
    <div className="settings-sections">
      <section className="settings-card">
        <h2>Applicatie</h2>
        <dl className="sysinfo-list">
          <InfoRow term="Omgeving">{info.environment}</InfoRow>
          <InfoRow term="Versie">{info.version}</InfoRow>
          <InfoRow term="Build (API)">
            {info.buildCommit ? (
              <span className="sysinfo-mono sysinfo-truncate" title={info.buildCommit}>
                {info.buildCommit}
              </span>
            ) : (
              '—'
            )}
          </InfoRow>
          <InfoRow term="Build (frontend)">
            {frontendBuild ? (
              <span className="sysinfo-mono sysinfo-truncate" title={frontendBuild}>
                {frontendBuild}
              </span>
            ) : (
              'lokale build'
            )}
          </InfoRow>
          <InfoRow term="Laatst bijgewerkt">{formatDateTime(info.lastDeployedAtUtc) || '—'}</InfoRow>
          <InfoRow term="Deployment-ref">
            {info.deploymentRef ? (
              <span className="sysinfo-mono sysinfo-truncate" title={info.deploymentRef}>
                {info.deploymentRef}
              </span>
            ) : (
              '—'
            )}
          </InfoRow>
        </dl>
      </section>

      <section className="settings-card">
        <h2>Status</h2>
        <dl className="sysinfo-list">
          <InfoRow term="API">
            <Badge tone={healthTone(info.apiStatus)}>{healthLabel(info.apiStatus)}</Badge>
          </InfoRow>
          <InfoRow term="Database">
            <Badge tone={healthTone(info.databaseStatus)}>{healthLabel(info.databaseStatus)}</Badge>
            {info.databaseLatencyMs !== null && (
              <span className="sysinfo-latency">{info.databaseLatencyMs} ms</span>
            )}
          </InfoRow>
          <InfoRow term="Databaseschema">
            {info.schemaVersion ? (
              <span className="sysinfo-mono sysinfo-truncate" title={info.schemaVersion}>
                {info.schemaVersion}
              </span>
            ) : (
              '—'
            )}
          </InfoRow>
          <InfoRow term="Openstaande migraties">
            {info.pendingMigrations > 0 ? (
              <Badge tone="warning">{info.pendingMigrations} openstaand</Badge>
            ) : (
              <Badge tone="neutral">Geen</Badge>
            )}
          </InfoRow>
        </dl>
      </section>
    </div>
  )
}

/** Modal voor "Nieuwe back-up maken": optionele notitie, daarna POST. */
function CreateBackupModal({
  busy,
  onConfirm,
  onCancel,
}: {
  busy: boolean
  onConfirm: (note: string | null) => void
  onCancel: () => void
}) {
  const [note, setNote] = useState('')
  return (
    <Modal
      title="Nieuwe back-up maken"
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Annuleren
          </Button>
          <Button onClick={() => onConfirm(note.trim() === '' ? null : note.trim())} disabled={busy}>
            {busy ? 'Back-up maken…' : 'Back-up maken'}
          </Button>
        </>
      }
    >
      <p>Er wordt onmiddellijk een volledige back-up van de databank gemaakt.</p>
      <FormField label="Notitie (optioneel)" htmlFor="backup-note" hint="Bijvoorbeeld de reden van deze back-up.">
        <input
          id="backup-note"
          type="text"
          maxLength={500}
          value={note}
          disabled={busy}
          autoFocus
          onChange={(e) => setNote(e.target.value)}
        />
      </FormField>
    </Modal>
  )
}

/**
 * Terugzet-modal met getypte bevestiging: de knop ontgrendelt pas wanneer de beheerder de
 * exacte bestandsnaam heeft ingetypt. Na afloop toont dezelfde modal het resultaat
 * (veiligheidsback-up + advies).
 */
function RestoreBackupModal({
  backup,
  busy,
  result,
  onConfirm,
  onClose,
}: {
  backup: BackupDto
  busy: boolean
  result: RestoreResult | null
  onConfirm: (confirmation: string) => void
  onClose: () => void
}) {
  const [confirmation, setConfirmation] = useState('')
  const matches = confirmation === backup.fileName

  if (result) {
    return (
      <Modal
        title="Back-up teruggezet"
        onClose={onClose}
        footer={<Button onClick={onClose}>Sluiten</Button>}
      >
        <p>
          De databank is teruggezet vanaf <span className="sysinfo-mono">{backup.fileName}</span>.
        </p>
        <dl className="sysinfo-list">
          <InfoRow term="Veiligheidsback-up">
            <span className="sysinfo-mono sysinfo-truncate" title={result.safetyBackupFileName}>
              {result.safetyBackupFileName}
            </span>
          </InfoRow>
          <InfoRow term="Databasestatus">
            <Badge tone={healthTone(result.databaseStatus)}>{healthLabel(result.databaseStatus)}</Badge>
          </InfoRow>
        </dl>
        <div className="sysbackups-restore-success" role="status">
          {result.advice}
        </div>
      </Modal>
    )
  }

  return (
    <Modal
      title="Back-up terugzetten"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button variant="danger" onClick={() => onConfirm(confirmation)} disabled={!matches || busy}>
            {busy ? 'Terugzetten…' : 'Terugzetten'}
          </Button>
        </>
      }
    >
      <dl className="sysinfo-list">
        <InfoRow term="Bestandsnaam">
          <span className="sysinfo-mono sysinfo-truncate" title={backup.fileName}>
            {backup.fileName}
          </span>
        </InfoRow>
        <InfoRow term="Datum/tijd">{formatDateTime(backup.createdAtUtc)}</InfoRow>
        <InfoRow term="Grootte">{formatBackupSize(backup.sizeBytes)}</InfoRow>
        <InfoRow term="Schema">
          {backup.schemaVersion ? (
            <span className="sysinfo-mono sysinfo-truncate" title={backup.schemaVersion}>
              {backup.schemaVersion}
            </span>
          ) : (
            '—'
          )}
        </InfoRow>
      </dl>
      <div className="sysbackups-restore-warning" role="alert">
        Deze actie VERVANGT de huidige databank met de inhoud van deze back-up. Er wordt eerst automatisch een
        veiligheidsback-up gemaakt.
      </div>
      <FormField label="Typ de exacte bestandsnaam om te bevestigen" htmlFor="restore-confirmation">
        <input
          id="restore-confirmation"
          type="text"
          className="sysbackups-restore-input"
          value={confirmation}
          disabled={busy}
          autoFocus
          autoComplete="off"
          spellCheck={false}
          onChange={(e) => setConfirmation(e.target.value)}
        />
      </FormField>
    </Modal>
  )
}

function BackupsTab() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canCreate = hasPermission('backups.create')
  const canDownload = hasPermission('backups.download')
  const canDelete = hasPermission('backups.delete')
  const canRestore = hasPermission('backups.restore')

  const [data, setData] = useState<BackupListResult | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [creating, setCreating] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<BackupDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [restoreTarget, setRestoreTarget] = useState<BackupDto | null>(null)
  const [restoring, setRestoring] = useState(false)
  const [restoreResult, setRestoreResult] = useState<RestoreResult | null>(null)

  async function load() {
    try {
      setData(await listBackups())
      setLoadError(null)
    } catch {
      setLoadError('De back-ups konden niet worden geladen.')
    }
  }

  useEffect(() => {
    let mounted = true
    listBackups()
      .then((result) => {
        if (!mounted) return
        setData(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De back-ups konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  async function handleCreate(note: string | null) {
    setCreating(true)
    try {
      await createBackup(note)
      showSuccess('De back-up is gemaakt.')
      setCreateOpen(false)
      await load()
    } catch (err) {
      showError(describeApiError(err, 'De back-up kon niet worden gemaakt.').message)
    } finally {
      setCreating(false)
    }
  }

  async function handleDownload(backup: BackupDto) {
    try {
      await downloadBackup(backup.id, backup.fileName)
    } catch {
      showError('De back-up kon niet worden gedownload.')
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      await deleteBackup(deleteTarget.id)
      showSuccess('De back-up is verwijderd.')
      setDeleteTarget(null)
      await load()
    } catch (err) {
      showError(describeApiError(err, 'De back-up kon niet worden verwijderd.').message)
    } finally {
      setDeleting(false)
    }
  }

  async function handleRestore(confirmation: string) {
    if (!restoreTarget) return
    setRestoring(true)
    try {
      const result = await restoreBackup(restoreTarget.id, confirmation)
      setRestoreResult(result)
      await load()
    } catch (err) {
      showError(describeApiError(err, 'De back-up kon niet worden teruggezet.').message)
    } finally {
      setRestoring(false)
    }
  }

  const readOnlyTitle = 'Deze back-up is aangemaakt door het deploy-script en kan hier niet worden beheerd.'

  const columns: Column<BackupDto>[] = [
    { key: 'createdAt', header: 'Datum/tijd', render: (b) => formatDateTime(b.createdAtUtc) },
    { key: 'source', header: 'Type', render: (b) => SOURCE_LABELS[b.source] ?? b.source },
    { key: 'size', header: 'Grootte', align: 'right', render: (b) => formatBackupSize(b.sizeBytes) },
    {
      key: 'schema',
      header: 'Schema',
      render: (b) =>
        b.schemaVersion ? (
          <span className="sysinfo-mono sysbackups-schema" title={b.schemaVersion}>
            {b.schemaVersion}
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (b) => (
        <Badge
          tone={STATUS_TONES[b.status] ?? 'neutral'}
          title={b.restoredAtUtc ? `Teruggezet op ${formatDateTime(b.restoredAtUtc)}` : undefined}
        >
          {STATUS_LABELS[b.status] ?? b.status}
        </Badge>
      ),
    },
    { key: 'note', header: 'Reden/notitie', render: (b) => b.note ?? '' },
  ]

  if (canDownload || canDelete || canRestore) {
    columns.push({
      key: 'actions',
      header: 'Acties',
      align: 'right',
      render: (b) => (
        <span className="sysbackups-row-actions">
          {canDownload && (
            <button
              type="button"
              className="sysbackups-link"
              disabled={b.readOnly}
              title={b.readOnly ? readOnlyTitle : undefined}
              onClick={() => void handleDownload(b)}
            >
              Downloaden
            </button>
          )}
          {canRestore && (
            <button
              type="button"
              className="sysbackups-link"
              disabled={b.readOnly}
              title={b.readOnly ? readOnlyTitle : undefined}
              onClick={() => {
                setRestoreResult(null)
                setRestoreTarget(b)
              }}
            >
              Terugzetten
            </button>
          )}
          {canDelete && (
            <button
              type="button"
              className="sysbackups-link sysbackups-link-danger"
              disabled={b.readOnly || b.protected}
              title={
                b.readOnly
                  ? readOnlyTitle
                  : b.protected
                    ? 'Deze back-up is beveiligd en kan niet worden verwijderd.'
                    : undefined
              }
              onClick={() => setDeleteTarget(b)}
            >
              Verwijderen
            </button>
          )}
        </span>
      ),
    })
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!data) return <p className="placeholder-text">Back-ups laden…</p>

  const storageOk = data.storageStatus === 'Beschikbaar'

  return (
    <div>
      <div className="sysbackups-banner">
        Automatische back-ups: {data.automaticRetentionDays} dagen · Veiligheidsback-ups (pre-restore):{' '}
        {data.preRestoreRetentionDays} dagen · Handmatige back-ups worden nooit automatisch verwijderd.
      </div>
      {!data.automaticEnabled && (
        <p className="sysbackups-note">Automatische back-ups zijn momenteel uitgeschakeld.</p>
      )}
      {!storageOk && (
        <div className="sysbackups-storage-warning" role="alert">
          Opslagstatus: {data.storageStatus}. Controleer de back-upopslag op de server.
        </div>
      )}

      {canCreate && (
        <div className="sysbackups-toolbar">
          <Button onClick={() => setCreateOpen(true)} disabled={creating}>
            {creating ? 'Back-up maken…' : 'Nieuwe back-up maken'}
          </Button>
        </div>
      )}

      <DataTable
        columns={columns}
        rows={data.backups}
        rowKey={(b) => `${b.id}-${b.fileName}`}
        emptyMessage="Er zijn nog geen back-ups."
      />

      {createOpen && (
        <CreateBackupModal
          busy={creating}
          onConfirm={(note) => void handleCreate(note)}
          onCancel={() => setCreateOpen(false)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Back-up verwijderen"
          message={`Weet je zeker dat je de back-up "${deleteTarget.fileName}" definitief wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={deleting}
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {restoreTarget && (
        <RestoreBackupModal
          backup={restoreTarget}
          busy={restoring}
          result={restoreResult}
          onConfirm={(confirmation) => void handleRestore(confirmation)}
          onClose={() => {
            setRestoreTarget(null)
            setRestoreResult(null)
          }}
        />
      )}
    </div>
  )
}

/**
 * Parameters → Beheer → Systeeminformatie: versie/deploy/gezondheid van de applicatie plus
 * het back-upbeheer (maken, downloaden, verwijderen, terugzetten met getypte bevestiging).
 */
export function SystemInfoPage() {
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const canViewSystem = hasPermission('system_info.view')
  const canViewBackups = hasPermission('backups.view')

  const tabs: TabItem[] = useMemo(() => {
    const list: TabItem[] = []
    if (canViewSystem) list.push({ id: 'systeem', label: 'Systeeminformatie' })
    if (canViewBackups) list.push({ id: 'backups', label: 'Back-ups' })
    return list
  }, [canViewSystem, canViewBackups])

  const requestedTab = searchParams.get('tab')
  const fallbackTab = (tabs[0]?.id ?? 'systeem') as TabId
  const tab: TabId =
    TAB_IDS.includes(requestedTab as TabId) && tabs.some((t) => t.id === requestedTab)
      ? (requestedTab as TabId)
      : fallbackTab

  function setTab(next: string) {
    setSearchParams(next === fallbackTab ? {} : { tab: next }, { replace: true })
  }

  if (tabs.length === 0) {
    return <p className="placeholder-text">Je hebt geen rechten om de systeeminformatie te bekijken.</p>
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Systeeminformatie' }]} />
      <PageHeader
        title="Systeeminformatie"
        subtitle="Versie- en omgevingsgegevens van de applicatie en het beheer van databankback-ups."
      />

      <Tabs tabs={tabs} activeId={tab} onChange={setTab} />

      {tab === 'systeem' && canViewSystem && (
        <TabPanel tabId="systeem">
          <SystemInfoTab />
        </TabPanel>
      )}
      {tab === 'backups' && canViewBackups && (
        <TabPanel tabId="backups">
          <BackupsTab />
        </TabPanel>
      )}
    </div>
  )
}
