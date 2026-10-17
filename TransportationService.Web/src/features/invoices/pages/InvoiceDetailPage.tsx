import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, localizeApiError, type FieldErrors } from '../../../api/problemDetails'
import { isNotFoundError } from '../../../api/notFound'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  changeInvoiceStatus,
  completeInvoiceLedgerSnapshots,
  createCreditNote,
  deleteInvoice,
  fetchInvoicePdfUrl,
  getInvoice,
  getInvoiceIssuePreflight,
  overrideInvoiceNumber,
  updateInvoice,
} from '../api/invoicesApi'
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
import { InvoiceHistoryPanel } from '../components/InvoiceHistoryPanel'
import { InvoiceIssuePreflightList } from '../components/InvoiceIssuePreflightList'
import { CreditNoteDialog } from '../components/CreditNoteDialog'
import { listSalesCategories, type SalesCategory } from '../../accounting/api/accountingApi'
import {
  addDaysIso,
  creditAmount,
  daysBetweenIso,
  euro,
  INVOICE_STATUS_LABELS,
  INVOICE_STATUS_TONE,
  INVOICE_TRANSITION_LABELS,
  invoiceDisplayNumber,
  unitPrice as formatUnitPrice,
  type InvoiceDetail,
  type InvoiceIssuePreflight,
  type InvoiceStatus,
  type UpdateLineInput,
} from '../types'
import { formatPeriod, monthInputToPeriod, periodToMonthInput } from '../utils/invoicePeriod'
import { formatDate, formatDateTime } from '../../../utils/dates'
import { InvoiceFiscalSummary, InvoiceLineFiscalBadge } from '../components/InvoiceFiscalSummary'
import './invoices.css'

interface EditableLine extends UpdateLineInput {
  key: string
  orderNumber: string | null
  /** Set on an activity-backed line (transportOrderId is null then). */
  dossierNumber: string | null
}

let lineKey = 0

interface EditSnapshot {
  invoiceDate: string
  dueDate: string
  periodInput: string
  notes: string
  poNumber: string
  lines: UpdateLineInput[]
}

