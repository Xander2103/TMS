import { useEffect, useRef, useState, type FormEvent } from 'react'
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
import { DOSSIER_LEVEL, orderLevelLabel, resolveInitialLevel, type DocumentLevelId, type DossierDocumentOrderLevel } from './documentLevels'
import { useDossierDocuments } from './useDossierDocuments'
import './dossier-documents.css'

interface DossierDocumentsWorkspaceProps {
  dossierId: string
  orderLevels: DossierDocumentOrderLevel[]
  /** Order id to open on (`?opdracht=`); anything else lands on the dossier level. */
  initialLevel?: string | null
  /** `craneJobKind` per order id when the host already loaded the order (picks the default issued-document kind). */
  craneJobKinds?: Record<string, string | null | undefined>
  /** Fired with the full dossier list after every change, so the overview count stays honest. */
  onDocumentsChange?: (documents: DossierDocument[]) => void
}

/**
 * Documenten workspace (master sprint 2026-09-21, D6 / contract 4.2). ONE list (the dossier
 * endpoint, both scopes) feeds every level: "Dossier (algemeen)" shows the dossier documents,
 * an order level its OWN documents plus the general ones read-only (the same records, never
 * copies) and the issued transport documents of that order. Scope and customer visibility are
 * two different things: the level switch never publishes or withdraws anything.
 *
 * Rights follow the scope exactly like the server's OrderDocumentAccessResolver: dossier
 * documents need `dossiers.manage`, order documents the order rights. Hiding a button is a
 * courtesy; the API refuses on its own.
 */
