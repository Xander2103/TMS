import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { ApiError } from '../../../api/apiClient'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import { resolvePackageIncident } from '../../packages/api/packagesApi'
import { PACKAGE_INCIDENT_ACTION_LABELS, type PackageIncidentAction } from '../../packages/types'
import {
  assignException,
  changeExceptionStatus,
  deleteExceptionPhoto,
  fetchExceptionPhotoUrl,
  getException,
  updateException,
} from '../api/exceptionsApi'
import {
  EXCEPTION_SEVERITIES,
  EXCEPTION_SEVERITY_ICONS,
  EXCEPTION_SEVERITY_LABELS,
  EXCEPTION_SEVERITY_TONE,
  EXCEPTION_STATUS_ICONS,
  EXCEPTION_STATUS_LABELS,
  EXCEPTION_STATUS_TONE,
  EXCEPTION_TYPE_LABELS,
  type ExceptionDetail,
  type ExceptionSeverity,
  type ExecutionExceptionStatus,
} from '../types'
import { formatDateTime as formatDateTimeIso } from '../../../utils/dates'
import '../components/exceptions.css'

function formatDateTime(value: string | null): string {
  return value ? formatDateTimeIso(value) : '—'
}

/** Dispositions offered per current package status; the server re-validates via the lifecycle machine. */
const PACKAGE_DISPOSITIONS: Record<string, PackageIncidentAction[]> = {
  Missing: ['Found', 'Return', 'Cancel'],
  Damaged: ['ReleaseToLoad', 'Return', 'Quarantine', 'Cancel'],
  Refused: ['Return', 'Redeliver', 'Quarantine'],
  DeliveryFailed: ['Return', 'Redeliver', 'Quarantine'],
  PartiallyDelivered: ['Return', 'Redeliver', 'Quarantine'],
  ReturnPending: ['Redeliver', 'Quarantine', 'Cancel'],
  ReturnedToDepot: ['Redeliver', 'ReturnToSender', 'Quarantine', 'Cancel'],
  Quarantined: ['Return', 'Redeliver', 'Cancel'],
}

