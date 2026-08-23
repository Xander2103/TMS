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
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
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

/** Vertaalsleutels per bron/status (labelmap → keymap); onbekende waarden tonen de ruwe code. */
const SOURCE_KEYS: Record<BackupSource, string> = {
  Manual: 'settingsPages.backups.source.Manual',
  Automatic: 'settingsPages.backups.source.Automatic',
  PreRestore: 'settingsPages.backups.source.PreRestore',
  PreDeployment: 'settingsPages.backups.source.PreDeployment',
}

const STATUS_KEYS: Record<BackupStatus, string> = {
  Completed: 'settingsPages.backups.status.Completed',
  Failed: 'settingsPages.backups.status.Failed',
  Restoring: 'settingsPages.backups.status.Restoring',
  Restored: 'settingsPages.backups.status.Restored',
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

/** 'Healthy' krijgt een vertaald label; elke andere waarde blijft ruwe technische status (§92). */
function healthLabel(t: TranslateFn, status: string): string {
  return status === 'Healthy' ? t('settingsPages.system.healthy') : status
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
  const { t } = useLocale()
  const [info, setInfo] = useState<SystemInfo | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getSystemInfo()
      .then((data) => {
        if (mounted) setInfo(data)
      })
      .catch(() => {
        if (mounted) setLoadError(t('settingsPages.system.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!info) return <p className="placeholder-text">{t('settingsPages.system.loading')}</p>

  const frontendBuild = import.meta.env.VITE_BUILD_COMMIT

  return (
    <div className="settings-sections">
      <section className="settings-card">
        <h2>{t('settingsPages.system.application')}</h2>
        <dl className="sysinfo-list">
          <InfoRow term={t('settingsPages.system.environment')}>{info.environment}</InfoRow>
          <InfoRow term={t('settingsPages.system.version')}>{info.version}</InfoRow>
          <InfoRow term={t('settingsPages.system.buildApi')}>
            {info.buildCommit ? (
              <span className="sysinfo-mono sysinfo-truncate" title={info.buildCommit}>
                {info.buildCommit}
              </span>
            ) : (
              '—'
            )}
          </InfoRow>
          <InfoRow term={t('settingsPages.system.buildFrontend')}>
            {frontendBuild ? (
              <span className="sysinfo-mono sysinfo-truncate" title={frontendBuild}>
                {frontendBuild}
              </span>
            ) : (
              t('settingsPages.system.localBuild')
            )}
          </InfoRow>
          <InfoRow term={t('settingsPages.system.lastUpdated')}>{formatDateTime(info.lastDeployedAtUtc) || '—'}</InfoRow>
          <InfoRow term={t('settingsPages.system.deploymentRef')}>
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
        <h2>{t('settingsPages.system.statusTitle')}</h2>
        <dl className="sysinfo-list">
          <InfoRow term={t('settingsPages.system.api')}>
            <Badge tone={healthTone(info.apiStatus)}>{healthLabel(t, info.apiStatus)}</Badge>
          </InfoRow>
          <InfoRow term={t('settingsPages.system.database')}>
            <Badge tone={healthTone(info.databaseStatus)}>{healthLabel(t, info.databaseStatus)}</Badge>
            {info.databaseLatencyMs !== null && (
              <span className="sysinfo-latency">{info.databaseLatencyMs} ms</span>
            )}
          </InfoRow>
          <InfoRow term={t('settingsPages.system.databaseSchema')}>
            {info.schemaVersion ? (
              <span className="sysinfo-mono sysinfo-truncate" title={info.schemaVersion}>
                {info.schemaVersion}
              </span>
            ) : (
              '—'
            )}
          </InfoRow>
          <InfoRow term={t('settingsPages.system.pendingMigrations')}>
            {info.pendingMigrations > 0 ? (
              <Badge tone="warning">{t('settingsPages.system.pendingCount', { count: info.pendingMigrations })}</Badge>
            ) : (
              <Badge tone="neutral">{t('settingsPages.system.noPending')}</Badge>
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
  const { t } = useLocale()
  const [note, setNote] = useState('')
  return (
    <Modal
      title={t('settingsPages.backups.createTitle')}
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => onConfirm(note.trim() === '' ? null : note.trim())} disabled={busy}>
            {busy ? t('settingsPages.backups.createBusy') : t('settingsPages.backups.createConfirm')}
          </Button>
        </>
      }
    >
      <p>{t('settingsPages.backups.createIntro')}</p>
      <FormField label={t('settingsPages.backups.noteLabel')} htmlFor="backup-note" hint={t('settingsPages.backups.noteHint')}>
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
  const { t } = useLocale()
  const [confirmation, setConfirmation] = useState('')
  const matches = confirmation === backup.fileName

  if (result) {
    return (
      <Modal
        title={t('settingsPages.backups.restoredTitle')}
        onClose={onClose}
        footer={<Button onClick={onClose}>{t('ui.actions.close')}</Button>}
      >
        <p>{t('settingsPages.backups.restoredFrom', { fileName: backup.fileName })}</p>
        <dl className="sysinfo-list">
          <InfoRow term={t('settingsPages.backups.safetyBackup')}>
            <span className="sysinfo-mono sysinfo-truncate" title={result.safetyBackupFileName}>
              {result.safetyBackupFileName}
            </span>
          </InfoRow>
          <InfoRow term={t('settingsPages.backups.databaseStatus')}>
            <Badge tone={healthTone(result.databaseStatus)}>{healthLabel(t, result.databaseStatus)}</Badge>
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
      title={t('settingsPages.backups.restoreTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button variant="danger" onClick={() => onConfirm(confirmation)} disabled={!matches || busy}>
            {busy ? t('settingsPages.backups.restoreBusy') : t('settingsPages.backups.restoreAction')}
          </Button>
        </>
      }
    >
      <dl className="sysinfo-list">
        <InfoRow term={t('settingsPages.backups.fileName')}>
          <span className="sysinfo-mono sysinfo-truncate" title={backup.fileName}>
            {backup.fileName}
          </span>
        </InfoRow>
        <InfoRow term={t('settingsPages.backups.columnDateTime')}>{formatDateTime(backup.createdAtUtc)}</InfoRow>
        <InfoRow term={t('settingsPages.backups.columnSize')}>{formatBackupSize(backup.sizeBytes)}</InfoRow>
        <InfoRow term={t('settingsPages.backups.columnSchema')}>
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
        {t('settingsPages.backups.restoreWarning')}
      </div>
      <FormField label={t('settingsPages.backups.confirmationLabel')} htmlFor="restore-confirmation">
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
  const { t } = useLocale()
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
      setLoadError(t('settingsPages.backups.loadFailed'))
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
        if (mounted) setLoadError(t('settingsPages.backups.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function handleCreate(note: string | null) {
    setCreating(true)
    try {
      await createBackup(note)
      showSuccess(t('settingsPages.backups.created'))
      setCreateOpen(false)
      await load()
    } catch (err) {
      showError(describeApiError(err, t('settingsPages.backups.createFailed')).message)
    } finally {
      setCreating(false)
    }
  }

  async function handleDownload(backup: BackupDto) {
    try {
      await downloadBackup(backup.id, backup.fileName)
    } catch {
      showError(t('settingsPages.backups.downloadFailed'))
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      await deleteBackup(deleteTarget.id)
      showSuccess(t('settingsPages.backups.deleted'))
      setDeleteTarget(null)
      await load()
    } catch (err) {
      showError(describeApiError(err, t('settingsPages.backups.deleteFailed')).message)
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
      showError(describeApiError(err, t('settingsPages.backups.restoreFailed')).message)
    } finally {
      setRestoring(false)
    }
  }

  const readOnlyTitle = t('settingsPages.backups.readOnlyTitle')

  const columns: Column<BackupDto>[] = [
    { key: 'createdAt', header: t('settingsPages.backups.columnDateTime'), render: (b) => formatDateTime(b.createdAtUtc) },
    {
      key: 'source',
      header: t('settingsPages.backups.columnType'),
      render: (b) => (SOURCE_KEYS[b.source] ? t(SOURCE_KEYS[b.source]) : b.source),
    },
    { key: 'size', header: t('settingsPages.backups.columnSize'), align: 'right', render: (b) => formatBackupSize(b.sizeBytes) },
    {
      key: 'schema',
      header: t('settingsPages.backups.columnSchema'),
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
      header: t('settingsPages.backups.columnStatus'),
      render: (b) => (
        <Badge
          tone={STATUS_TONES[b.status] ?? 'neutral'}
          title={b.restoredAtUtc ? t('settingsPages.backups.restoredAtTitle', { dateTime: formatDateTime(b.restoredAtUtc) }) : undefined}
        >
          {STATUS_KEYS[b.status] ? t(STATUS_KEYS[b.status]) : b.status}
        </Badge>
      ),
    },
    { key: 'note', header: t('settingsPages.backups.columnNote'), render: (b) => b.note ?? '' },
  ]

  if (canDownload || canDelete || canRestore) {
    columns.push({
      key: 'actions',
      header: t('settingsPages.common.actions'),
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
              {t('settingsPages.backups.download')}
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
              {t('settingsPages.backups.restoreAction')}
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
                    ? t('settingsPages.backups.protectedTitle')
                    : undefined
              }
              onClick={() => setDeleteTarget(b)}
            >
              {t('ui.actions.delete')}
            </button>
          )}
        </span>
      ),
    })
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!data) return <p className="placeholder-text">{t('settingsPages.backups.loading')}</p>

  return (
    <div>
      <div className="sysbackups-banner">
        {t('settingsPages.backups.retentionBanner', {
          automaticDays: data.automaticRetentionDays,
          preRestoreDays: data.preRestoreRetentionDays,
        })}
      </div>
      {!data.automaticEnabled && (
        <p className="sysbackups-note">{t('settingsPages.backups.automaticDisabled')}</p>
      )}
      {/* Storage-healthcheck brancht op het stabiele boolveld; het label komt uit de
          vertaalbundel en nooit uit de (Nederlandstalige) servertekst. */}
      {data.storageAvailable ? (
        <p className="sysbackups-note">
          {t('settingsPages.backups.storageStatusLine', { status: t('settingsPages.system.storageAvailable') })}
        </p>
      ) : (
        <div className="sysbackups-storage-warning" role="alert">
          {t('settingsPages.backups.storageWarning', { status: t('settingsPages.system.storageUnavailable') })}
        </div>
      )}

      {canCreate && (
        <div className="sysbackups-toolbar">
          <Button onClick={() => setCreateOpen(true)} disabled={creating}>
            {creating ? t('settingsPages.backups.createBusy') : t('settingsPages.backups.create')}
          </Button>
        </div>
      )}

      <DataTable
        columns={columns}
        rows={data.backups}
        rowKey={(b) => `${b.id}-${b.fileName}`}
        emptyMessage={t('settingsPages.backups.empty')}
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
          title={t('settingsPages.backups.deleteTitle')}
          message={t('settingsPages.backups.deleteMessage', { fileName: deleteTarget.fileName })}
          confirmLabel={t('ui.actions.delete')}
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
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const canViewSystem = hasPermission('system_info.view')
  const canViewBackups = hasPermission('backups.view')

  const tabs: TabItem[] = useMemo(() => {
    const list: TabItem[] = []
    if (canViewSystem) list.push({ id: 'systeem', label: t('settingsPages.system.tabSystem') })
    if (canViewBackups) list.push({ id: 'backups', label: t('settingsPages.system.tabBackups') })
    return list
  }, [canViewSystem, canViewBackups, t])

  const requestedTab = searchParams.get('tab')
  const fallbackTab = (tabs[0]?.id ?? 'systeem') as TabId
  const tab: TabId =
    TAB_IDS.includes(requestedTab as TabId) && tabs.some((item) => item.id === requestedTab)
      ? (requestedTab as TabId)
      : fallbackTab

  function setTab(next: string) {
    setSearchParams(next === fallbackTab ? {} : { tab: next }, { replace: true })
  }

  if (tabs.length === 0) {
    return <p className="placeholder-text">{t('settingsPages.system.noPermission')}</p>
  }

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('navigation.menu.systemInfo') }]}
      />
      <PageHeader title={t('settingsPages.system.title')} subtitle={t('settingsPages.system.subtitle')} />

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
