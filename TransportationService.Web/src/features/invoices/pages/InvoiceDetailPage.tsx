import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, localizeApiError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { changeInvoiceStatus, completeInvoiceLedgerSnapshots, createCreditNote, deleteInvoice, fetchInvoicePdfUrl, getInvoice, overrideInvoiceNumber, updateInvoice } from '../api/invoicesApi'
import { formatQuantity } from '../../../utils/numbers'
import { fiscalTreatmentLabel } from '../utils/invoiceFiscal'
import {
  deleteInvoiceAttachment,
  downloadInvoiceAttachment,
  listInvoiceAttachments,
  updateInvoiceAttachment,
  uploadInvoiceAttachment,
  INVOICE_ATTACHMENT_ACCEPT,
  MAX_INVOICE_ATTACHMENT_BYTES,
  type InvoiceAttachment,
} from '../api/invoiceAttachmentsApi'
import { formatFileSize } from '../utils/fileSize'
import { InvoicePeppolPanel } from '../components/InvoicePeppolPanel'
import { listSalesCategories, type SalesCategory } from '../../accounting/api/accountingApi'
import {
  euro,
  INVOICE_STATUS_LABELS,
  INVOICE_STATUS_TONE,
  INVOICE_TRANSITION_LABELS,
  type InvoiceDetail,
  type InvoiceStatus,
  type UpdateLineInput,
} from '../types'
import { formatPeriod, monthInputToPeriod, periodToMonthInput } from '../utils/invoicePeriod'
import { formatDate } from '../../../utils/dates'
import { InvoiceFiscalSummary, InvoiceLineFiscalBadge } from '../components/InvoiceFiscalSummary'
import './invoices.css'

interface EditableLine extends UpdateLineInput {
  key: string
  orderNumber: string | null
}

let lineKey = 0

