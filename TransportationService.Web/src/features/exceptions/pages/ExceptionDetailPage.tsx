import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { isNotFoundError } from '../../../api/notFound'
import { ActionMenu, type ActionMenuItem } from '../../../components/ui/ActionMenu'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DetailCard, DetailGrid } from '../../../components/ui/DetailCard'
import { FactList } from '../../../components/ui/FactList'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { localizeApiError } from '../../../api/problemDetails'
import { getAssignableUsers } from '../../incidents/api/incidentsApi'
import { DAMAGE_SEVERITIES, suggestedDamageSeverity, type LinkedDamageSeverity } from '../../incidents/types'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { ApiError } from '../../../api/apiClient'
import { AuditHistoryPanel } from '../../auditing/components/AuditHistoryPanel'
import type { AuditValueLabels } from '../../auditing/formatAuditValues'
import { resolvePackageIncident } from '../../packages/api/packagesApi'
import { PACKAGE_INCIDENT_ACTION_LABELS, PACKAGE_STATUS_LABELS, type PackageIncidentAction, type PackageLifecycleStatus } from '../../packages/types'
import {
  assignException,
  changeExceptionStatus,
  createExceptionDamageReport,
  deleteExceptionPhoto,
  escalateException,
  fetchExceptionPhotoUrl,
  getException,
  updateException,
  updateExceptionCustomerMessage,
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
  type ExecutionExceptionType,
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

/** Button/menu wording per target status: a verb, not "Naar <status>". */
const TRANSITION_LABELS: Record<ExecutionExceptionStatus, string> = {
  Open: 'exceptions.detail.transition.Open',
  Investigating: 'exceptions.detail.transition.Investigating',
  Resolved: 'exceptions.detail.transition.Resolved',
  Rejected: 'exceptions.detail.transition.Rejected',
  CustomerActionRequired: 'exceptions.detail.transition.CustomerActionRequired',
}

/** Server enum order of ExecutionExceptionType (audit payloads store ordinals). */
const EXCEPTION_TYPE_ORDER: ExecutionExceptionType[] = [
  'MissingPackage', 'WrongPackage', 'DamagedPackage', 'UnknownPackage', 'WrongRoutePackage', 'WrongStopPackage',
  'IncompleteQuantity', 'UnreadableBarcode', 'LabelMissing', 'RejectedBeforeLoading', 'RejectedDelivery',
  'PartialDelivery', 'AddressIssue', 'CustomerUnavailable', 'AccessDenied',
] as ExecutionExceptionType[]
const EXCEPTION_SEVERITY_ORDER: ExceptionSeverity[] = ['Low', 'Medium', 'High', 'Critical']
const EXCEPTION_STATUS_ORDER: ExecutionExceptionStatus[] = ['Open', 'Investigating', 'Resolved', 'Rejected', 'CustomerActionRequired']

function byOrdinalOrName<T extends string>(order: T[], labels: Record<T, string>, t: (key: string) => string) {
  return (value: unknown): string | undefined => {
    const code = typeof value === 'number' ? order[value] : (typeof value === 'string' && value in labels ? (value as T) : undefined)
    return code ? t(labels[code]) : undefined
  }
}

export function ExceptionDetailPage() {
  const { t } = useLocale()
  const exceptionAuditLabels = useMemo<AuditValueLabels>(() => ({
    Type: byOrdinalOrName(EXCEPTION_TYPE_ORDER, EXCEPTION_TYPE_LABELS, t),
    Severity: byOrdinalOrName(EXCEPTION_SEVERITY_ORDER, EXCEPTION_SEVERITY_LABELS, t),
    Status: byOrdinalOrName(EXCEPTION_STATUS_ORDER, EXCEPTION_STATUS_LABELS, t),
  }), [t])
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
  const [photoToDelete, setPhotoToDelete] = useState<string | null>(null)
  const [severity, setSeverity] = useState<ExceptionSeverity>('Medium')
  const [dispatcherNotes, setDispatcherNotes] = useState('')
  const [customerVisible, setCustomerVisible] = useState(false)

  const [dispositionAction, setDispositionAction] = useState<PackageIncidentAction | null>(null)
  const [dispositionNote, setDispositionNote] = useState('')

  const canResolve = hasPermission('exceptions.resolve')
  const canDisposition = hasPermission('package_exceptions.manage')
  const canEscalate = hasPermission('incidents.manage')
  const canCreateDamage = canResolve
  const navigate = useNavigate()

  const [assignDialog, setAssignDialog] = useState(false)
  const [assignOptions, setAssignOptions] = useState<SearchableSelectOption[]>([])
  const [assignee, setAssignee] = useState<string | null>(null)
  const [messageEditing, setMessageEditing] = useState(false)
  const [customerMessage, setCustomerMessage] = useState('')
  const [damageDialog, setDamageDialog] = useState(false)
  const [damageSeverity, setDamageSeverity] = useState<LinkedDamageSeverity>('Moderate')

  useEffect(() => {
    let mounted = true
    getException(id)
      .then((data) => {
        if (!mounted) return
        setDetail(data)
        setLoadError(null)
        // Another exception opened in the same page instance: never carry an edit or dialog over.
        setEditing(false)
        setStatusTarget(null)
        setDispositionAction(null)
      })
      .catch((err: unknown) => {
        // M14: a record that does not exist says so, with the way back to the list.
        if (mounted) setLoadError(t(isNotFoundError(err) ? 'exceptions.detail.notFound' : 'exceptions.detail.loadError'))
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

  function openAssign() {
    setAssignee(detail?.assignedToUserId ?? null)
    setAssignDialog(true)
    // EX-07: the dedicated, safe picker endpoint — no general users.view needed.
    getAssignableUsers()
      .then((users) => setAssignOptions(users.map((u) => ({ value: u.id, label: u.displayName, description: u.roleName ?? undefined }))))
      .catch(() => setAssignOptions([]))
  }

  async function submitAssign() {
    setBusy(true)
    try {
      setDetail(await assignException(id, assignee))
      setAssignDialog(false)
      showSuccess(assignee ? t('exceptions.detail.assigned') : t('exceptions.detail.unassigned'))
    } catch (err) {
      showError(localizeApiError(t, err, t('exceptions.detail.assignFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function escalate() {
    setBusy(true)
    try {
      const incident = await escalateException(id)
      showSuccess(t('exceptions.detail.escalated'))
      navigate(`/incidents/${incident.id}`)
    } catch (err) {
      showError(localizeApiError(t, err, t('exceptions.detail.escalateFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function saveCustomerMessage() {
    setBusy(true)
    try {
      setDetail(await updateExceptionCustomerMessage(id, customerMessage.trim() || null))
      setMessageEditing(false)
      showSuccess(t('exceptions.detail.customerMessageSaved'))
    } catch (err) {
      showError(localizeApiError(t, err, t('exceptions.detail.updateFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function submitDamage() {
    setBusy(true)
    try {
      setDetail(await createExceptionDamageReport(id, { severity: damageSeverity }))
      setDamageDialog(false)
      showSuccess(t('exceptions.detail.damageCreated'))
    } catch (err) {
      showError(localizeApiError(t, err, t('exceptions.detail.damageFailed')))
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
    } catch (err) {
      showError(localizeApiError(t, err, t('exceptions.detail.photoDeleteFailed')))
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} backTo="/exceptions" />
  if (!detail) return <LoadingState message={t('exceptions.detail.loading')} />

  // B05: leaving the page with unsaved edits asks first.
  const editDirty =
    editing &&
    (severity !== detail.severity ||
      dispatcherNotes.trim() !== (detail.dispatcherNotes ?? '') ||
      customerVisible !== detail.customerVisible)

  const isMine = detail.assignedToUserId === user?.id
  const allowed = canResolve ? detail.allowedTransitions : []
  const openStatusDialog = (target: ExecutionExceptionStatus) => {
    setStatusTarget(target)
    setStatusNote('')
  }

  // B24: one sensible next step in the header; the other moves (and rejecting) in Meer ▾.
  let nextTarget: ExecutionExceptionStatus | null = null
  if (detail.status === 'Open' && allowed.includes('Investigating')) nextTarget = 'Investigating'
  else if (allowed.includes('Resolved')) nextTarget = 'Resolved'
  const menuItems: ActionMenuItem[] = [
    ...allowed
      .filter((target) => target !== nextTarget && target !== 'Rejected')
      .map((target) => ({
        key: target,
        label: t(TRANSITION_LABELS[target]),
        onSelect: () => openStatusDialog(target),
      })),
    ...(canResolve ? [{ key: 'assign', label: t('exceptions.detail.chooseResponsible'), onSelect: openAssign }] : []),
    ...(canResolve && isMine ? [{ key: 'unassign', label: t('exceptions.detail.unassign'), onSelect: () => void toggleAssignment() }] : []),
    ...(canEscalate && !detail.escalatedIncidentId && detail.status !== 'Rejected'
      ? [{ key: 'escalate', label: t('exceptions.detail.escalate'), onSelect: () => void escalate() }]
      : []),
    ...(canCreateDamage && detail.canCreateDamageReport
      ? [{
          key: 'damage',
          label: t('exceptions.detail.createDamage'),
          onSelect: () => {
            setDamageSeverity(suggestedDamageSeverity(detail.severity))
            setDamageDialog(true)
          },
        }]
      : []),
    ...(allowed.includes('Rejected')
      ? [{ key: 'Rejected', label: t(TRANSITION_LABELS.Rejected), danger: true, onSelect: () => openStatusDialog('Rejected') }]
      : []),
  ]
  const headerActions = canResolve ? (
    <>
      {!isMine && (
        <Button variant="secondary" onClick={() => void toggleAssignment()} disabled={busy || editing}>
          {t('exceptions.detail.assignToMe')}
        </Button>
      )}
      <ActionMenu items={menuItems} disabled={busy || editing} />
      {nextTarget && (
        <Button
          // Resolving asks for a note and is final: calm secondary, never the loudest button.
          variant={nextTarget === 'Resolved' ? 'secondary' : 'primary'}
          onClick={() => openStatusDialog(nextTarget)}
          disabled={busy || editing}
        >
          {t(TRANSITION_LABELS[nextTarget])}
        </Button>
      )}
    </>
  ) : undefined

  const typeLabel = t(EXCEPTION_TYPE_LABELS[detail.type])
  const packageStatusLabel = detail.packageStatus
    ? (PACKAGE_STATUS_LABELS[detail.packageStatus as PackageLifecycleStatus] ? t(PACKAGE_STATUS_LABELS[detail.packageStatus as PackageLifecycleStatus]) : detail.packageStatus)
    : null
  const hasGps = detail.latitude !== null && detail.longitude !== null
  const terminal = detail.status === 'Resolved' || detail.status === 'Rejected'

  return (
    <div>
      <Breadcrumbs items={[{ label: t('exceptions.detail.breadcrumb'), to: '/exceptions' }, { label: `${typeLabel} — ${detail.tripNumber}` }]} />
      <PageHeader
        title={`${typeLabel} — ${detail.tripNumber}`}
        badges={
          <>
            <Badge tone={EXCEPTION_STATUS_TONE[detail.status]}>
              {EXCEPTION_STATUS_ICONS[detail.status]} {t(EXCEPTION_STATUS_LABELS[detail.status])}
            </Badge>
            <Badge tone={EXCEPTION_SEVERITY_TONE[detail.severity]}>
              {EXCEPTION_SEVERITY_ICONS[detail.severity]} {t(EXCEPTION_SEVERITY_LABELS[detail.severity])}
            </Badge>
          </>
        }
        subtitle={detail.reportedByName
          ? t('exceptions.detail.subtitleReportedBy', { date: formatDateTime(detail.occurredAt), name: detail.reportedByName })
          : t('exceptions.detail.subtitleReported', { date: formatDateTime(detail.occurredAt) })}
        action={headerActions}
      />

      <UnsavedChangesGuard when={editDirty && !busy} />
      {detail.vehicleSafetyIssue && !detail.damageReportId && (
        <p className="exc-safety-banner" role="status">{t('exceptions.detail.vehicleSafety', { vehicle: detail.vehicleLabel ?? '—' })}</p>
      )}

      {terminal && detail.resolutionNote && (
        <DetailCard title={t(detail.status === 'Resolved' ? 'exceptions.detail.outcomeResolved' : 'exceptions.detail.outcomeRejected')} id="exception-outcome" className="exc-outcome-card">
          <p className="exc-description">{detail.resolutionNote}</p>
          <p className="ui-detail-empty">
            {[detail.resolvedByName, detail.resolvedAt ? formatDateTime(detail.resolvedAt) : null].filter(Boolean).join(' · ')}
          </p>
        </DetailCard>
      )}

      <DetailGrid>
        <DetailCard title={t('exceptions.detail.reportTitle')} id="exception-report" className="ui-detail-card-wide">
          <p className="exc-description">{detail.description}</p>
          <FactList
            columns={2}
            facts={[
              { key: 'status', label: t('exceptions.detail.status'), value: t(EXCEPTION_STATUS_LABELS[detail.status]) },
              { key: 'trip', label: t('exceptions.detail.trip'), value: <Link className="ui-id-link" to={`/planning/${detail.tripId}`}>{detail.tripNumber}</Link> },
              { key: 'type', label: t('exceptions.detail.type'), value: typeLabel },
              {
                key: 'order',
                label: t('exceptions.detail.order'),
                value: detail.transportOrderId ? <Link className="ui-id-link" to={`/transport-orders/${detail.transportOrderId}`}>{detail.orderNumber}</Link> : null,
              },
              { key: 'severity', label: t('exceptions.detail.severityLabel'), value: t(EXCEPTION_SEVERITY_LABELS[detail.severity]) },
              {
                key: 'dossier',
                label: t('exceptions.detail.dossier'),
                value: detail.dossierId ? <Link className="ui-id-link" to={`/dossiers/${detail.dossierId}`}>{detail.dossierNumber}</Link> : null,
              },
              { key: 'assignee', label: t('exceptions.detail.assignee'), value: detail.assignedToName },
              {
                key: 'customer',
                label: t('exceptions.detail.customer'),
                value: detail.customerId ? <Link to={`/customers/${detail.customerId}`}>{detail.customerName}</Link> : null,
              },
              { key: 'driver', label: t('exceptions.detail.driver'), value: detail.driverName },
              !!detail.vehicleLabel && {
                key: 'vehicle',
                label: t('exceptions.detail.vehicle'),
                value: detail.vehicleId ? <Link className="ui-id-link" to={`/vehicles/${detail.vehicleId}`}>{detail.vehicleLabel}</Link> : detail.vehicleLabel,
              },
              !!detail.escalatedIncidentId && {
                key: 'incident',
                label: t('exceptions.detail.escalatedIncident'),
                value: <Link to={`/incidents/${detail.escalatedIncidentId}`}>{detail.escalatedIncidentTitle ?? t('exceptions.detail.openIncident')}</Link>,
              },
              !!detail.damageReportId && {
                key: 'damage',
                label: t('exceptions.detail.damageReport'),
                value: detail.vehicleId ? <Link to={`/vehicles/${detail.vehicleId}?tab=schade`}>{t('exceptions.detail.openDamage')}</Link> : t('exceptions.detail.damageRegistered'),
              },
              !!detail.stopLabel && { key: 'stop', label: t('exceptions.detail.stop'), value: detail.stopLabel },
              !!detail.cargoDescription && { key: 'cargo', label: t('exceptions.detail.cargoLine'), value: detail.cargoDescription },
              !!detail.packageNumber && {
                key: 'package',
                label: t('exceptions.detail.package'),
                value: (
                  <>
                    <code>{detail.packageNumber}</code>
                    {packageStatusLabel && <> · {packageStatusLabel}</>}
                  </>
                ),
              },
              detail.quantity !== null && { key: 'quantity', label: t('exceptions.detail.quantity'), value: String(detail.quantity) },
              hasGps && { key: 'gps', label: t('exceptions.detail.gps'), value: `${detail.latitude}, ${detail.longitude}` },
            ]}
          />
        </DetailCard>

        {canDisposition && detail.packageId && detail.packageStatus && (PACKAGE_DISPOSITIONS[detail.packageStatus]?.length ?? 0) > 0 && (
          <DetailCard title={t('exceptions.detail.dispositionTitle', { packageNumber: detail.packageNumber ?? '' })} id="exception-disposition">
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
          </DetailCard>
        )}

        <DetailCard
          title={t('exceptions.detail.handlingTitle')}
          id="exception-handling"
          action={
            canResolve && !editing ? (
              <Button
                variant="ghost"
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
            ) : undefined
          }
        >
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
                <Button onClick={() => void handleEditSave()} disabled={busy || !editDirty}>
                  {t('exceptions.detail.save')}
                </Button>
              </div>
            </div>
          ) : (
            <FactList
              facts={[
                { key: 'notes', label: t('exceptions.detail.dispatcherNotes'), value: detail.dispatcherNotes ? <span className="exc-multiline">{detail.dispatcherNotes}</span> : null },
                { key: 'visible', label: t('exceptions.detail.customerVisible'), value: detail.customerVisible ? t('exceptions.detail.yes') : t('exceptions.detail.no') },
                !terminal && { key: 'resolution', label: t('exceptions.detail.resolutionNote'), value: detail.resolutionNote ? <span className="exc-multiline">{detail.resolutionNote}</span> : null },
                !terminal && !!detail.resolvedByName && { key: 'resolvedBy', label: t('exceptions.detail.resolvedBy'), value: detail.resolvedByName },
              ]}
            />
          )}
        </DetailCard>

        <DetailCard
          title={t('exceptions.detail.customerMessageTitle')}
          id="exception-customer-message"
          action={
            canResolve && !messageEditing ? (
              <Button
                variant="ghost"
                onClick={() => {
                  setCustomerMessage(detail.customerMessage ?? '')
                  setMessageEditing(true)
                }}
                disabled={busy}
              >
                {t('exceptions.detail.edit')}
              </Button>
            ) : undefined
          }
        >
          {messageEditing ? (
            <div className="exc-edit">
              <FormField label={t('exceptions.detail.customerMessageLabel')} htmlFor="exc-customer-message" hint={t('exceptions.detail.customerMessageHint')}>
                <textarea id="exc-customer-message" rows={3} value={customerMessage} onChange={(e) => setCustomerMessage(e.target.value)} maxLength={2000} disabled={busy} />
              </FormField>
              <div className="exc-edit-actions">
                <Button variant="secondary" onClick={() => setMessageEditing(false)} disabled={busy}>{t('exceptions.detail.cancel')}</Button>
                <Button onClick={() => void saveCustomerMessage()} disabled={busy}>{t('exceptions.detail.save')}</Button>
              </div>
            </div>
          ) : detail.customerMessage ? (
            <p className="exc-description">{detail.customerMessage}</p>
          ) : (
            <p className="ui-detail-empty">{t('exceptions.detail.noCustomerMessage')}</p>
          )}
          {!detail.customerVisible && <p className="ui-detail-empty">{t('exceptions.detail.notCustomerVisible')}</p>}
        </DetailCard>

        {detail.photos.length > 0 && (
          <DetailCard title={t('exceptions.detail.photosTitle', { count: detail.photos.length })} id="exception-photos">
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
                    <span className="exc-photo-name" title={photo.fileName}>{photo.fileName}</span>
                    {canResolve && !detail.photosLocked && (
                      <button type="button" className="exc-photo-delete" onClick={() => setPhotoToDelete(photo.id)} disabled={busy}>
                        {t('exceptions.detail.deletePhoto')}
                      </button>
                    )}
                  </figcaption>
                </figure>
              ))}
            </div>
            {detail.photosLocked && <p className="ui-detail-empty">{t('exceptions.detail.photosLocked')}</p>}
          </DetailCard>
        )}
      </DetailGrid>

      <DetailCard title={t('exceptions.detail.historyTitle')} id="exception-history">
        {/* Re-read after every in-page change, so the new step appears without a reload. */}
        <AuditHistoryPanel
          key={JSON.stringify([detail.status, detail.severity, detail.assignedToUserId, detail.dispatcherNotes, detail.customerVisible, detail.photos.length])}
          entityType="ExecutionException"
          entityId={detail.id}
          valueLabels={exceptionAuditLabels}
          historyPath={`/api/exceptions/${detail.id}/history`}
        />
      </DetailCard>

      {assignDialog && (
        <Modal
          title={t('exceptions.detail.chooseResponsible')}
          onClose={() => setAssignDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setAssignDialog(false)} disabled={busy}>{t('exceptions.detail.cancel')}</Button>
              <Button onClick={() => void submitAssign()} disabled={busy}>{t('exceptions.detail.save')}</Button>
            </>
          }
        >
          <FormField label={t('exceptions.detail.assignee')} htmlFor="exc-assignee">
            <SearchableSelect id="exc-assignee" value={assignee} onChange={setAssignee} options={assignOptions} />
          </FormField>
        </Modal>
      )}

      {damageDialog && (
        <Modal
          title={t('exceptions.detail.createDamage')}
          onClose={() => setDamageDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDamageDialog(false)} disabled={busy}>{t('exceptions.detail.cancel')}</Button>
              <Button onClick={() => void submitDamage()} disabled={busy}>{t('exceptions.detail.createDamage')}</Button>
            </>
          }
        >
          <p>{t('exceptions.detail.damageIntro', { vehicle: detail.vehicleLabel ?? '—', trip: detail.tripNumber })}</p>
          <FormField label={t('exceptions.detail.damageSeverity')} htmlFor="exc-damage-severity" required>
            <select id="exc-damage-severity" value={damageSeverity} onChange={(e) => setDamageSeverity(e.target.value as LinkedDamageSeverity)}>
              {DAMAGE_SEVERITIES.map((value) => (
                <option key={value} value={value}>{t(`incidents.fleet.damageSeverity.${value}`)}</option>
              ))}
            </select>
          </FormField>
        </Modal>
      )}

      {photoToDelete && (
        <ConfirmDialog
          title={t('exceptions.detail.deletePhotoTitle')}
          message={t('exceptions.detail.deletePhotoMessage')}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={() => {
            const target = photoToDelete
            setPhotoToDelete(null)
            void handleDeletePhoto(target)
          }}
          onCancel={() => setPhotoToDelete(null)}
        />
      )}

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
