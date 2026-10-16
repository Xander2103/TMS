import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  ORDER_DOCUMENT_ACCEPT,
  ORDER_DOCUMENT_TYPE_LABELS,
  ORDER_DOCUMENT_TYPES,
  uploadOrderDocumentFile,
  type OrderDocumentType,
} from '../../transport-orders/api/orderDocumentsApi'
import { IssuedTransportDocumentsBlock } from '../../transport-orders/components/IssuedTransportDocumentsBlock'
import {
  createDossierDocument,
  deleteDossierDocument,
  moveDossierDocument,
  setDossierDocumentCustomerVisible,
  type DossierDocument,
} from '../api/dossierDocumentsApi'
import { DossierDocumentTable } from './DossierDocumentTable'
import { fileProblem } from './documentFile'
import {
  DOSSIER_LEVEL,
  activityLevelId,
  isKnownLevel,
  levelOfDocument,
  orderLevelLabel,
  parseLevel,
  resolveInitialLevel,
  type DocumentLevelId,
  type DossierDocumentLevels,
} from './documentLevels'
import { useDossierDocuments } from './useDossierDocuments'
import './dossier-documents.css'

interface DossierDocumentsWorkspaceProps {
  dossierId: string
  levels: DossierDocumentLevels
  /** Order id to open on (`?opdracht=`); unknown ids land on the dossier level. */
  initialOrderId?: string | null
  /** Standalone activity id to open on (`?activiteit=`); an order id wins when both are given. */
  initialActivityId?: string | null
  /** `craneJobKind` per order id when the host already loaded the order (picks the default issued-document kind). */
  craneJobKinds?: Record<string, string | null | undefined>
  /** Fired with the full dossier list after every change, so the overview count stays honest. */
  onDocumentsChange?: (documents: DossierDocument[]) => void
  /** Fired after a transport document was issued, so the dossier summaries (overview, activity cells) refresh. */
  onTransportDocumentIssued?: () => void
}

/**
 * Documenten workspace (master sprint 2026-09-21, D6 / contract 4.2; activity scope 2026-10-08).
 * ONE list (the dossier endpoint, every scope) feeds every level. The level selector groups the
 * levels the way a planner thinks about the dossier — Dossier / Opdrachten / Activiteiten — with
 * the OWN document count per level. "Dossierdocumenten" shows the documents of the dossier as a
 * whole; an order or activity level shows its OWN documents plus the dossier documents read-only
 * (the same records, never copies), and an order level also its issued transport documents.
 * Scope and customer visibility are two different things: the level switch never publishes or
 * withdraws anything.
 *
 * Rights follow the scope exactly like the server's OrderDocumentAccessResolver: dossier and
 * activity documents need `dossiers.manage`, order documents the order rights. Hiding a button
 * is a courtesy; the API refuses on its own.
 */