export function ExceptionDetailPage() {
  const { t } = useLocale()
  const { id = '' } = useParams<{ id: string }>()
  const { showSuccess, showError } = useToast()
  const { hasPermission, user } = useAuth()

  const [detail, setDetail] = useState<ExceptionDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [photoUrls, setPhotoUrls] = useState<Record<string, string>>({})

  const [statusTarget, setStatusTarget] = useState<ExecutionExceptionStatus | null>(null)
  const [statusNote, setStatusNote] = useState('')

  const [editing, setEditing] = useState(false)
  const [severity, setSeverity] = useState<ExceptionSeverity>('Medium')
  const [dispatcherNotes, setDispatcherNotes] = useState('')
  const [customerVisible, setCustomerVisible] = useState(false)

  const [dispositionAction, setDispositionAction] = useState<PackageIncidentAction | null>(null)
  const [dispositionNote, setDispositionNote] = useState('')

  const canResolve = hasPermission('exceptions.resolve')
  const canDisposition = hasPermission('package_exceptions.manage')

  useEffect(() => {
    let mounted = true
    getException(id)
      .then((data) => {
        if (!mounted) return
        setDetail(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('exceptions.detail.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [id, t])

  // Photos need the bearer token, so they load as object URLs (revoked on unmount).
  useEffect(() => {
    if (!detail) return
    let mounted = true
    const urls: string[] = []
    void (async () => {
      for (const photo of detail.photos) {
        if (photoUrls[photo.id]) continue
        try {
          const url = await fetchExceptionPhotoUrl(detail.id, photo.id)
          urls.push(url)
          if (mounted) setPhotoUrls((prev) => ({ ...prev, [photo.id]: url }))
        } catch {
          // Photo stays hidden; the gallery shows the filename as fallback.
        }
      }
    })()
    return () => {
      mounted = false
      urls.forEach((url) => URL.revokeObjectURL(url))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detail])

  async function handleStatusSubmit() {
    if (!statusTarget) return
    const isTerminal = statusTarget === 'Resolved' || statusTarget === 'Rejected'
    if (isTerminal && !statusNote.trim()) {
      showError(t('exceptions.detail.noteRequired'))
      return
    }
    setBusy(true)
    try {
      const updated = await changeExceptionStatus(id, statusTarget, statusNote.trim() || null)
      setDetail(updated)
      setStatusTarget(null)
      setStatusNote('')
      showSuccess(t('exceptions.detail.statusChanged', { status: t(EXCEPTION_STATUS_LABELS[statusTarget]) }))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('exceptions.detail.statusChangeFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function toggleAssignment() {
    if (!detail) return
    setBusy(true)
    try {
      const isMine = detail.assignedToUserId === user?.id
      const updated = await assignException(id, isMine ? null : (user?.id ?? null))
      setDetail(updated)
      showSuccess(isMine ? t('exceptions.detail.unassigned') : t('exceptions.detail.assignedToYou'))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('exceptions.detail.assignFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function submitDisposition() {
    if (!detail?.packageId || !dispositionAction) return
    setBusy(true)
    try {
      await resolvePackageIncident(detail.packageId, dispositionAction, dispositionNote.trim())
      showSuccess(t('exceptions.detail.dispositionDone', { action: t(PACKAGE_INCIDENT_ACTION_LABELS[dispositionAction]) }))
      setDispositionAction(null)
      setDispositionNote('')
      setDetail(await getException(id))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('exceptions.detail.dispositionFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleEditSave() {
    setBusy(true)
    try {
      const updated = await updateException(id, {
        severity,
        dispatcherNotes: dispatcherNotes.trim() || null,
        customerVisible,
      })
      setDetail(updated)
      setEditing(false)
      showSuccess(t('exceptions.detail.updated'))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('exceptions.detail.updateFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleDeletePhoto(photoId: string) {
    setBusy(true)
    try {
      setDetail(await deleteExceptionPhoto(id, photoId))
      showSuccess(t('exceptions.detail.photoDeleted'))
    } catch {
      showError(t('exceptions.detail.photoDeleteFailed'))
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!detail) return <LoadingState message={t('exceptions.detail.loading')} />

  return (
    <div>
      <Breadcrumbs items={[{ label: t('exceptions.detail.breadcrumb'), to: '/exceptions' }, { label: t(EXCEPTION_TYPE_LABELS[detail.type]) }]} />
      <PageHeader
        title={`${t(EXCEPTION_TYPE_LABELS[detail.type])} — ${detail.tripNumber}`}
        subtitle={detail.reportedByName
          ? t('exceptions.detail.subtitleReportedBy', { date: formatDateTime(detail.occurredAt), name: detail.reportedByName })
          : t('exceptions.detail.subtitleReported', { date: formatDateTime(detail.occurredAt) })}
        action={
          <span className="exc-header-badges">
            <Badge tone={EXCEPTION_SEVERITY_TONE[detail.severity]}>
              {EXCEPTION_SEVERITY_ICONS[detail.severity]} {t(EXCEPTION_SEVERITY_LABELS[detail.severity])}
            </Badge>
            <Badge tone={EXCEPTION_STATUS_TONE[detail.status]}>
              {EXCEPTION_STATUS_ICONS[detail.status]} {t(EXCEPTION_STATUS_LABELS[detail.status])}
            </Badge>
          </span>
        }
      />

      {canDisposition && detail.packageId && detail.packageStatus
        && (PACKAGE_DISPOSITIONS[detail.packageStatus]?.length ?? 0) > 0 && (
        <section className="to-section">
          <h2>{t('exceptions.detail.dispositionTitle', { packageNumber: detail.packageNumber ?? '' })}</h2>
          <div className="exc-actions">
            {PACKAGE_DISPOSITIONS[detail.packageStatus].map((action) => (
              <Button
                key={action}
                variant="secondary"
                onClick={() => {
                  setDispositionAction(action)
                  setDispositionNote('')
                }}
                disabled={busy}
              >
                {t(PACKAGE_INCIDENT_ACTION_LABELS[action])}
              </Button>
            ))}
          </div>
        </section>
      )}

      {canResolve && (
        <div className="exc-actions">
          <Button variant="secondary" onClick={() => void toggleAssignment()} disabled={busy}>
            {detail.assignedToUserId === user?.id ? t('exceptions.detail.unassign') : t('exceptions.detail.assignToMe')}
          </Button>
          {detail.assignedToName && detail.assignedToUserId !== user?.id && (
            <span className="exc-assignee">{t('exceptions.detail.assignedTo', { name: detail.assignedToName })}</span>
          )}
        </div>
      )}

      {canResolve && detail.allowedTransitions.length > 0 && (
        <div className="exc-actions">
          {detail.allowedTransitions.map((target) => (
            <Button
              key={target}
              variant={target === 'Resolved' ? 'primary' : 'secondary'}
              onClick={() => {
                setStatusTarget(target)
                setStatusNote('')
              }}
              disabled={busy}
            >
              {EXCEPTION_STATUS_ICONS[target]} {t(EXCEPTION_STATUS_LABELS[target])}
            </Button>
          ))}
        </div>
      )}

      <section className="to-section">
        <h2>{t('exceptions.detail.reportTitle')}</h2>
        <dl className="to-facts">
          <div>
            <dt>{t('exceptions.detail.description')}</dt>
            <dd>{detail.description}</dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.trip')}</dt>
            <dd>
              <Link to={`/planning/${detail.tripId}`}>{detail.tripNumber}</Link>
            </dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.order')}</dt>
            <dd>
              {detail.transportOrderId ? (
                <Link to={`/transport-orders/${detail.transportOrderId}`}>{detail.orderNumber}</Link>
              ) : (
                '—'
              )}
            </dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.stop')}</dt>
            <dd>{detail.stopLabel ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.cargoLine')}</dt>
            <dd>{detail.cargoDescription ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.package')}</dt>
            <dd>
              {detail.packageNumber ? (
                <>
                  <code>{detail.packageNumber}</code>
                  {detail.packageStatus && <> — {detail.packageStatus}</>}
                </>
              ) : (
                '—'
              )}
            </dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.quantity')}</dt>
            <dd>{detail.quantity ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.driver')}</dt>
            <dd>{detail.driverName ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('exceptions.detail.gps')}</dt>
            <dd>{detail.latitude !== null && detail.longitude !== null ? `${detail.latitude}, ${detail.longitude}` : '—'}</dd>
          </div>
        </dl>
      </section>

      <section className="to-section">
        <h2>{t('exceptions.detail.handlingTitle')}</h2>
        {editing ? (
          <div className="exc-edit">
            <FormField label={t('exceptions.detail.severityLabel')} htmlFor="exc-edit-severity">
              <select id="exc-edit-severity" value={severity} onChange={(e) => setSeverity(e.target.value as ExceptionSeverity)} disabled={busy}>
                {EXCEPTION_SEVERITIES.map((s) => (
                  <option key={s} value={s}>
                    {t(EXCEPTION_SEVERITY_LABELS[s])}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label={t('exceptions.detail.dispatcherNotes')} htmlFor="exc-edit-notes">
              <textarea id="exc-edit-notes" rows={3} value={dispatcherNotes} onChange={(e) => setDispatcherNotes(e.target.value)} disabled={busy} maxLength={4000} />
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={customerVisible} onChange={(e) => setCustomerVisible(e.target.checked)} disabled={busy} />
              {t('exceptions.detail.customerVisible')}
            </label>
            <div className="exc-edit-actions">
              <Button variant="secondary" onClick={() => setEditing(false)} disabled={busy}>
                {t('exceptions.detail.cancel')}
              </Button>
              <Button onClick={() => void handleEditSave()} disabled={busy}>
                {t('exceptions.detail.save')}
              </Button>
            </div>
          </div>
        ) : (
          <>
            <dl className="to-facts">
              <div>
                <dt>{t('exceptions.detail.dispatcherNotes')}</dt>
                <dd className="exc-multiline">{detail.dispatcherNotes ?? '—'}</dd>
              </div>
              <div>
                <dt>{t('exceptions.detail.customerVisible')}</dt>
                <dd>{detail.customerVisible ? t('exceptions.detail.yes') : t('exceptions.detail.no')}</dd>
              </div>
              <div>
                <dt>{t('exceptions.detail.resolutionNote')}</dt>
                <dd className="exc-multiline">{detail.resolutionNote ?? '—'}</dd>
              </div>
              <div>
                <dt>{t('exceptions.detail.resolvedBy')}</dt>
                <dd>
                  {detail.resolvedByName ?? '—'}
                  {detail.resolvedAt && ` · ${formatDateTime(detail.resolvedAt)}`}
                </dd>
              </div>
            </dl>
            {canResolve && (
              <Button
                variant="secondary"
                onClick={() => {
                  setSeverity(detail.severity)
                  setDispatcherNotes(detail.dispatcherNotes ?? '')
                  setCustomerVisible(detail.customerVisible)
                  setEditing(true)
                }}
                disabled={busy}
              >
                {t('exceptions.detail.edit')}
              </Button>
            )}
          </>
        )}
      </section>

      {detail.photos.length > 0 && (
        <section className="to-section">
          <h2>{t('exceptions.detail.photosTitle', { count: detail.photos.length })}</h2>
          <div className="exc-photos">
            {detail.photos.map((photo) => (
              <figure key={photo.id} className="exc-photo">
                {photoUrls[photo.id] ? (
                  <a href={photoUrls[photo.id]} target="_blank" rel="noreferrer">
                    <img src={photoUrls[photo.id]} alt={photo.fileName} />
                  </a>
                ) : (
                  <span className="exc-photo-placeholder">{photo.fileName}</span>
                )}
                <figcaption>
                  {photo.fileName}
                  {canResolve && (
                    <button type="button" className="exc-photo-delete" onClick={() => void handleDeletePhoto(photo.id)} disabled={busy}>
                      {t('exceptions.detail.deletePhoto')}
                    </button>
                  )}
                </figcaption>
              </figure>
            ))}
          </div>
        </section>
      )}

      <section className="to-section">
        <h2>{t('exceptions.detail.historyTitle')}</h2>
        <AuditHistoryPanel entityType="ExecutionException" entityId={detail.id} />
      </section>

      {dispositionAction && (
        <Modal
          title={t('exceptions.detail.dispositionModalTitle', { packageNumber: detail.packageNumber ?? '', action: t(PACKAGE_INCIDENT_ACTION_LABELS[dispositionAction]) })}
          onClose={() => setDispositionAction(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDispositionAction(null)} disabled={busy}>
                {t('exceptions.detail.cancel')}
              </Button>
              <Button
                onClick={() => void submitDisposition()}
                disabled={busy || !dispositionNote.trim()}
              >
                {t('exceptions.detail.confirm')}
              </Button>
            </>
          }
        >
          <FormField label={t('exceptions.detail.noteLabel')} htmlFor="disposition-note" required>
            <input
              id="disposition-note"
              value={dispositionNote}
              onChange={(e) => setDispositionNote(e.target.value)}
              disabled={busy}
              maxLength={500}
              autoFocus
            />
          </FormField>
        </Modal>
      )}

      {statusTarget && (
        <Modal
          title={t('exceptions.detail.statusModalTitle', { status: t(EXCEPTION_STATUS_LABELS[statusTarget]) })}
          onClose={() => setStatusTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setStatusTarget(null)} disabled={busy}>
                {t('exceptions.detail.cancel')}
              </Button>
              <Button onClick={() => void handleStatusSubmit()} disabled={busy}>
                {busy ? t('exceptions.detail.busy') : t('exceptions.detail.confirm')}
              </Button>
            </>
          }
        >
          <FormField
            label={statusTarget === 'Resolved' || statusTarget === 'Rejected' ? t('exceptions.detail.resolutionNoteLabel') : t('exceptions.detail.optionalNoteLabel')}
            htmlFor="exc-status-note"
            required={statusTarget === 'Resolved' || statusTarget === 'Rejected'}
            hint={t('exceptions.detail.statusNoteHint')}
          >
            <textarea
              id="exc-status-note"
              rows={3}
              value={statusNote}
              onChange={(e) => setStatusNote(e.target.value)}
              maxLength={2000}
              autoFocus
            />
          </FormField>
        </Modal>
      )}
    </div>
  )
}