export function InvoiceDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()

  const [invoice, setInvoice] = useState<InvoiceDetail | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [loadNotFound, setLoadNotFound] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)
  const [historyToken, setHistoryToken] = useState(0)
  const [busy, setBusy] = useState(false)

  const [editing, setEditing] = useState(false)
  const [invoiceDate, setInvoiceDate] = useState('')
  const [dueDate, setDueDate] = useState('')
  // ACC-H5: the due date follows the invoice date until the user edits it by hand in this session.
  const [dueDateTouched, setDueDateTouched] = useState(false)
  const [periodInput, setPeriodInput] = useState('')
  const [notes, setNotes] = useState('')
  const [poNumber, setPoNumber] = useState('')
  const [lines, setLines] = useState<EditableLine[]>([])
  // ACC-M5: what the form looked like when editing started; "dirty" = differs from this.
  const [editBaseline, setEditBaseline] = useState<string>('')
  const [vatFallbackHint, setVatFallbackHint] = useState(false)

  const [confirmTransition, setConfirmTransition] = useState<InvoiceStatus | null>(null)
  // Send is irreversible (number, snapshots, descriptions freeze): it always goes through an
  // explicit summary dialog, and a single in-flight guard makes a double click a no-op.
  const [confirmSend, setConfirmSend] = useState(false)
  const sendInFlight = useRef(false)
  // ACC-M4: the issue preflight shown in the send dialog; null while loading.
  const [preflight, setPreflight] = useState<InvoiceIssuePreflight | null>(null)
  const [preflightError, setPreflightError] = useState<string | null>(null)
  const [pdfBusy, setPdfBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [creditDialog, setCreditDialog] = useState(false)
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
        setLoadNotFound(false)
      })
      .catch((err: unknown) => {
        if (!mounted) return
        // ACC-M8: a record that does not exist says so, with the way back — retrying never helps.
        setLoadNotFound(isNotFoundError(err))
        setLoadErrorKey('invoices.detail.loadError')
      })
    return () => {
      mounted = false
    }
  }, [id, reloadToken])

  // ACC-M4: the send dialog opens with the server's preflight (PO, categories, VAT, sources …).
  const preflightRequest = useRef(0)
  function openSendDialog() {
    if (!invoice) return
    const request = ++preflightRequest.current
    setPreflight(null)
    setPreflightError(null)
    setConfirmSend(true)
    getInvoiceIssuePreflight(invoice.id)
      .then((result) => {
        if (preflightRequest.current === request) setPreflight(result)
      })
      .catch(() => {
        // The server-side guard still runs on confirm; the dialog only loses its early warning.
        if (preflightRequest.current === request) setPreflightError(t('invoices.detail.preflightLoadError'))
      })
  }

  const snapshotOf = useCallback(
    (state: EditSnapshot) => JSON.stringify({ ...state, lines: state.lines.map((l) => ({ ...l, salesCategoryId: l.salesCategoryId ?? null })) }),
    [],
  )

  function toUpdateLines(rows: EditableLine[]): UpdateLineInput[] {
    return rows.map((line) => ({
      id: line.id,
      description: line.description,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      vatRatePercent: line.vatRatePercent,
      salesCategoryId: line.salesCategoryId ?? null,
    }))
  }

  function startEditing() {
    if (!invoice) return
    const period = periodToMonthInput(invoice.invoicePeriodYear, invoice.invoicePeriodMonth)
    const rows: EditableLine[] = invoice.lines.map((l) => ({
      key: `l-${++lineKey}`,
      id: l.id,
      orderNumber: l.orderNumber,
      dossierNumber: l.dossierNumber,
      description: l.description,
      quantity: l.quantity,
      unitPrice: l.unitPrice,
      vatRatePercent: l.vatRatePercent,
      salesCategoryId: l.salesCategoryId,
    }))
    setInvoiceDate(invoice.invoiceDate)
    setDueDate(invoice.dueDate)
    setDueDateTouched(false)
    setPeriodInput(period)
    setNotes(invoice.notes ?? '')
    setPoNumber(invoice.purchaseOrderNumber ?? '')
    setLines(rows)
    setVatFallbackHint(false)
    setEditBaseline(snapshotOf({
      invoiceDate: invoice.invoiceDate,
      dueDate: invoice.dueDate,
      periodInput: period,
      notes: invoice.notes ?? '',
      poNumber: invoice.purchaseOrderNumber ?? '',
      lines: toUpdateLines(rows),
    }))
    listSalesCategories()
      .then(setSalesCategories)
      .catch(() => setSalesCategories([]))
    setEditing(true)
  }

  function setLine(key: string, patch: Partial<EditableLine>) {
    setLines((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  /** ACC-H5: effective payment term — the server's, else what the draft's dates already say. */
  function effectiveTermDays(): number | null {
    if (!invoice) return null
    if (typeof invoice.paymentTermDays === 'number') return invoice.paymentTermDays
    return daysBetweenIso(invoice.invoiceDate, invoice.dueDate)
  }

  function handleInvoiceDateChange(value: string) {
    setInvoiceDate(value)
    if (dueDateTouched) return
    const term = effectiveTermDays()
    if (term !== null && value) setDueDate(addDaysIso(value, term))
  }

  /** ACC-H1: a new manual line takes the invoice's own default VAT, never a hard-coded 0. */
  function addLine() {
    if (!invoice) return
    const fromInvoice = typeof invoice.defaultVatRatePercent === 'number' ? invoice.defaultVatRatePercent : null
    const fromLines = lines.length > 0 ? lines[0].vatRatePercent : invoice.lines[0]?.vatRatePercent ?? null
    const vat = fromInvoice ?? fromLines ?? 21
    setVatFallbackHint(fromInvoice === null && fromLines === null)
    setLines((rows) => [
      ...rows,
      { key: `l-${++lineKey}`, id: null, orderNumber: null, dossierNumber: null, description: '', quantity: 1, unitPrice: 0, vatRatePercent: vat, salesCategoryId: null },
    ])
  }

  const editDirty =
    editing
    && snapshotOf({ invoiceDate, dueDate, periodInput, notes, poNumber, lines: toUpdateLines(lines) }) !== editBaseline

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
        lines: toUpdateLines(lines),
      })
      setInvoice(updated)
      setEditing(false)
      setHistoryToken((n) => n + 1)
      showSuccess(t('invoices.internalDetail.updated'))
    } catch (err) {
      // ACC-M4: the server names the problem ("Lijn 3 …"); never swap it for a generic sentence.
      showError(localizeApiError(t, err, t('invoices.internalDetail.saveError')))
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
      setHistoryToken((n) => n + 1)
      showSuccess(t('invoices.internalDetail.statusChanged', { status: t(INVOICE_STATUS_LABELS[target]) }))
    } catch (err) {
      // ACC-M4: "Deze klant vereist een PO-nummer …" is the message the user needs, not a generic one.
      showError(localizeApiError(t, err, t('invoices.internalDetail.statusError')))
    } finally {
      setBusy(false)
      setConfirmTransition(null)
    }
  }

  async function handleCreditNote(reason: string) {
    if (!invoice) return
    setBusy(true)
    try {
      const created = await createCreditNote(invoice.id, { reason })
      showSuccess(t('invoices.internalDetail.creditNoteCreated', { number: invoiceDisplayNumber(created, t('invoices.detail.draftLabel')) }))
      setCreditDialog(false)
      navigate(`/invoices/${created.id}`)
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.internalDetail.creditNoteError')))
    } finally {
      setBusy(false)
    }
  }

  function openOverride() {
    if (!invoice) return
    setOverrideNumber(invoice.invoiceNumber ?? '')
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
      setHistoryToken((n) => n + 1)
      showSuccess(t('invoices.override.success', { number: updated.invoiceNumber ?? number }))
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
    } catch (err) {
      // A10: the H-06 refusal ("ooit verzonden … blijft bewaard als historisch document") is
      // precisely what the user needs here, and it used to be thrown away for a generic sentence.
      showError(localizeApiError(t, err, t('invoices.internalDetail.deleteError')))
      setConfirmDelete(false)
    }
  }

  if (loadErrorKey && loadNotFound) return <ErrorState message={t('invoices.detail.notFound')} backTo="/invoices" />
  if (loadErrorKey) return <ErrorState message={t(loadErrorKey)} onRetry={() => { setLoadErrorKey(null); setReloadToken((n) => n + 1) }} backTo="/invoices" />
  if (!invoice) return <LoadingState message={t('invoices.detail.loading')} />

  const isDraft = invoice.status === 'Draft'
  const isCredit = invoice.kind === 'CreditNote'
  const number = invoiceDisplayNumber(invoice, t('invoices.detail.draftLabel'))
  const amount = (value: number) => (isCredit ? creditAmount(value, invoice.currency) : euro(value, invoice.currency))
  const editable = isDraft && hasPermission('invoices.edit')
  const deletable = (isDraft || invoice.status === 'Cancelled') && hasPermission('invoices.delete')
  const canOverrideNumber = isDraft && hasPermission('invoices.override_number')
  // H-06: een definitief document (verzonden of betaald) wordt nooit geannuleerd — corrigeren
  // gebeurt met 'Creditnota maken' hieronder. De server weigert het hoe dan ook; deze filter
  // zorgt dat de knop ook niet meer verschijnt bij een oudere/gecachete allowedTransitions.
  const finalized = invoice.status === 'Sent' || invoice.status === 'Paid'
  const offeredTransitions = invoice.allowedTransitions.filter(
    // ACC-M11: a credit note settles against its invoice; "betaald" is not a state it reaches here.
    (target) => !(finalized && target === 'Cancelled') && !(isCredit && target === 'Paid'),
  )
  const canChangeStatus = hasPermission('invoices.change_status')
  const canCredit = finalized
    && !isCredit
    && hasPermission('invoices.create')
    && !(invoice.creditNotes ?? []).some((note) => note.status !== 'Cancelled')
  const subtitle = isCredit
    ? t('invoices.detail.creditSubtitle', { date: formatDate(invoice.invoiceDate), number: invoice.creditedInvoiceNumber ?? invoice.creditedInvoiceId ?? '—' })
    : t('invoices.internalDetail.subtitle', { date: formatDate(invoice.invoiceDate), dueDate: formatDate(invoice.dueDate) })

  return (
    <div className={isCredit ? 'inv-credit-document' : undefined}>
      <UnsavedChangesGuard when={editDirty && !busy} />
      <Breadcrumbs items={[{ label: t('invoices.list.title'), to: '/invoices' }, { label: number }]} />
      <PageHeader
        title={`${isCredit ? `${t('invoices.detail.creditTitle')} ` : ''}${number} — ${invoice.customerName}`}
        subtitle={`${subtitle}${invoice.customerVatNumber ? ` · ${invoice.customerVatNumber}` : ''}`}
        action={
          <span className="inv-header-actions">
            {isCredit && <Badge tone="warning">{t('invoices.creditNote')}</Badge>}
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
            {canChangeStatus &&
              !editing &&
              offeredTransitions.map((target) => (
                <Button
                  key={target}
                  variant={target === 'Cancelled' ? 'secondary' : 'primary'}
                  onClick={() =>
                    target === 'Cancelled'
                      ? setConfirmTransition(target)
                      : target === 'Sent'
                        ? openSendDialog()
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
        {isDraft && <>{t('invoices.detail.draftNumberHint')} · </>}
        {invoice.legalEntityName && <>{t('invoices.fields.billingEntity')}: {invoice.legalEntityName} · </>}
        {t('invoices.internalDetail.periodLabel')}: {formatPeriod(invoice.invoicePeriodYear, invoice.invoicePeriodMonth)}
        {invoice.purchaseOrderNumber && <> · {t('invoices.detail.poNumber', { number: invoice.purchaseOrderNumber })}</>}
      </p>
      {invoice.status === 'Paid' && !isCredit && (
        <p className="inv-meta inv-paid-info" data-testid="invoice-paid-info">
          {invoice.paidAt
            ? t('invoices.detail.paidAt', { date: formatDateTime(invoice.paidAt) })
            : t(INVOICE_STATUS_LABELS.Paid)}
          {invoice.paidByName && <> {t('invoices.detail.paidBy', { name: invoice.paidByName })}</>}
          {' · '}{t('invoices.detail.paymentManual')}
        </p>
      )}
      {isCredit && invoice.creditReason && (
        <p className="inv-meta" data-testid="invoice-credit-reason">
          {t('invoices.detail.creditReason')}: {invoice.creditReason}
        </p>
      )}
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
                    {note.invoiceNumber ?? t('invoices.detail.draftLabel')}
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
              <input type="date" value={invoiceDate} onChange={(e) => handleInvoiceDateChange(e.target.value)} disabled={busy} />
            </label>
            <label>
              {t('invoices.fields.dueDate')}
              <input
                type="date"
                value={dueDate}
                onChange={(e) => {
                  setDueDateTouched(true)
                  setDueDate(e.target.value)
                }}
                disabled={busy}
              />
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
          {!dueDateTouched && effectiveTermDays() !== null && invoiceDate !== invoice.invoiceDate && (
            <p className="customer-form-muted" data-testid="due-date-recalculated">
              {t('invoices.detail.dueDateRecalculated', { days: effectiveTermDays() ?? 0 })}
            </p>
          )}
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
                    {line.dossierNumber && <code className="inv-line-order">{line.dossierNumber}</code>}
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
                    {/* ACC-M2: three decimals for quantities, never rounded on the client. */}
                    <input type="number" min={0.001} step="0.001" value={line.quantity} aria-label={t('invoices.new.quantityLabel')} onChange={(e) => setLine(line.key, { quantity: Number(e.target.value) })} disabled={busy} />
                  </td>
                  <td>
                    {/* ACC-M2: four decimals for unit prices (km, kg, litre, hour fractions). */}
                    <input type="number" step="0.0001" value={line.unitPrice} aria-label={t('invoices.new.unitPriceLabel')} onChange={(e) => setLine(line.key, { unitPrice: Number(e.target.value) })} disabled={busy} />
                  </td>
                  <td>
                    <input type="number" min={0} max={100} step="0.5" value={line.vatRatePercent} aria-label={t('invoices.internalLines.columns.vat')} onChange={(e) => setLine(line.key, { vatRatePercent: Number(e.target.value) })} disabled={busy} />
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
          <Button variant="secondary" onClick={addLine} disabled={busy}>
            {t('invoices.internalLines.addLine')}
          </Button>
          {typeof invoice.defaultVatRatePercent === 'number' ? (
            <p className="customer-form-muted">{t('invoices.detail.vatDefaultHint', { rate: formatQuantity(invoice.defaultVatRatePercent) })}</p>
          ) : (
            vatFallbackHint && <p className="inv-period-warning" role="note">{t('invoices.detail.vatDefaultFallback')}</p>
          )}
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
                    )}
                    {/* Activity line: the DTO carries no dossierId, so the number is a label, not a link. */}
                    {line.dossierNumber && <code className="inv-line-order">{line.dossierNumber}</code>}{' '}
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
                      <div className="inv-ledger-warning">
                        <Badge tone="warning">{line.ledgerWarning}</Badge>
                        {/* B43: the warning names the fix; take the user there when allowed. */}
                        {hasAnyPermission(['accounting.view', 'accounting.manage']) && (
                          <Link to="/settings/accounting">{t('invoices.detail.ledgerSettingsLink')}</Link>
                        )}
                      </div>
                    )}
                    <InvoiceLineFiscalBadge line={line} />
                  </td>
                  <td>
                    {formatQuantity(line.quantity, 3)}
                    {line.unitLabel && <span className="inv-unit-label"> {line.unitLabel}</span>}
                  </td>
                  <td>{formatUnitPrice(line.unitPrice, invoice.currency)}</td>
                  <td>{formatQuantity(line.vatRatePercent)}%</td>
                  <td>{amount(line.lineTotal)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={5}>{t('invoices.detail.subtotal')}</td>
                <td>{amount(invoice.subtotal)}</td>
              </tr>
              <tr>
                <td colSpan={5}>{t('invoices.internalDetail.vat')}</td>
                <td>{amount(invoice.vatAmount)}</td>
              </tr>
              <tr className="inv-total-row">
                <td colSpan={5}>{isCredit ? t('invoices.detail.creditTotalLabel') : t('invoices.detail.total')}</td>
                <td data-testid="invoice-total">{amount(invoice.total)}</td>
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
              <Button variant="secondary" onClick={openOverride} disabled={busy} title={t('invoices.detail.manualNumberHint')}>
                {invoice.invoiceNumber ? t('invoices.override.open') : t('invoices.detail.manualNumberAction')}
              </Button>
            )}
            {deletable && (
              <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
                {t('ui.actions.delete')}
              </Button>
            )}
            {canCredit && (
              <Button
                variant="secondary"
                title={t('invoices.internalDetail.creditNoteHint')}
                onClick={() => setCreditDialog(true)}
                disabled={busy}
              >
                {t('invoices.internalDetail.creditNoteAction')}
              </Button>
            )}
            {finalized
              && hasPermission('accounting.manage')
              && invoice.lines.some((line) => !line.ledgerAccountNumber) && (
                <Button
                  variant="secondary"
                  onClick={async () => {
                    setBusy(true)
                    try {
                      const updated = await completeInvoiceLedgerSnapshots(invoice.id)
                      setInvoice(updated)
                      setHistoryToken((n) => n + 1)
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
          isDraft={isDraft}
        />
      )}

      {hasAnyPermission(['peppol.view', 'peppol.validate']) && (
        <InvoicePeppolPanel invoiceId={invoice.id} invoiceNumber={number} invoiceStatus={invoice.status} />
      )}

      <InvoiceHistoryPanel invoiceId={invoice.id} refreshToken={historyToken} showPeppolHint={hasAnyPermission(['peppol.view', 'peppol.validate'])} />

      {creditDialog && (
        <CreditNoteDialog
          invoiceNumber={number}
          totalLabel={euro(invoice.total, invoice.currency)}
          busy={busy}
          onConfirm={(reason) => void handleCreditNote(reason)}
          onCancel={() => setCreditDialog(false)}
        />
      )}

      {overrideOpen && (
        <Modal
          title={invoice.invoiceNumber ? t('invoices.override.title') : t('invoices.detail.manualNumberAction')}
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
            {!invoice.invoiceNumber && <p className="customer-form-muted">{t('invoices.detail.manualNumberHint')}</p>}
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
              <Button
                onClick={() => void handleSend()}
                // ACC-M4: a known blocker disables the confirm; an unknown preflight keeps the server guard.
                disabled={busy || (preflight !== null && !preflight.canIssue)}
                data-testid="invoice-send-confirm"
              >
                {busy ? t('invoices.internalDetail.sendConfirm.busy') : t('invoices.internalDetail.sendConfirm.confirm')}
              </Button>
            </>
          }
        >
          <p>{t('invoices.internalDetail.sendConfirm.intro')}</p>
          <dl className="inv-send-summary" data-testid="invoice-send-summary">
            <dt>{t('invoices.internalDetail.sendConfirm.number')}</dt>
            <dd>{invoice.invoiceNumber ?? t('invoices.detail.draftNumberHint')}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.customer')}</dt>
            <dd>{invoice.customerName}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.entity')}</dt>
            <dd>{invoice.legalEntityName ?? '—'}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.total')}</dt>
            <dd>{amount(invoice.total)}</dd>
            <dt>{t('invoices.internalDetail.sendConfirm.language')}</dt>
            <dd>
              {invoice.languageCode && ['nl', 'fr', 'en', 'de'].includes(invoice.languageCode.toLowerCase())
                ? t(`invoices.fiscal.languages.${invoice.languageCode.toLowerCase()}`)
                : (invoice.languageCode?.toUpperCase() ?? '—')}
            </dd>
            <dt>{t('invoices.internalDetail.sendConfirm.treatment')}</dt>
            <dd>{fiscalTreatmentLabel(t, invoice.customerVatTreatment) ?? '—'}</dd>
          </dl>
          <InvoiceIssuePreflightList preflight={preflight} error={preflightError} lines={invoice.lines} />
        </Modal>
      )}

      {confirmTransition && (
        <ConfirmDialog
          title={t('invoices.internalDetail.cancelTitle')}
          message={t('invoices.internalDetail.cancelMessage', { number })}
          confirmLabel={t(INVOICE_TRANSITION_LABELS.Cancelled)}
          destructive
          onConfirm={() => void applyTransition(confirmTransition)}
          onCancel={() => setConfirmTransition(null)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('invoices.internalDetail.deleteTitle')}
          message={t('invoices.internalDetail.deleteMessage', { number })}
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