export function InvoiceDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()

  const [invoice, setInvoice] = useState<InvoiceDetail | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [editing, setEditing] = useState(false)
  const [invoiceDate, setInvoiceDate] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [periodInput, setPeriodInput] = useState('')
  const [notes, setNotes] = useState('')
  const [poNumber, setPoNumber] = useState('')
  const [lines, setLines] = useState<EditableLine[]>([])

  const [confirmTransition, setConfirmTransition] = useState<InvoiceStatus | null>(null)
  // Send is irreversible (number, snapshots, descriptions freeze): it always goes through an
  // explicit summary dialog, and a single in-flight guard makes a double click a no-op.
  const [confirmSend, setConfirmSend] = useState(false)
  const sendInFlight = useRef(false)
  const [pdfBusy, setPdfBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  // Active sales categories for the per-line select in edit mode; empty when the caller
  // lacks the accounting/invoice read permissions — the select simply doesn't render then.
  const [salesCategories, setSalesCategories] = useState<SalesCategory[]>([])

  const [overrideOpen, setOverrideOpen] = useState(false)
  const [overrideNumber, setOverrideNumber] = useState('')
  const [overrideReason, setOverrideReason] = useState('')
  const [overrideError, setOverrideError] = useState<string | null>(null)
  const [overrideFieldErrors, setOverrideFieldErrors] = useState<FieldErrors>({})

  useEffect(() => {
    let mounted = true
    getInvoice(id)
      .then((data) => {
        if (!mounted) return
        setInvoice(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('invoices.detail.loadError')
      })
    return () => {
      mounted = false
    }
  }, [id])

  function startEditing() {
    if (!invoice) return
    setInvoiceDate(invoice.invoiceDate)
    setDueDate(invoice.dueDate)
    setPeriodInput(periodToMonthInput(invoice.invoicePeriodYear, invoice.invoicePeriodMonth))
    setNotes(invoice.notes ?? '')
    setPoNumber(invoice.purchaseOrderNumber ?? '')
    setLines(
      invoice.lines.map((l) => ({
        key: `l-${++lineKey}`,
        id: l.id,
        orderNumber: l.orderNumber,
        description: l.description,
        quantity: l.quantity,
        unitPrice: l.unitPrice,
        vatRatePercent: l.vatRatePercent,
        salesCategoryId: l.salesCategoryId,
      })),
    )
    listSalesCategories()
      .then(setSalesCategories)
      .catch(() => setSalesCategories([]))
    setEditing(true)
  }

  function setLine(key: string, patch: Partial<EditableLine>) {
    setLines((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  async function handleSave() {
    if (!invoice) return
    if (lines.length === 0) {
      showError(t('invoices.internalDetail.needsLine'))
      return
    }
    const period = monthInputToPeriod(periodInput)
    setBusy(true)
    try {
      const updated = await updateInvoice(invoice.id, {
        invoiceDate,
        dueDate,
        invoicePeriodYear: period?.year ?? null,
        invoicePeriodMonth: period?.month ?? null,
        notes: notes.trim() || null,
        purchaseOrderNumber: poNumber.trim() || null,
        lines: lines.map((line) => ({
          id: line.id,
          description: line.description,
          quantity: line.quantity,
          unitPrice: line.unitPrice,
          vatRatePercent: line.vatRatePercent,
          salesCategoryId: line.salesCategoryId ?? null,
        })),
      })
      setInvoice(updated)
      setEditing(false)
      showSuccess(t('invoices.internalDetail.updated'))
    } catch {
      showError(t('invoices.internalDetail.saveError'))
    } finally {
      setBusy(false)
    }
  }

  async function handleSend() {
    if (!invoice || sendInFlight.current) return
    sendInFlight.current = true
    try {
      await applyTransition('Sent')
    } finally {
      sendInFlight.current = false
      setConfirmSend(false)
    }
  }

  async function openPdfPreview() {
    if (!invoice || pdfBusy) return
    setPdfBusy(true)
    try {
      const url = await fetchInvoicePdfUrl(invoice.id)
      window.open(url, '_blank', 'noopener')
      // Give the new tab time to take the blob before it is released.
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.internalDetail.pdfError')))
    } finally {
      setPdfBusy(false)
    }
  }

  async function applyTransition(target: InvoiceStatus) {
    if (!invoice) return
    setBusy(true)
    try {
      const updated = await changeInvoiceStatus(invoice.id, target)
      setInvoice(updated)
      showSuccess(t('invoices.internalDetail.statusChanged', { status: t(INVOICE_STATUS_LABELS[target]) }))
    } catch {
      showError(t('invoices.internalDetail.statusError'))
    } finally {
      setBusy(false)
      setConfirmTransition(null)
    }
  }

  function openOverride() {
    if (!invoice) return
    setOverrideNumber(invoice.invoiceNumber)
    setOverrideReason('')
    setOverrideError(null)
    setOverrideFieldErrors({})
    setOverrideOpen(true)
  }

  async function handleOverride() {
    if (!invoice) return
    const number = overrideNumber.trim()
    const reason = overrideReason.trim()
    if (!number || !reason) {
      setOverrideError(t('invoices.override.missingFields'))
      return
    }
    setBusy(true)
    try {
      const updated = await overrideInvoiceNumber(invoice.id, { invoiceNumber: number, reason })
      setInvoice(updated)
      setOverrideOpen(false)
      showSuccess(t('invoices.override.success', { number: updated.invoiceNumber }))
    } catch (err) {
      const { fieldErrors } = describeApiError(err, '')
      setOverrideError(localizeApiError(t, err, t('invoices.override.error')))
      setOverrideFieldErrors(fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!invoice) return
    try {
      await deleteInvoice(invoice.id)
      showSuccess(t('invoices.internalDetail.deleted'))
      navigate('/invoices')
    } catch {
      showError(t('invoices.internalDetail.deleteError'))
      setConfirmDelete(false)
    }
  }

  if (loadErrorKey) return <ErrorState message={t(loadErrorKey)} />
  if (!invoice) return <LoadingState message={t('invoices.detail.loading')} />

  const editable = invoice.status === 'Draft' && hasPermission('invoices.edit')
  const deletable = (invoice.status === 'Draft' || invoice.status === 'Cancelled') && hasPermission('invoices.delete')
  const canOverrideNumber = invoice.status === 'Draft' && hasPermission('invoices.override_number')
  // H-06: een definitief document (verzonden of betaald) wordt nooit geannuleerd — corrigeren
  // gebeurt met 'Creditnota maken' hieronder. De server weigert het hoe dan ook; deze filter
  // zorgt dat de knop ook niet meer verschijnt bij een oudere/gecachete allowedTransitions.
  const finalized = invoice.status === 'Sent' || invoice.status === 'Paid'
  const offeredTransitions = invoice.allowedTransitions.filter(
    (target) => !(finalized && target === 'Cancelled'),
  )

  return (
    <div>
      <Breadcrumbs items={[{ label: t('invoices.list.title'), to: '/invoices' }, { label: invoice.invoiceNumber }]} />
      <PageHeader
        title={`${invoice.invoiceNumber} — ${invoice.customerName}`}
        subtitle={`${t('invoices.internalDetail.subtitle', { date: formatDate(invoice.invoiceDate), dueDate: formatDate(invoice.dueDate) })}${invoice.customerVatNumber ? ` · ${invoice.customerVatNumber}` : ''}`}
        action={
          <span className="inv-header-actions">
            <Badge tone={INVOICE_STATUS_TONE[invoice.status]}>{t(INVOICE_STATUS_LABELS[invoice.status])}</Badge>
            {invoice.numberIsManual && <Badge tone="warning">{t('invoices.internalDetail.manualNumber')}</Badge>}
            {!editing && (
              <Button
                variant="secondary"
                onClick={() => void openPdfPreview()}
                disabled={busy || pdfBusy}
                title={t('invoices.internalDetail.pdfPreviewHint')}
              >
                {pdfBusy ? t('invoices.common.busy') : t('invoices.internalDetail.pdfPreview')}
              </Button>
            )}
            {hasPermission('invoices.change_status') &&
              !editing &&
              offeredTransitions.map((target) => (
                <Button
                  key={target}
                  variant={target === 'Cancelled' ? 'secondary' : 'primary'}
                  onClick={() =>
                    target === 'Cancelled'
                      ? setConfirmTransition(target)
                      : target === 'Sent'
                        ? setConfirmSend(true)
                        : void applyTransition(target)
                  }
                  disabled={busy}
                >
                  {t(INVOICE_TRANSITION_LABELS[target])}
                </Button>
              ))}
          </span>
        }
      />

      <p className="inv-meta">
        {invoice.legalEntityName && <>{t('invoices.fields.billingEntity')}: {invoice.legalEntityName} · </>}
        {t('invoices.internalDetail.periodLabel')}: {formatPeriod(invoice.invoicePeriodYear, invoice.invoicePeriodMonth)}
        {invoice.purchaseOrderNumber && <> · {t('invoices.detail.poNumber', { number: invoice.purchaseOrderNumber })}</>}
      </p>
      <InvoiceFiscalSummary invoice={invoice} />
      {(invoice.creditedInvoiceId || (invoice.creditNotes && invoice.creditNotes.length > 0)) && (
        <p className="inv-meta inv-relations" data-testid="invoice-relations">
          {invoice.creditedInvoiceId && (
            <>
              {t('invoices.internalDetail.creditsInvoice')}:{' '}
              <button type="button" className="inv-link" onClick={() => navigate(`/invoices/${invoice.creditedInvoiceId}`)}>
                {invoice.creditedInvoiceNumber ?? invoice.creditedInvoiceId}
              </button>
            </>
          )}
          {invoice.creditNotes && invoice.creditNotes.length > 0 && (
            <>
              {t('invoices.internalDetail.creditNotesTitle')}:{' '}
              {invoice.creditNotes.map((note, index) => (
                <span key={note.id}>
                  {index > 0 && ', '}
                  <button type="button" className="inv-link" onClick={() => navigate(`/invoices/${note.id}`)}>
                    {note.invoiceNumber}
                  </button>{' '}
                  <Badge tone={INVOICE_STATUS_TONE[note.status]}>{t(INVOICE_STATUS_LABELS[note.status])}</Badge>
                </span>
              ))}
            </>
          )}
        </p>
      )}

      {editing ? (
        <div className="inv-edit">
          <div className="inv-edit-dates">
            <label>
              {t('invoices.fields.invoiceDate')}
              <input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} disabled={busy} />
            </label>
            <label>
              {t('invoices.fields.dueDate')}
              <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} disabled={busy} />
            </label>
            <label>
              {t('invoices.fields.period')}
              <input type="month" value={periodInput} onChange={(e) => setPeriodInput(e.target.value)} disabled={busy} />
            </label>
            <label>
              {t('invoices.fields.poNumber')}
              <input value={poNumber} onChange={(e) => setPoNumber(e.target.value)} disabled={busy} maxLength={100} />
            </label>
          </div>
          <table className="inv-lines-table">
            <thead>
              <tr>
                <th>{t('invoices.internalLines.columns.description')}</th>
                <th>{t('invoices.internalLines.columns.category')}</th>
                <th>{t('invoices.internalLines.columns.quantity')}</th>
                <th>{t('invoices.internalLines.columns.price')}</th>
                <th>{t('invoices.internalLines.columns.vat')}</th>
                <th aria-label={t('invoices.internalLines.columns.actions')} />
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => (
                <tr key={line.key}>
                  <td>
                    {line.orderNumber && <code className="inv-line-order">{line.orderNumber}</code>}
                    <input value={line.description} onChange={(e) => setLine(line.key, { description: e.target.value })} disabled={busy} maxLength={500} />
                  </td>
                  <td>
                    <select
                      aria-label={t('invoices.internalLines.categoryFor', { name: line.description || t('invoices.internalLines.newLine') })}
                      value={line.salesCategoryId ?? ''}
                      onChange={(e) => {
                        const categoryId = e.target.value || null
                        // Wave 2 §3 (InvoiceTextResolver): an empty description takes the code's
                        // invoice text (invoiceDescriptionNl, terugval naam) — nooit overschrijven.
                        const category = salesCategories.find((c) => c.id === categoryId)
                        const prefill = !line.description.trim() && category
                          ? { description: category.invoiceDescriptionNl ?? category.name }
                          : {}
                        setLine(line.key, { salesCategoryId: categoryId, ...prefill })
                      }}
                      disabled={busy}
                    >
                      <option value="">{t('invoices.common.none')}</option>
                      {salesCategories.map((category) => (
                        <option key={category.id} value={category.id}>
                          {category.name}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input type="number" min={0.01} step="0.01" value={line.quantity} onChange={(e) => setLine(line.key, { quantity: Number(e.target.value) })} disabled={busy} />
                  </td>
                  <td>
                    <input type="number" step="0.01" value={line.unitPrice} onChange={(e) => setLine(line.key, { unitPrice: Number(e.target.value) })} disabled={busy} />
                  </td>
                  <td>
                    <input type="number" min={0} max={100} step="0.5" value={line.vatRatePercent} onChange={(e) => setLine(line.key, { vatRatePercent: Number(e.target.value) })} disabled={busy} />
                  </td>
                  <td>
                    <button type="button" className="inv-link inv-link-danger" onClick={() => setLines((rows) => rows.filter((r) => r.key !== line.key))} disabled={busy}>
                      {t('ui.actions.delete')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Button
            variant="secondary"
            onClick={() =>
              setLines((rows) => [
                ...rows,
                { key: `l-${++lineKey}`, id: null, orderNumber: null, description: '', quantity: 1, unitPrice: 0, vatRatePercent: 21, salesCategoryId: null },
              ])
            }
            disabled={busy}
          >
            {t('invoices.internalLines.addLine')}
          </Button>
          <label className="inv-notes-label">
            {t('invoices.fields.notes')}
            <textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={4000} />
          </label>
          <div className="inv-edit-actions">
            <Button variant="secondary" onClick={() => setEditing(false)} disabled={busy}>
              {t('ui.actions.cancel')}
            </Button>
            <Button onClick={() => void handleSave()} disabled={busy}>
              {busy ? t('invoices.common.saving') : t('ui.actions.save')}
            </Button>
          </div>
        </div>
      ) : (
        <>
          <table className="inv-lines-table">
            <thead>
              <tr>
                <th>#</th>
                <th>{t('invoices.internalLines.columns.description')}</th>
                <th>{t('invoices.internalLines.columns.quantity')}</th>
                <th>{t('invoices.internalLines.columns.price')}</th>
                <th>{t('invoices.internalLines.columns.vat')}</th>
                <th>{t('invoices.internalLines.columns.amount')}</th>
              </tr>
            </thead>
            <tbody>
              {invoice.lines.map((line) => (
                <tr key={line.id}>
                  <td>{line.sequence}</td>
                  <td>
                    {line.orderNumber && (
                      <button type="button" className="inv-link" onClick={() => navigate(`/transport-orders/${line.transportOrderId}`)}>
                        {line.orderNumber}
                      </button>
                    )}{' '}
                    {line.customerDescription ?? line.description}
                    {line.customerDescription
                      && line.customerDescription !== line.description
                      && line.description !== line.salesCategoryName && (
                      <div className="customer-form-muted">
                        {line.description}
                      </div>
                    )}
                    {(line.salesCategoryName || line.ledgerAccountNumber) && (
                      <div className="customer-form-muted">
                        {line.salesCategoryName}
                        {line.ledgerAccountNumber && ` → ${line.ledgerAccountNumber} ${line.ledgerAccountName ?? ''}`.trimEnd()}
                      </div>
                    )}
                    {line.ledgerWarning && (
                      <div>
                        <Badge tone="warning">{line.ledgerWarning}</Badge>
                      </div>
                    )}
                    <InvoiceLineFiscalBadge line={line} />
                  </td>
                  <td>{formatQuantity(line.quantity)}</td>
                  <td>{euro(line.unitPrice, invoice.currency)}</td>
                  <td>{formatQuantity(line.vatRatePercent)}%</td>
                  <td>{euro(line.lineTotal, invoice.currency)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={5}>{t('invoices.detail.subtotal')}</td>
                <td>{euro(invoice.subtotal, invoice.currency)}</td>
              </tr>
              <tr>
                <td colSpan={5}>{t('invoices.internalDetail.vat')}</td>
                <td>{euro(invoice.vatAmount, invoice.currency)}</td>
              </tr>
              <tr className="inv-total-row">
                <td colSpan={5}>{t('invoices.detail.total')}</td>
                <td>{euro(invoice.total, invoice.currency)}</td>
              </tr>
            </tfoot>
          </table>

          {invoice.notes && <p className="inv-notes">{invoice.notes}</p>}

          <div className="inv-detail-actions">
            {editable && (
              <Button variant="secondary" onClick={startEditing} disabled={busy}>
                {t('ui.actions.edit')}
              </Button>
            )}
            {canOverrideNumber && (
              <Button variant="secondary" onClick={openOverride} disabled={busy}>
                {t('invoices.override.open')}
              </Button>
            )}
            {deletable && (
              <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
                {t('ui.actions.delete')}
              </Button>
            )}
            {(invoice.status === 'Sent' || invoice.status === 'Paid')
              && invoice.kind !== 'CreditNote'
              && hasPermission('invoices.create')
              && !(invoice.creditNotes ?? []).some((note) => note.status !== 'Cancelled') && (
                <Button
                  variant="secondary"
                  title={t('invoices.internalDetail.creditNoteHint')}
                  onClick={async () => {
                    setBusy(true)
                    try {
                      const created = await createCreditNote(invoice.id)
                      showSuccess(t('invoices.internalDetail.creditNoteCreated', { number: created.invoiceNumber }))
                      navigate(`/invoices/${created.id}`)
                    } catch (err) {
                      showError(localizeApiError(t, err, t('invoices.internalDetail.creditNoteError')))
                    } finally {
                      setBusy(false)
                    }
                  }}
                  disabled={busy}
                >
                  {t('invoices.internalDetail.creditNoteAction')}
                </Button>
              )}
            {(invoice.status === 'Sent' || invoice.status === 'Paid')
              && hasPermission('accounting.manage')
              && invoice.lines.some((line) => !line.ledgerAccountNumber) && (
                <Button
                  variant="secondary"
                  onClick={async () => {
                    setBusy(true)
                    try {
                      const updated = await completeInvoiceLedgerSnapshots(invoice.id)
                      setInvoice(updated)
                      showSuccess(t('invoices.internalDetail.ledgerCompleted'))
                    } catch (err) {
                      showError(localizeApiError(t, err, t('invoices.internalDetail.ledgerError')))
                    } finally {
                      setBusy(false)
                    }
                  }}
                  disabled={busy}
                >
                  {t('invoices.internalDetail.ledgerAction')}
                </Button>
              )}
          </div>
        </>
      )}

      {hasAnyPermission(['invoice_attachments.view', 'invoice_attachments.manage']) && (
        <InvoiceAttachmentsSection
          invoiceId={invoice.id}
          canManage={hasPermission('invoice_attachments.manage')}
          isDraft={invoice.status === 'Draft'}
        />
      )}

      {hasAnyPermission(['peppol.view', 'peppol.validate']) && (
        <InvoicePeppolPanel invoiceId={invoice.id} invoiceNumber={invoice.invoiceNumber} invoiceStatus={invoice.status} />
      )}

      {overrideOpen && (
        <Modal
          title={t('invoices.override.title')}
          onClose={() => setOverrideOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setOverrideOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button
                onClick={() => void handleOverride()}
                disabled={busy || !overrideNumber.trim() || !overrideReason.trim()}
              >
                {busy ? t('invoices.common.busy') : t('invoices.override.confirm')}
              </Button>
            </>
          }
        >
          <div className="inv-override-form">
            <FormField
              label={t('invoices.override.numberLabel')}
              htmlFor="inv-override-number"
              required
              error={getFieldError(overrideFieldErrors, 'invoiceNumber')}
            >
              <input
                id="inv-override-number"
                value={overrideNumber}
                onChange={(e) => setOverrideNumber(e.target.value)}
                disabled={busy}
                maxLength={30}
              />
            </FormField>
            <FormField
              label={t('invoices.common.reason')}
              htmlFor="inv-override-reason"
              required
              error={getFieldError(overrideFieldErrors, 'reason')}
            >
              <textarea
                id="inv-override-reason"
                rows={3}
                value={overrideReason}
                onChange={(e) => setOverrideReason(e.target.value)}
                disabled={busy}
                maxLength={500}
              />
            </FormField>
            {overrideError && (
              <p className="inv-override-error" role="alert">
                {overrideError}
              </p>
            )}
          </div>
        </Modal>
      )}

      {confirmSend && (
        <Modal
          title={t('invoices.internalDetail.sendConfirm.title')}
          onClose={() => !busy && setConfirmSend(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setConfirmSend(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={() => void handleSend()} disabled={busy} data-testid="invoice-send-confirm">
                {busy ? t('invoices.internalDetail.sendConfirm.busy') : t('invoices.internalDetail.sendConfirm.confirm')}
              </Button>
            </>
          }
        >
          <p>{t('invoices.internalDetail.sendConfirm.intro')}</p>
          <dl className="inv-send-summary" data-testid="invoice-send-summary">
            <dt>{t('invoices.internalDetail.sendConfirm.number')}</dt>
            <dd>{invoice.invoiceNumber}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.customer')}</dt>
            <dd>{invoice.customerName}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.entity')}</dt>
            <dd>{invoice.legalEntityName ?? '—'}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.total')}</dt>
            <dd>{euro(invoice.total, invoice.currency)}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.language')}</dt>
            <dd>
              {invoice.languageCode && ['nl', 'fr', 'en', 'de'].includes(invoice.languageCode.toLowerCase())
                ? t(`invoices.fiscal.languages.${invoice.languageCode.toLowerCase()}`)
                : (invoice.languageCode?.toUpperCase() ?? '—')}
            </dd>
            <dt>{t('invoices.internalDetail.sendConfirm.treatment')}</dt>
            <dd>{fiscalTreatmentLabel(t, invoice.customerVatTreatment) ?? '—'}</dd>
          </dl>
        </Modal>
      )}

      {confirmTransition && (
        <ConfirmDialog
          title={t('invoices.internalDetail.cancelTitle')}
          message={t('invoices.internalDetail.cancelMessage', { number: invoice.invoiceNumber })}
          confirmLabel={t(INVOICE_TRANSITION_LABELS.Cancelled)}
          destructive
          onConfirm={() => void applyTransition(confirmTransition)}
          onCancel={() => setConfirmTransition(null)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('invoices.internalDetail.deleteTitle')}
          message={t('invoices.internalDetail.deleteMessage', { number: invoice.invoiceNumber })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}

interface InvoiceAttachmentsSectionProps {
  invoiceId: string
  canManage: boolean
  isDraft: boolean
}

/** Bijlagen van een factuur: uploaden, meesturen togglen, downloaden en (in concept) verwijderen. */
export function InvoiceAttachmentsSection({ invoiceId, canManage, isDraft }: InvoiceAttachmentsSectionProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [attachments, setAttachments] = useState<InvoiceAttachment[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [removeTarget, setRemoveTarget] = useState<InvoiceAttachment | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    listInvoiceAttachments(invoiceId)
      .then((data) => {
        setAttachments(data)
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('invoices.attachments.loadError'))
  }, [invoiceId])

  useEffect(() => {
    reload()
  }, [reload])

  async function handleUpload(file: File) {
    if (file.size > MAX_INVOICE_ATTACHMENT_BYTES) {
      showError(t('invoices.attachments.tooLarge'))
      return
    }
    setBusy(true)
    try {
      await uploadInvoiceAttachment(invoiceId, file)
      showSuccess(t('invoices.attachments.uploaded'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.attachments.uploadError')))
    } finally {
      setBusy(false)
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  async function toggleInclude(attachment: InvoiceAttachment) {
    setBusy(true)
    try {
      const updated = await updateInvoiceAttachment(invoiceId, attachment.id, {
        includeWhenSending: !attachment.includeWhenSending,
        notes: attachment.notes,
      })
      setAttachments((rows) => (rows ?? []).map((row) => (row.id === updated.id ? updated : row)))
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.attachments.updateError')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="inv-section">
      <div className="inv-manual-head">
        <h3>{t('invoices.detail.attachmentsTitle')}</h3>
        {canManage && (
          <span className="inv-attachment-upload">
            <input
              ref={fileInputRef}
              type="file"
              accept={INVOICE_ATTACHMENT_ACCEPT}
              disabled={busy}
              aria-label={t('invoices.attachments.choose')}
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (file) void handleUpload(file)
              }}
            />
          </span>
        )}
      </div>

      {loadErrorKey && (
        <p className="inv-override-error" role="alert">
          {t(loadErrorKey)}
        </p>
      )}
      {!loadErrorKey && attachments !== null && attachments.length === 0 && (
        <p className="placeholder-text">{t('invoices.attachments.empty')}</p>
      )}
      {attachments !== null && attachments.length > 0 && (
        <table className="inv-lines-table">
          <thead>
            <tr>
              <th>{t('invoices.attachments.columns.file')}</th>
              <th>{t('invoices.attachments.columns.size')}</th>
              <th>{t('invoices.attachments.columns.uploadedAt')}</th>
              <th>{t('invoices.attachments.columns.include')}</th>
              <th aria-label={t('invoices.internalLines.columns.actions')} />
            </tr>
          </thead>
          <tbody>
            {attachments.map((attachment) => (
              <tr key={attachment.id}>
                <td>{attachment.fileName}</td>
                <td>{formatFileSize(attachment.sizeBytes)}</td>
                <td>{formatDate(attachment.uploadedAt)}</td>
                <td>
                  <label className="inv-attachment-toggle">
                    <input
                      type="checkbox"
                      checked={attachment.includeWhenSending}
                      onChange={() => void toggleInclude(attachment)}
                      disabled={!canManage || busy}
                    />
                    <span>{attachment.includeWhenSending ? t('invoices.common.yes') : t('invoices.common.no')}</span>
                  </label>
                </td>
                <td>
                  <button
                    type="button"
                    className="inv-link"
                    onClick={() =>
                      void downloadInvoiceAttachment(invoiceId, attachment.id, attachment.fileName).catch(() =>
                        showError(t('invoices.detail.attachmentError')),
                      )
                    }
                  >
                    {t('invoices.attachments.download')}
                  </button>
                  {isDraft && canManage && (
                    <button type="button" className="inv-link inv-link-danger" onClick={() => setRemoveTarget(attachment)} disabled={busy}>
                      {t('ui.actions.delete')}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {removeTarget && (
        <ConfirmDialog
          title={t('invoices.attachments.removeTitle')}
          message={t('invoices.attachments.removeMessage', { name: removeTarget.fileName })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteInvoiceAttachment(invoiceId, removeTarget.id)
              showSuccess(t('invoices.attachments.removed'))
              setRemoveTarget(null)
              reload()
            } catch (err) {
              showError(localizeApiError(t, err, t('invoices.attachments.removeError')))
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
    </section>
  )
}
