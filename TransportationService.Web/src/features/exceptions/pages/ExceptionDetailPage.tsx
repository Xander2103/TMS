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
import '../components/exceptions.css'

function formatDateTime(value: string | null): string {
  return value ? value.slice(0, 16).replace('T', ' ') : '—'
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
        if (mounted) setLoadError('De afwijking kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

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
      showError('Een afhandelingsnotitie is verplicht bij het afsluiten.')
      return
    }
    setBusy(true)
    try {
      const updated = await changeExceptionStatus(id, statusTarget, statusNote.trim() || null)
      setDetail(updated)
      setStatusTarget(null)
      setStatusNote('')
      showSuccess(`Status gewijzigd naar ${EXCEPTION_STATUS_LABELS[statusTarget]}.`)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De status kon niet worden gewijzigd.')
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
      showSuccess(isMine ? 'Toewijzing opgeheven.' : 'Aan jou toegewezen.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De toewijzing kon niet worden gewijzigd.')
    } finally {
      setBusy(false)
    }
  }

  async function submitDisposition() {
    if (!detail?.packageId || !dispositionAction) return
    setBusy(true)
    try {
      await resolvePackageIncident(detail.packageId, dispositionAction, dispositionNote.trim())
      showSuccess(`Vervolgactie '${PACKAGE_INCIDENT_ACTION_LABELS[dispositionAction]}' uitgevoerd.`)
      setDispositionAction(null)
      setDispositionNote('')
      setDetail(await getException(id))
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De vervolgactie kon niet worden uitgevoerd.')
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
      showSuccess('Afwijking bijgewerkt.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De afwijking kon niet worden bijgewerkt.')
    } finally {
      setBusy(false)
    }
  }

  async function handleDeletePhoto(photoId: string) {
    setBusy(true)
    try {
      setDetail(await deleteExceptionPhoto(id, photoId))
      showSuccess('Foto verwijderd.')
    } catch {
      showError('De foto kon niet worden verwijderd.')
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!detail) return <LoadingState message="Afwijking laden..." />

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Afwijkingen', to: '/exceptions' }, { label: EXCEPTION_TYPE_LABELS[detail.type] }]} />
      <PageHeader
        title={`${EXCEPTION_TYPE_LABELS[detail.type]} — ${detail.tripNumber}`}
        subtitle={`Gemeld ${formatDateTime(detail.occurredAt)}${detail.reportedByName ? ` door ${detail.reportedByName}` : ''}`}
        action={
          <span className="exc-header-badges">
            <Badge tone={EXCEPTION_SEVERITY_TONE[detail.severity]}>
              {EXCEPTION_SEVERITY_ICONS[detail.severity]} {EXCEPTION_SEVERITY_LABELS[detail.severity]}
            </Badge>
            <Badge tone={EXCEPTION_STATUS_TONE[detail.status]}>
              {EXCEPTION_STATUS_ICONS[detail.status]} {EXCEPTION_STATUS_LABELS[detail.status]}
            </Badge>
          </span>
        }
      />

      {canDisposition && detail.packageId && detail.packageStatus
        && (PACKAGE_DISPOSITIONS[detail.packageStatus]?.length ?? 0) > 0 && (
        <section className="to-section">
          <h2>Vervolgactie colli {detail.packageNumber}</h2>
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
                {PACKAGE_INCIDENT_ACTION_LABELS[action]}
              </Button>
            ))}
          </div>
        </section>
      )}

      {canResolve && (
        <div className="exc-actions">
          <Button variant="secondary" onClick={() => void toggleAssignment()} disabled={busy}>
            {detail.assignedToUserId === user?.id ? 'Toewijzing opheffen' : 'Aan mij toewijzen'}
          </Button>
          {detail.assignedToName && detail.assignedToUserId !== user?.id && (
            <span className="exc-assignee">Toegewezen aan {detail.assignedToName}</span>
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
              {EXCEPTION_STATUS_ICONS[target]} {EXCEPTION_STATUS_LABELS[target]}
            </Button>
          ))}
        </div>
      )}

      <section className="to-section">
        <h2>Melding</h2>
        <dl className="to-facts">
          <div>
            <dt>Omschrijving</dt>
            <dd>{detail.description}</dd>
          </div>
          <div>
            <dt>Rit</dt>
            <dd>
              <Link to={`/planning/${detail.tripId}`}>{detail.tripNumber}</Link>
            </dd>
          </div>
          <div>
            <dt>Opdracht</dt>
            <dd>
              {detail.transportOrderId ? (
                <Link to={`/transport-orders/${detail.transportOrderId}`}>{detail.orderNumber}</Link>
              ) : (
                '—'
              )}
            </dd>
          </div>
          <div>
            <dt>Stop</dt>
            <dd>{detail.stopLabel ?? '—'}</dd>
          </div>
          <div>
            <dt>Goederenlijn</dt>
            <dd>{detail.cargoDescription ?? '—'}</dd>
          </div>
          <div>
            <dt>Colli</dt>
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
            <dt>Aantal</dt>
            <dd>{detail.quantity ?? '—'}</dd>
          </div>
          <div>
            <dt>Chauffeur</dt>
            <dd>{detail.driverName ?? '—'}</dd>
          </div>
          <div>
            <dt>Locatie (GPS)</dt>
            <dd>{detail.latitude !== null && detail.longitude !== null ? `${detail.latitude}, ${detail.longitude}` : '—'}</dd>
          </div>
        </dl>
      </section>

      <section className="to-section">
        <h2>Afhandeling</h2>
        {editing ? (
          <div className="exc-edit">
            <FormField label="Ernst" htmlFor="exc-edit-severity">
              <select id="exc-edit-severity" value={severity} onChange={(e) => setSeverity(e.target.value as ExceptionSeverity)} disabled={busy}>
                {EXCEPTION_SEVERITIES.map((s) => (
                  <option key={s} value={s}>
                    {EXCEPTION_SEVERITY_LABELS[s]}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Dispatchernotities" htmlFor="exc-edit-notes">
              <textarea id="exc-edit-notes" rows={3} value={dispatcherNotes} onChange={(e) => setDispatcherNotes(e.target.value)} disabled={busy} maxLength={4000} />
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={customerVisible} onChange={(e) => setCustomerVisible(e.target.checked)} disabled={busy} />
              Zichtbaar voor klant
            </label>
            <div className="exc-edit-actions">
              <Button variant="secondary" onClick={() => setEditing(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button onClick={() => void handleEditSave()} disabled={busy}>
                Opslaan
              </Button>
            </div>
          </div>
        ) : (
          <>
            <dl className="to-facts">
              <div>
                <dt>Dispatchernotities</dt>
                <dd className="exc-multiline">{detail.dispatcherNotes ?? '—'}</dd>
              </div>
              <div>
                <dt>Zichtbaar voor klant</dt>
                <dd>{detail.customerVisible ? 'Ja' : 'Nee'}</dd>
              </div>
              <div>
                <dt>Afhandelingsnotitie</dt>
                <dd className="exc-multiline">{detail.resolutionNote ?? '—'}</dd>
              </div>
              <div>
                <dt>Afgehandeld door</dt>
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
                Bewerken
              </Button>
            )}
          </>
        )}
      </section>

      {detail.photos.length > 0 && (
        <section className="to-section">
          <h2>Foto's ({detail.photos.length})</h2>
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
                      Verwijderen
                    </button>
                  )}
                </figcaption>
              </figure>
            ))}
          </div>
        </section>
      )}

      <section className="to-section">
        <h2>Historiek</h2>
        <AuditHistoryPanel entityType="ExecutionException" entityId={detail.id} />
      </section>

      {dispositionAction && (
        <Modal
          title={`Colli ${detail.packageNumber}: ${PACKAGE_INCIDENT_ACTION_LABELS[dispositionAction]}`}
          onClose={() => setDispositionAction(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDispositionAction(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button
                onClick={() => void submitDisposition()}
                disabled={busy || !dispositionNote.trim()}
              >
                Bevestigen
              </Button>
            </>
          }
        >
          <FormField label="Toelichting" htmlFor="disposition-note" required>
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
          title={`Status wijzigen naar: ${EXCEPTION_STATUS_LABELS[statusTarget]}`}
          onClose={() => setStatusTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setStatusTarget(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button onClick={() => void handleStatusSubmit()} disabled={busy}>
                {busy ? 'Bezig…' : 'Bevestigen'}
              </Button>
            </>
          }
        >
          <FormField
            label={statusTarget === 'Resolved' || statusTarget === 'Rejected' ? 'Afhandelingsnotitie' : 'Notitie (optioneel)'}
            htmlFor="exc-status-note"
            required={statusTarget === 'Resolved' || statusTarget === 'Rejected'}
            hint="De melder ontvangt deze beslissing als notificatie."
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