export function DossierDocumentsWorkspace({ dossierId, orderLevels, initialLevel, craneJobKinds, onDocumentsChange }: DossierDocumentsWorkspaceProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const { documents, loadFailed, reload } = useDossierDocuments(dossierId)

  const canManageDossier = hasPermission('dossiers.manage')
  const canUploadOrder = hasPermission('orders.create') || hasPermission('orders.edit') || hasPermission('orders.manage')
  const canWriteOrder = hasPermission('orders.edit') || hasPermission('orders.manage')

  const [level, setLevel] = useState<DocumentLevelId>(() => resolveInitialLevel(initialLevel, orderLevels))
  // A level that disappears (order unlinked) falls back to the dossier level.
  const levelKnown = level === DOSSIER_LEVEL || orderLevels.some((o) => o.orderId === level)
  const activeLevel: DocumentLevelId = levelKnown ? level : DOSSIER_LEVEL
  const activeOrder = activeLevel === DOSSIER_LEVEL ? null : orderLevels.find((o) => o.orderId === activeLevel)!

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
  const dossierDocuments = all.filter((d) => d.scope === 'Dossier')
  const ownDocuments = activeOrder ? all.filter((d) => d.transportOrderId === activeOrder.orderId) : []

  /** Write right for the documents of the level a document hangs on. */
  const canWrite = (document: DossierDocument) => (document.scope === 'Dossier' ? canManageDossier : canWriteOrder)
  const canUploadHere = activeLevel === DOSSIER_LEVEL ? canManageDossier : canManageDossier && canUploadOrder
  // Moving needs the write right of BOTH scopes (server rule) and more than one level to choose from.
  const canMove = (document: DossierDocument) => orderLevels.length > 0 && canManageDossier && canWriteOrder && canWrite(document)

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

  return (
    <div className="ddoc-workspace" data-level={activeLevel}>
      <div className="ddoc-toolbar">
        <div className="ddoc-levels" role="tablist" aria-label={t('dossierDocuments.columns.level')}>
          <button
            type="button"
            role="tab"
            aria-selected={activeLevel === DOSSIER_LEVEL}
            className={`ddoc-level${activeLevel === DOSSIER_LEVEL ? ' is-active' : ''}`}
            onClick={() => setLevel(DOSSIER_LEVEL)}
          >
            {t('dossierDocuments.levels.dossier')}
            <span className="ddoc-level-count">{dossierDocuments.length}</span>
          </button>
          {orderLevels.map((order) => {
            const count = all.filter((d) => d.transportOrderId === order.orderId).length
            return (
              <button
                key={order.orderId}
                type="button"
                role="tab"
                aria-selected={activeLevel === order.orderId}
                className={`ddoc-level${activeLevel === order.orderId ? ' is-active' : ''}`}
                onClick={() => setLevel(order.orderId)}
              >
                {orderLevelLabel(order)}
                <span className="ddoc-level-count">{count}</span>
              </button>
            )
          })}
        </div>
        <div className="ddoc-toolbar-actions">
          {documents && <span className="ddoc-summary">{t('dossierDocuments.summary', { count: documents.length })}</span>}
          {canUploadHere && documents && (
            <Button variant="secondary" onClick={() => setUploadOpen(true)} disabled={busy}>
              + {t('dossierDocuments.upload.open')}
            </Button>
          )}
        </div>
      </div>

      {loadFailed && (
        <p className="ddoc-error" role="alert">
          {t('dossierDocuments.loadFailed')}{' '}
          <button type="button" className="link-button" onClick={() => void reload()}>{t('dossierDocuments.retry')}</button>
        </p>
      )}
      {!loadFailed && documents === null && <p className="placeholder-text">{t('dossierDocuments.loading')}</p>}

      {documents && activeLevel === DOSSIER_LEVEL && (
        <section className="ddoc-section" aria-label={t('dossierDocuments.sections.dossier')}>
          <h3>{t('dossierDocuments.sections.dossier')}</h3>
          <p className="ddoc-hint">{t('dossierDocuments.sections.dossierHint')}</p>
          <DossierDocumentTable documents={dossierDocuments} actions={dossierActions} emptyText={t('dossierDocuments.empty.dossier')} />
        </section>
      )}

      {documents && activeOrder && (
        <>
          <section className="ddoc-section" aria-label={t('dossierDocuments.sections.own')}>
            <h3>{t('dossierDocuments.sections.own')}</h3>
            <p className="ddoc-hint">{t('dossierDocuments.sections.ownHint', { number: activeOrder.orderNumber })}</p>
            <DossierDocumentTable documents={ownDocuments} actions={orderActions} emptyText={t('dossierDocuments.empty.order')} />
          </section>
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
          <IssuedTransportDocumentsBlock key={activeOrder.orderId} orderId={activeOrder.orderId} craneJobKind={craneJobKinds?.[activeOrder.orderId]} />
        </>
      )}

      {uploadOpen && (
        <UploadDocumentDialog
          dossierId={dossierId}
          orderLevels={orderLevels}
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
          orderLevels={orderLevels}
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

// ------------------------------------------------------------------ upload

interface UploadDocumentDialogProps {
  dossierId: string
  orderLevels: DossierDocumentOrderLevel[]
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
function UploadDocumentDialog({ dossierId, orderLevels, defaultLevel, allowOrderTarget, onClose, onChanged }: UploadDocumentDialogProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const [target, setTarget] = useState<DocumentLevelId>(allowOrderTarget ? defaultLevel : DOSSIER_LEVEL)
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
        record = await createDossierDocument(dossierId, {
          transportOrderId: target === DOSSIER_LEVEL ? null : target,
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
              <select id="ddoc-upload-target" value={target} onChange={(event) => setTarget(event.target.value)} disabled={busy || !allowOrderTarget}>
                <option value={DOSSIER_LEVEL}>{t('dossierDocuments.upload.targetDossier')}</option>
                {allowOrderTarget && orderLevels.map((order) => (
                  <option key={order.orderId} value={order.orderId}>
                    {t('dossierDocuments.upload.targetOrder', { label: orderLevelLabel(order) })}
                  </option>
                ))}
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
  orderLevels: DossierDocumentOrderLevel[]
  onClose: () => void
  onMoved: () => void
}

/** Changes the LEVEL inside the same dossier. The file and the customer visibility stay as they are. */
function MoveDocumentDialog({ document, orderLevels, onClose, onMoved }: MoveDocumentDialogProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const currentLevel: DocumentLevelId = document.transportOrderId ?? DOSSIER_LEVEL
  const [target, setTarget] = useState<DocumentLevelId>(currentLevel)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit() {
    if (busy || target === currentLevel) return
    setBusy(true)
    setError(null)
    try {
      await moveDossierDocument(document.id, target === DOSSIER_LEVEL ? null : target)
      const levelLabel = target === DOSSIER_LEVEL
        ? t('dossierDocuments.levels.dossier')
        : orderLevelLabel(orderLevels.find((o) => o.orderId === target)!)
      showSuccess(t('dossierDocuments.move.success', { level: levelLabel }))
      onMoved()
    } catch (err) {
      setError(describeApiError(err, t('dossierDocuments.move.failed')).message)
    } finally {
      setBusy(false)
    }
  }

  const option = (id: DocumentLevelId, label: string) => (
    <label key={id} className="ddoc-move-option">
      <input type="radio" name="ddoc-move-target" value={id} checked={target === id} onChange={() => setTarget(id)} disabled={busy} />
      {' '}{label}
      {id === currentLevel && <span className="ddoc-meta"> ({t('dossierDocuments.move.current')})</span>}
    </label>
  )

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
        {option(DOSSIER_LEVEL, t('dossierDocuments.levels.dossier'))}
        {orderLevels.map((order) => option(order.orderId, orderLevelLabel(order)))}
      </fieldset>
      <p className="ddoc-hint">{t('dossierDocuments.move.explanation')}</p>
      <p className="ddoc-hint">{t('dossierDocuments.visibility.independent')}</p>
    </Modal>
  )
}