export function DossierDocumentsWorkspace({ dossierId, levels, initialOrderId, initialActivityId, craneJobKinds, onDocumentsChange, onTransportDocumentIssued }: DossierDocumentsWorkspaceProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const { documents, loadFailed, reload } = useDossierDocuments(dossierId)

  const canManageDossier = hasPermission('dossiers.manage')
  const canUploadOrder = hasPermission('orders.create') || hasPermission('orders.edit') || hasPermission('orders.manage')
  const canWriteOrder = hasPermission('orders.edit') || hasPermission('orders.manage')

  const [level, setLevel] = useState<DocumentLevelId>(() =>
    resolveInitialLevel({ orderId: initialOrderId, activityId: initialActivityId }, levels),
  )
  // A level that disappears (order unlinked, activity removed) falls back to the dossier level.
  const activeLevel: DocumentLevelId = isKnownLevel(level, levels) ? level : DOSSIER_LEVEL
  const activeRef = parseLevel(activeLevel)
  const activeOrder = activeRef.kind === 'order' ? levels.orders.find((o) => o.orderId === activeRef.orderId)! : null
  const activeActivity = activeRef.kind === 'activity' ? levels.activities.find((a) => a.activityId === activeRef.activityId)! : null

  const [uploadOpen, setUploadOpen] = useState(false)
  /** A created document whose file upload failed: the retry dialog works on this record. */
  const [retryDocument, setRetryDocument] = useState<DossierDocument | null>(null)
  const [visibilityTarget, setVisibilityTarget] = useState<DossierDocument | null>(null)
  const [moveTarget, setMoveTarget] = useState<DossierDocument | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<DossierDocument | null>(null)
  const [busy, setBusy] = useState(false)

  const onChangeRef = useRef(onDocumentsChange)
  useEffect(() => {
    onChangeRef.current = onDocumentsChange
  })
  useEffect(() => {
    if (documents) onChangeRef.current?.(documents)
  }, [documents])

  const all = documents ?? []
  // Every list is a strict filter on the server's scope + link: a dossier document is never an
  // order's or activity's own document, and vice versa.
  const dossierDocuments = all.filter((d) => d.scope === 'Dossier')
  const countOf = (id: DocumentLevelId) => all.filter((d) => levelOfDocument(d) === id).length
  const ownDocuments = activeLevel === DOSSIER_LEVEL ? [] : all.filter((d) => levelOfDocument(d) === activeLevel)

  /** Write right of a scope, exactly like the server: dossier and activity documents follow the dossier. */
  const scopeWritable = (scope: DossierDocument['scope']) => (scope === 'Order' ? canWriteOrder : canManageDossier)
  const canWrite = (document: DossierDocument) => scopeWritable(document.scope)
  const canUploadHere = activeRef.kind === 'order' ? canManageDossier && canUploadOrder : canManageDossier
  // Moving needs the write right of BOTH the source and the target scope (server rule): offered
  // when at least one OTHER level the user may write to exists.
  const writableTargets = (current: DocumentLevelId): DocumentLevelId[] => [
    ...(canManageDossier ? [DOSSIER_LEVEL] : []),
    ...(canWriteOrder ? levels.orders.map((o) => o.orderId) : []),
    ...(canManageDossier ? levels.activities.map((a) => activityLevelId(a.activityId)) : []),
  ].filter((id) => id !== current)
  const canMove = (document: DossierDocument) => canWrite(document) && writableTargets(levelOfDocument(document)).length > 0

  async function changeVisibility(document: DossierDocument) {
    setBusy(true)
    try {
      const next = !document.customerVisible
      await setDossierDocumentCustomerVisible(document, next)
      showSuccess(next ? t('dossierDocuments.visibility.nowVisible') : t('dossierDocuments.visibility.nowInternal'))
      setVisibilityTarget(null)
      await reload()
    } catch (err) {
      showError(describeApiError(err, t('dossierDocuments.visibility.failed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function remove(document: DossierDocument) {
    setBusy(true)
    try {
      await deleteDossierDocument(document.id)
      showSuccess(t('dossierDocuments.delete.success'))
      setDeleteTarget(null)
      await reload()
    } catch (err) {
      showError(describeApiError(err, t('dossierDocuments.delete.failed')).message)
    } finally {
      setBusy(false)
    }
  }

  // Each list is homogeneous in scope, so the rights are decided per list; the handlers receive the row.
  const rowActions = (scope: DossierDocument['scope']) => {
    const probe = { scope } as DossierDocument
    return {
      onAttachFile: canWrite(probe) ? (document: DossierDocument) => setRetryDocument(document) : undefined,
      onChangeVisibility: canWrite(probe) ? (document: DossierDocument) => setVisibilityTarget(document) : undefined,
      onMove: canMove(probe) ? (document: DossierDocument) => setMoveTarget(document) : undefined,
      onDelete: canWrite(probe) ? (document: DossierDocument) => setDeleteTarget(document) : undefined,
    }
  }
  const dossierActions = rowActions('Dossier')
  const orderActions = rowActions('Order')
  const activityActions = rowActions('Activity')

  /** The read-only block of dossier documents shown on an order or activity level: the same records, never copies. */
  const sharedDossierDocuments = (
    <section className="ddoc-section ddoc-section-general" aria-label={t('dossierDocuments.sections.general')}>
      <h3>{t('dossierDocuments.sections.general')}</h3>
      <p className="ddoc-hint">
        {t('dossierDocuments.sections.generalHint')}{' '}
        <button type="button" className="link-button" onClick={() => setLevel(DOSSIER_LEVEL)}>
          {t('dossierDocuments.sections.manageViaDossier')}
        </button>
      </p>
      <DossierDocumentTable documents={dossierDocuments} emptyText={t('dossierDocuments.empty.general')} />
    </section>
  )

  // B48: an empty list offers the upload right there instead of a bare sentence.
  const uploadAction =
    canUploadHere && documents ? (
      <Button variant="secondary" onClick={() => setUploadOpen(true)} disabled={busy}>
        + {t('dossierDocuments.upload.open')}
      </Button>
    ) : undefined

  return (
    <div className="ddoc-workspace" data-level={activeLevel}>
      <div className="ddoc-layout">
        <DocumentLevelNav levels={levels} activeLevel={activeLevel} countOf={countOf} onSelect={setLevel} />

        <div className="ddoc-main">
          <div className="ddoc-toolbar">
            {documents && <span className="ddoc-summary">{t('dossierDocuments.summary', { count: documents.length })}</span>}
            {canUploadHere && documents && (
              <Button variant="secondary" onClick={() => setUploadOpen(true)} disabled={busy}>
                + {t('dossierDocuments.upload.open')}
              </Button>
            )}
          </div>

          {loadFailed && (
            <p className="ddoc-error" role="alert">
              {t('dossierDocuments.loadFailed')}{' '}
              <button type="button" className="link-button" onClick={() => void reload()}>{t('dossierDocuments.retry')}</button>
            </p>
          )}
          {!loadFailed && documents === null && <p className="placeholder-text">{t('dossierDocuments.loading')}</p>}

          {documents && activeRef.kind === 'dossier' && (
            <section className="ddoc-section" aria-label={t('dossierDocuments.sections.dossier')}>
              <h3>{t('dossierDocuments.sections.dossier')}</h3>
              <p className="ddoc-hint">{t('dossierDocuments.sections.dossierHint')}</p>
              <DossierDocumentTable documents={dossierDocuments} actions={dossierActions} emptyText={t('dossierDocuments.empty.dossier')} emptyAction={uploadAction} />
            </section>
          )}

          {documents && activeOrder && (
            <>
              <section className="ddoc-section" aria-label={t('dossierDocuments.sections.own')}>
                <h3>{t('dossierDocuments.sections.own')}</h3>
                <p className="ddoc-hint">{t('dossierDocuments.sections.ownHint', { number: activeOrder.orderNumber })}</p>
                <DossierDocumentTable documents={ownDocuments} actions={orderActions} emptyText={t('dossierDocuments.empty.order')} emptyAction={uploadAction} />
              </section>
              {sharedDossierDocuments}
              <IssuedTransportDocumentsBlock key={activeOrder.orderId} orderId={activeOrder.orderId} craneJobKind={craneJobKinds?.[activeOrder.orderId]} onIssued={onTransportDocumentIssued} />
            </>
          )}

          {documents && activeActivity && (
            <>
              <section className="ddoc-section" aria-label={t('dossierDocuments.sections.ownActivity')}>
                <h3>{t('dossierDocuments.sections.ownActivity')}</h3>
                <p className="ddoc-hint">{t('dossierDocuments.sections.ownActivityHint', { label: activeActivity.label })}</p>
                <DossierDocumentTable documents={ownDocuments} actions={activityActions} emptyText={t('dossierDocuments.empty.activity')} emptyAction={uploadAction} />
              </section>
              {sharedDossierDocuments}
            </>
          )}
        </div>
      </div>

      {uploadOpen && (
        <UploadDocumentDialog
          dossierId={dossierId}
          levels={levels}
          defaultLevel={activeLevel}
          allowOrderTarget={canUploadOrder}
          onClose={() => setUploadOpen(false)}
          onChanged={() => void reload()}
        />
      )}

      {retryDocument && (
        <RetryFileDialog
          document={retryDocument}
          onClose={() => setRetryDocument(null)}
          onChanged={() => void reload()}
        />
      )}

      {visibilityTarget && (
        <ConfirmDialog
          title={t('dossierDocuments.visibility.title')}
          message={visibilityTarget.customerVisible ? t('dossierDocuments.visibility.confirmInternal') : t('dossierDocuments.visibility.confirmVisible')}
          confirmLabel={visibilityTarget.customerVisible ? t('dossierDocuments.visibility.makeInternal') : t('dossierDocuments.visibility.makeVisible')}
          busy={busy}
          onConfirm={() => void changeVisibility(visibilityTarget)}
          onCancel={() => setVisibilityTarget(null)}
        />
      )}

      {moveTarget && (
        <MoveDocumentDialog
          document={moveTarget}
          levels={levels}
          targets={writableTargets(levelOfDocument(moveTarget))}
          onClose={() => setMoveTarget(null)}
          onMoved={() => {
            setMoveTarget(null)
            void reload()
          }}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('dossierDocuments.delete.title')}
          message={
            deleteTarget.scope === 'Dossier'
              ? t('dossierDocuments.delete.confirmDossier', { title: deleteTarget.title })
              : deleteTarget.scope === 'Activity'
                ? t('dossierDocuments.delete.confirmActivity', { title: deleteTarget.title, label: deleteTarget.activityLabel ?? '' })
                : t('dossierDocuments.delete.confirmOrder', { title: deleteTarget.title, number: deleteTarget.orderNumber ?? '' })
          }
          confirmLabel={t('dossierDocuments.delete.confirm')}
          destructive
          busy={busy}
          onConfirm={() => void remove(deleteTarget)}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}

// ------------------------------------------------------------------ level selector

interface DocumentLevelNavProps {
  levels: DossierDocumentLevels
  activeLevel: DocumentLevelId
  countOf: (id: DocumentLevelId) => number
  onSelect: (id: DocumentLevelId) => void
}

/**
 * The level selector: three labelled groups (Dossier / Opdrachten / Activiteiten) of selectable
 * levels, each with its OWN document count. A vertical rail beside the documents on desktop, stacked
 * groups on narrow screens — never one unexplained row of pills. ONE tablist, so arrow keys move
 * through every level; the active one is marked by `aria-selected`, a ✓ and its styling.
 */
function DocumentLevelNav({ levels, activeLevel, countOf, onSelect }: DocumentLevelNavProps) {
  const { t } = useLocale()

  const item = (id: DocumentLevelId, title: string, subtitle: string) => {
    const selected = id === activeLevel
    return (
      <button
        key={id}
        type="button"
        role="tab"
        aria-selected={selected}
        tabIndex={selected ? 0 : -1}
        className={`ddoc-level${selected ? ' is-active' : ''}`}
        onClick={() => onSelect(id)}
      >
        <span className="ddoc-level-text">
          <span className="ddoc-level-title">{title}</span>
          <span className="ddoc-level-subtitle">{subtitle}</span>
        </span>
        <span className="ddoc-level-count">{countOf(id)}</span>
      </button>
    )
  }

  const group = (key: string, label: string, children: ReactNode) => (
    <div key={key} className="ddoc-level-group" role="presentation">
      <span className="ddoc-level-group-label">{label}</span>
      {children}
    </div>
  )

  return (
    <nav className="ddoc-nav" aria-label={t('dossierDocuments.selector')}>
      <div
        role="tablist"
        aria-orientation="vertical"
        aria-label={t('dossierDocuments.selector')}
        className="ddoc-levels"
        onKeyDown={(event) => {
          const ids = [DOSSIER_LEVEL, ...levels.orders.map((o) => o.orderId), ...levels.activities.map((a) => activityLevelId(a.activityId))]
          const index = ids.indexOf(activeLevel)
          const next = event.key === 'ArrowDown' || event.key === 'ArrowRight' ? index + 1
            : event.key === 'ArrowUp' || event.key === 'ArrowLeft' ? index - 1
            : event.key === 'Home' ? 0 : event.key === 'End' ? ids.length - 1 : null
          if (next === null) return
          event.preventDefault()
          const target = ids[(next + ids.length) % ids.length]
          onSelect(target)
          ;(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]')[(next + ids.length) % ids.length])?.focus()
        }}
      >
        {group('dossier', t('dossierDocuments.groups.dossier'), item(DOSSIER_LEVEL, t('dossierDocuments.levels.dossier'), t('dossierDocuments.levels.dossierHint')))}
        {levels.orders.length > 0 && group('orders', t('dossierDocuments.groups.orders'),
          levels.orders.map((order) => item(order.orderId, orderLevelLabel(order), t('dossierDocuments.kinds.order'))))}
        {levels.activities.length > 0 && group('activities', t('dossierDocuments.groups.activities'),
          levels.activities.map((activity) => item(activityLevelId(activity.activityId), activity.label, t('dossierDocuments.kinds.activity'))))}
      </div>
    </nav>
  )
}

// ------------------------------------------------------------------ upload

interface UploadDocumentDialogProps {
  dossierId: string
  levels: DossierDocumentLevels
  defaultLevel: DocumentLevelId
  /** The user may also target an order (order upload right). */
  allowOrderTarget: boolean
  onClose: () => void
  onChanged: () => void
}

/**
 * Two steps behind one dialog: the metadata (contract 4.2) and then the file through the
 * existing order-documents upload route. When the file fails after the record was created, the
 * dialog stays open in retry mode — the record is real and listed, so the planner decides: try
 * another file, close without a file, or delete the record again.
 */
function UploadDocumentDialog({ dossierId, levels, defaultLevel, allowOrderTarget, onClose, onChanged }: UploadDocumentDialogProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  // Context-sensitive default: the level the planner is looking at, unless it is an order and
  // the order upload right is missing.
  const [target, setTarget] = useState<DocumentLevelId>(
    parseLevel(defaultLevel).kind === 'order' && !allowOrderTarget ? DOSSIER_LEVEL : defaultLevel,
  )
  // A general document is rarely a CMR: start neutral and let the planner pick.
  const [type, setType] = useState<OrderDocumentType>('Other')
  const [customTypeName, setCustomTypeName] = useState('')
  const [title, setTitle] = useState('')
  const [titleTouched, setTitleTouched] = useState(false)
  const [customerVisible, setCustomerVisible] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  /** Set once the metadata is stored but the file is not: the dialog switches to retry mode. */
  const [created, setCreated] = useState<DossierDocument | null>(null)

  function pickFile(next: File | null) {
    setFile(next)
    setError(null)
    if (next && !titleTouched) setTitle(next.name.replace(/\.[^.]+$/, ''))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    const trimmedTitle = title.trim()
    if (!trimmedTitle) {
      setError(t('dossierDocuments.upload.titleRequired'))
      return
    }
    if (!file) {
      setError(t('dossierDocuments.upload.fileRequired'))
      return
    }
    const problem = fileProblem(file)
    if (problem) {
      setError(t(`dossierDocuments.upload.fileProblem.${problem}`))
      return
    }
    setBusy(true)
    setError(null)
    let record = created
    try {
      if (!record) {
        const targetRef = parseLevel(target)
        record = await createDossierDocument(dossierId, {
          transportOrderId: targetRef.kind === 'order' ? targetRef.orderId : null,
          dossierActivityId: targetRef.kind === 'activity' ? targetRef.activityId : null,
          documentType: type,
          customTypeName: type === 'Other' && customTypeName.trim() ? customTypeName.trim() : null,
          title: trimmedTitle,
          issueDate: null,
          notes: null,
          customerVisible,
        })
        setCreated(record)
        onChanged()
      }
    } catch (err) {
      setError(describeApiError(err, t('dossierDocuments.upload.createFailed')).message)
      setBusy(false)
      return
    }
    try {
      await uploadOrderDocumentFile(record.id, file)
      showSuccess(t('dossierDocuments.upload.success'))
      onChanged()
      onClose()
    } catch (err) {
      setError(t('dossierDocuments.upload.fileFailed', { reason: describeApiError(err, t('dossierDocuments.upload.fileFailedFallback')).message }))
    } finally {
      setBusy(false)
    }
  }

  async function removeCreated() {
    if (!created || busy) return
    setBusy(true)
    try {
      await deleteDossierDocument(created.id)
      showSuccess(t('dossierDocuments.upload.removed'))
      onChanged()
      onClose()
    } catch (err) {
      setError(describeApiError(err, t('dossierDocuments.delete.failed')).message)
    } finally {
      setBusy(false)
    }
  }

  const retryMode = created !== null

  return (
    <Modal
      title={t('dossierDocuments.upload.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          {retryMode ? (
            <>
              <Button variant="secondary" onClick={onClose} disabled={busy}>{t('dossierDocuments.upload.closeWithoutFile')}</Button>
              <Button variant="danger" onClick={() => void removeCreated()} disabled={busy}>{t('dossierDocuments.upload.removeCreated')}</Button>
            </>
          ) : (
            <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          )}
          <Button type="submit" form="ddoc-upload-form" disabled={busy}>
            {busy ? t('ui.actions.busy') : retryMode ? t('dossierDocuments.upload.retry') : t('dossierDocuments.upload.submit')}
          </Button>
        </>
      }
    >
      <form id="ddoc-upload-form" onSubmit={(event) => void submit(event)}>
        <ValidationSummary message={error} />
        {retryMode && <p className="ddoc-hint">{t('dossierDocuments.upload.retryHint')}</p>}
        {!retryMode && (
          <>
            <FormField label={t('dossierDocuments.upload.target')} htmlFor="ddoc-upload-target" hint={t('dossierDocuments.upload.targetHint')}>
              <select id="ddoc-upload-target" value={target} onChange={(event) => setTarget(event.target.value)} disabled={busy}>
                <optgroup label={t('dossierDocuments.groups.dossier')}>
                  <option value={DOSSIER_LEVEL}>{t('dossierDocuments.upload.targetDossier')}</option>
                </optgroup>
                {allowOrderTarget && levels.orders.length > 0 && (
                  <optgroup label={t('dossierDocuments.groups.orders')}>
                    {levels.orders.map((order) => (
                      <option key={order.orderId} value={order.orderId}>
                        {t('dossierDocuments.upload.targetOrder', { label: orderLevelLabel(order) })}
                      </option>
                    ))}
                  </optgroup>
                )}
                {levels.activities.length > 0 && (
                  <optgroup label={t('dossierDocuments.groups.activities')}>
                    {levels.activities.map((activity) => (
                      <option key={activity.activityId} value={activityLevelId(activity.activityId)}>
                        {t('dossierDocuments.upload.targetActivity', { label: activity.label })}
                      </option>
                    ))}
                  </optgroup>
                )}
              </select>
            </FormField>
            <FormField label={t('dossierDocuments.upload.type')} htmlFor="ddoc-upload-type">
              <select id="ddoc-upload-type" value={type} onChange={(event) => setType(event.target.value as OrderDocumentType)} disabled={busy}>
                {ORDER_DOCUMENT_TYPES.map((option) => (
                  <option key={option} value={option}>{t(ORDER_DOCUMENT_TYPE_LABELS[option])}</option>
                ))}
              </select>
            </FormField>
            {type === 'Other' && (
              <FormField label={t('dossierDocuments.upload.customType')} htmlFor="ddoc-upload-custom-type">
                <input id="ddoc-upload-custom-type" value={customTypeName} onChange={(event) => setCustomTypeName(event.target.value)} maxLength={100} disabled={busy} />
              </FormField>
            )}
            <FormField label={t('dossierDocuments.upload.documentTitle')} htmlFor="ddoc-upload-title" required>
              <input
                id="ddoc-upload-title"
                value={title}
                onChange={(event) => {
                  setTitleTouched(true)
                  setTitle(event.target.value)
                }}
                maxLength={200}
                disabled={busy}
              />
            </FormField>
          </>
        )}
        <FormField label={t('dossierDocuments.upload.file')} htmlFor="ddoc-upload-file" required hint={t('dossierDocuments.upload.fileHint')}>
          <input id="ddoc-upload-file" type="file" accept={ORDER_DOCUMENT_ACCEPT} onChange={(event) => pickFile(event.target.files?.[0] ?? null)} disabled={busy} />
        </FormField>
        {!retryMode && (
          <label className="ddoc-checkbox">
            <input type="checkbox" checked={customerVisible} onChange={(event) => setCustomerVisible(event.target.checked)} disabled={busy} />
            {' '}{t('dossierDocuments.upload.customerVisible')}
            <span className="ddoc-hint">{t('dossierDocuments.upload.customerVisibleHint')}</span>
          </label>
        )}
      </form>
    </Modal>
  )
}

// ------------------------------------------------------------------ retry file

function RetryFileDialog({ document, onClose, onChanged }: { document: DossierDocument; onClose: () => void; onChanged: () => void }) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    if (!file) {
      setError(t('dossierDocuments.upload.fileRequired'))
      return
    }
    const problem = fileProblem(file)
    if (problem) {
      setError(t(`dossierDocuments.upload.fileProblem.${problem}`))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await uploadOrderDocumentFile(document.id, file)
      showSuccess(t('dossierDocuments.upload.success'))
      onChanged()
      onClose()
    } catch (err) {
      setError(t('dossierDocuments.upload.fileFailed', { reason: describeApiError(err, t('dossierDocuments.upload.fileFailedFallback')).message }))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`${t('dossierDocuments.upload.retry')} — ${document.title}`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button type="submit" form="ddoc-retry-form" disabled={busy}>{busy ? t('ui.actions.busy') : t('dossierDocuments.upload.retry')}</Button>
        </>
      }
    >
      <form id="ddoc-retry-form" onSubmit={(event) => void submit(event)}>
        <ValidationSummary message={error} />
        <p className="ddoc-hint">{t('dossierDocuments.upload.retryHint')}</p>
        <FormField label={t('dossierDocuments.upload.file')} htmlFor="ddoc-retry-file" required hint={t('dossierDocuments.upload.fileHint')}>
          <input id="ddoc-retry-file" type="file" accept={ORDER_DOCUMENT_ACCEPT} onChange={(event) => setFile(event.target.files?.[0] ?? null)} disabled={busy} />
        </FormField>
      </form>
    </Modal>
  )
}

// ------------------------------------------------------------------ move

interface MoveDocumentDialogProps {
  document: DossierDocument
  levels: DossierDocumentLevels
  /** The levels the user may move the document to (write right on the target scope); the current level is listed read-only. */
  targets: DocumentLevelId[]
  onClose: () => void
  onMoved: () => void
}

/** Changes the LEVEL inside the same dossier. The file and the customer visibility stay as they are. */
function MoveDocumentDialog({ document, levels, targets, onClose, onMoved }: MoveDocumentDialogProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const currentLevel = levelOfDocument(document)
  const [target, setTarget] = useState<DocumentLevelId>(currentLevel)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const labelOf = (id: DocumentLevelId): string => {
    const ref = parseLevel(id)
    if (ref.kind === 'dossier') return t('dossierDocuments.levels.dossier')
    if (ref.kind === 'order') return orderLevelLabel(levels.orders.find((o) => o.orderId === ref.orderId)!)
    return levels.activities.find((a) => a.activityId === ref.activityId)!.label
  }

  async function submit() {
    if (busy || target === currentLevel) return
    setBusy(true)
    setError(null)
    try {
      const ref = parseLevel(target)
      await moveDossierDocument(document.id, {
        targetTransportOrderId: ref.kind === 'order' ? ref.orderId : null,
        targetDossierActivityId: ref.kind === 'activity' ? ref.activityId : null,
      })
      showSuccess(t('dossierDocuments.move.success', { level: labelOf(target) }))
      onMoved()
    } catch (err) {
      setError(describeApiError(err, t('dossierDocuments.move.failed')).message)
    } finally {
      setBusy(false)
    }
  }

  const selectable = new Set(targets)
  const option = (id: DocumentLevelId, label: string) => {
    if (id !== currentLevel && !selectable.has(id)) return null
    return (
      <label key={id} className="ddoc-move-option">
        <input type="radio" name="ddoc-move-target" value={id} checked={target === id} onChange={() => setTarget(id)} disabled={busy || id === currentLevel} />
        {' '}{label}
        {id === currentLevel && <span className="ddoc-meta"> ({t('dossierDocuments.move.current')})</span>}
      </label>
    )
  }
  const groupOptions = (label: string, options: (ReactNode | null)[]) => {
    const rendered = options.filter(Boolean)
    if (rendered.length === 0) return null
    return (
      <div className="ddoc-move-group" key={label}>
        <span className="ddoc-level-group-label">{label}</span>
        {rendered}
      </div>
    )
  }

  return (
    <Modal
      title={t('dossierDocuments.move.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button onClick={() => void submit()} disabled={busy || target === currentLevel}>
            {busy ? t('ui.actions.busy') : t('dossierDocuments.move.confirm')}
          </Button>
        </>
      }
    >
      <ValidationSummary message={error} />
      <p>{t('dossierDocuments.move.intro', { title: document.title })}</p>
      <fieldset className="ddoc-move-options">
        <legend>{t('dossierDocuments.move.legend')}</legend>
        {groupOptions(t('dossierDocuments.groups.dossier'), [option(DOSSIER_LEVEL, t('dossierDocuments.levels.dossier'))])}
        {groupOptions(t('dossierDocuments.groups.orders'), levels.orders.map((order) => option(order.orderId, orderLevelLabel(order))))}
        {groupOptions(t('dossierDocuments.groups.activities'), levels.activities.map((activity) => option(activityLevelId(activity.activityId), activity.label)))}
      </fieldset>
      <p className="ddoc-hint">{t('dossierDocuments.move.explanation')}</p>
      <p className="ddoc-hint">{t('dossierDocuments.visibility.independent')}</p>
    </Modal>
  )
}
