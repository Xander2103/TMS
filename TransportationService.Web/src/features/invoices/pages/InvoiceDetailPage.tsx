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
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { changeInvoiceStatus, completeInvoiceLedgerSnapshots, deleteInvoice, getInvoice, overrideInvoiceNumber, updateInvoice } from '../api/invoicesApi'
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
import './invoices.css'

interface EditableLine extends UpdateLineInput {
  key: string
  orderNumber: string | null
}

let lineKey = 0

export function InvoiceDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()

  const [invoice, setInvoice] = useState<InvoiceDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [editing, setEditing] = useState(false)
  const [invoiceDate, setInvoiceDate] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [periodInput, setPeriodInput] = useState('')
  const [notes, setNotes] = useState('')
  const [poNumber, setPoNumber] = useState('')
  const [lines, setLines] = useState<EditableLine[]>([])

  const [confirmTransition, setConfirmTransition] = useState<InvoiceStatus | null>(null)
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
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De factuur kon niet worden geladen.')
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
      showError('Een factuur heeft minstens één lijn nodig.')
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
      showSuccess('Factuur bijgewerkt.')
    } catch {
      showError('De factuur kon niet worden opgeslagen.')
    } finally {
      setBusy(false)
    }
  }

  async function applyTransition(target: InvoiceStatus) {
    if (!invoice) return
    setBusy(true)
    try {
      const updated = await changeInvoiceStatus(invoice.id, target)
      setInvoice(updated)
      showSuccess(`Factuur is nu: ${INVOICE_STATUS_LABELS[target]}.`)
    } catch {
      showError('De status kon niet worden gewijzigd.')
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
      setOverrideError('Vul een nieuw factuurnummer en een reden in.')
      return
    }
    setBusy(true)
    try {
      const updated = await overrideInvoiceNumber(invoice.id, { invoiceNumber: number, reason })
      setInvoice(updated)
      setOverrideOpen(false)
      showSuccess(`Factuurnummer aangepast naar ${updated.invoiceNumber}.`)
    } catch (err) {
      const { message, fieldErrors } = describeApiError(err, 'Het factuurnummer kon niet worden aangepast.')
      setOverrideError(message)
      setOverrideFieldErrors(fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!invoice) return
    try {
      await deleteInvoice(invoice.id)
      showSuccess('Factuur verwijderd.')
      navigate('/invoices')
    } catch {
      showError('De factuur kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!invoice) return <LoadingState message="Factuur laden..." />

  const editable = invoice.status === 'Draft' && hasPermission('invoices.edit')
  const deletable = (invoice.status === 'Draft' || invoice.status === 'Cancelled') && hasPermission('invoices.delete')
  const canOverrideNumber = invoice.status === 'Draft' && hasPermission('invoices.override_number')

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Facturen', to: '/invoices' }, { label: invoice.invoiceNumber }]} />
      <PageHeader
        title={`${invoice.invoiceNumber} — ${invoice.customerName}`}
        subtitle={`Factuurdatum ${invoice.invoiceDate} · vervalt ${invoice.dueDate}${invoice.customerVatNumber ? ` · ${invoice.customerVatNumber}` : ''}`}
        action={
          <span className="inv-header-actions">
            <Badge tone={INVOICE_STATUS_TONE[invoice.status]}>{INVOICE_STATUS_LABELS[invoice.status]}</Badge>
            {invoice.numberIsManual && <Badge tone="warning">handmatig nummer</Badge>}
            {hasPermission('invoices.change_status') &&
              !editing &&
              invoice.allowedTransitions.map((target) => (
                <Button
                  key={target}
                  variant={target === 'Cancelled' ? 'secondary' : 'primary'}
                  onClick={() => (target === 'Cancelled' ? setConfirmTransition(target) : void applyTransition(target))}
                  disabled={busy}
                >
                  {INVOICE_TRANSITION_LABELS[target]}
                </Button>
              ))}
          </span>
        }
      />

      <p className="inv-meta">
        {invoice.legalEntityName && <>Facturerende entiteit: {invoice.legalEntityName} · </>}
        Periode: {formatPeriod(invoice.invoicePeriodYear, invoice.invoicePeriodMonth)}
        {invoice.purchaseOrderNumber && <> · PO-nummer: {invoice.purchaseOrderNumber}</>}
      </p>

      {editing ? (
        <div className="inv-edit">
          <div className="inv-edit-dates">
            <label>
              Factuurdatum
              <input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} disabled={busy} />
            </label>
            <label>
              Vervaldag
              <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} disabled={busy} />
            </label>
            <label>
              Factuurperiode
              <input type="month" value={periodInput} onChange={(e) => setPeriodInput(e.target.value)} disabled={busy} />
            </label>
            <label>
              PO-nummer
              <input value={poNumber} onChange={(e) => setPoNumber(e.target.value)} disabled={busy} maxLength={100} />
            </label>
          </div>
          <table className="inv-lines-table">
            <thead>
              <tr>
                <th>Omschrijving</th>
                <th>Verkoopcategorie</th>
                <th>Aantal</th>
                <th>Prijs</th>
                <th>Btw %</th>
                <th aria-label="Acties" />
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
                      aria-label={`Verkoopcategorie voor ${line.description || 'nieuwe lijn'}`}
                      value={line.salesCategoryId ?? ''}
                      onChange={(e) => setLine(line.key, { salesCategoryId: e.target.value || null })}
                      disabled={busy}
                    >
                      <option value="">— Geen —</option>
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
                      Verwijderen
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
            + Lijn toevoegen
          </Button>
          <label className="inv-notes-label">
            Notities
            <textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={4000} />
          </label>
          <div className="inv-edit-actions">
            <Button variant="secondary" onClick={() => setEditing(false)} disabled={busy}>
              Annuleren
            </Button>
            <Button onClick={() => void handleSave()} disabled={busy}>
              {busy ? 'Opslaan…' : 'Opslaan'}
            </Button>
          </div>
        </div>
      ) : (
        <>
          <table className="inv-lines-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Omschrijving</th>
                <th>Aantal</th>
                <th>Prijs</th>
                <th>Btw %</th>
                <th>Bedrag</th>
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
                    {line.description}
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
                  </td>
                  <td>{line.quantity.toLocaleString('nl-BE')}</td>
                  <td>{euro(line.unitPrice, invoice.currency)}</td>
                  <td>{line.vatRatePercent.toLocaleString('nl-BE')}%</td>
                  <td>{euro(line.lineTotal, invoice.currency)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={5}>Subtotaal</td>
                <td>{euro(invoice.subtotal, invoice.currency)}</td>
              </tr>
              <tr>
                <td colSpan={5}>Btw</td>
                <td>{euro(invoice.vatAmount, invoice.currency)}</td>
              </tr>
              <tr className="inv-total-row">
                <td colSpan={5}>Totaal</td>
                <td>{euro(invoice.total, invoice.currency)}</td>
              </tr>
            </tfoot>
          </table>

          {invoice.notes && <p className="inv-notes">{invoice.notes}</p>}

          <div className="inv-detail-actions">
            {editable && (
              <Button variant="secondary" onClick={startEditing} disabled={busy}>
                Bewerken
              </Button>
            )}
            {canOverrideNumber && (
              <Button variant="secondary" onClick={openOverride} disabled={busy}>
                Nummer corrigeren
              </Button>
            )}
            {deletable && (
              <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
                Verwijderen
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
                      showSuccess('Ontbrekende boekhoudsnapshots aangevuld waar een mapping bestaat.')
                    } catch (err) {
                      showError(describeApiError(err, 'De snapshots konden niet worden aangevuld.').message)
                    } finally {
                      setBusy(false)
                    }
                  }}
                  disabled={busy}
                >
                  Boekhoudsnapshot aanvullen
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
          title="Factuurnummer corrigeren"
          onClose={() => setOverrideOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setOverrideOpen(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button
                onClick={() => void handleOverride()}
                disabled={busy || !overrideNumber.trim() || !overrideReason.trim()}
              >
                {busy ? 'Bezig…' : 'Nummer aanpassen'}
              </Button>
            </>
          }
        >
          <div className="inv-override-form">
            <FormField
              label="Nieuw factuurnummer"
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
              label="Reden"
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

      {confirmTransition && (
        <ConfirmDialog
          title="Factuur annuleren"
          message={`Weet je zeker dat je factuur ${invoice.invoiceNumber} wilt annuleren? Gefactureerde opdrachten komen weer vrij.`}
          confirmLabel="Annuleren"
          destructive
          onConfirm={() => void applyTransition(confirmTransition)}
          onCancel={() => setConfirmTransition(null)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Factuur verwijderen"
          message={`Weet je zeker dat je factuur ${invoice.invoiceNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
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
  const { showSuccess, showError } = useToast()
  const [attachments, setAttachments] = useState<InvoiceAttachment[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [removeTarget, setRemoveTarget] = useState<InvoiceAttachment | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    listInvoiceAttachments(invoiceId)
      .then((data) => {
        setAttachments(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De bijlagen konden niet worden geladen.'))
  }, [invoiceId])

  useEffect(() => {
    reload()
  }, [reload])

  async function handleUpload(file: File) {
    if (file.size > MAX_INVOICE_ATTACHMENT_BYTES) {
      showError('Het bestand is te groot (maximaal 10 MB).')
      return
    }
    setBusy(true)
    try {
      await uploadInvoiceAttachment(invoiceId, file)
      showSuccess('Bijlage geüpload.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De bijlage kon niet worden geüpload.').message)
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
      showError(describeApiError(err, 'De bijlage kon niet worden bijgewerkt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="inv-section">
      <div className="inv-manual-head">
        <h3>Bijlagen</h3>
        {canManage && (
          <span className="inv-attachment-upload">
            <input
              ref={fileInputRef}
              type="file"
              accept={INVOICE_ATTACHMENT_ACCEPT}
              disabled={busy}
              aria-label="Bijlage kiezen"
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (file) void handleUpload(file)
              }}
            />
          </span>
        )}
      </div>

      {loadError && (
        <p className="inv-override-error" role="alert">
          {loadError}
        </p>
      )}
      {!loadError && attachments !== null && attachments.length === 0 && (
        <p className="placeholder-text">Nog geen bijlagen.</p>
      )}
      {attachments !== null && attachments.length > 0 && (
        <table className="inv-lines-table">
          <thead>
            <tr>
              <th>Bestand</th>
              <th>Grootte</th>
              <th>Geüpload op</th>
              <th>Meesturen</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {attachments.map((attachment) => (
              <tr key={attachment.id}>
                <td>{attachment.fileName}</td>
                <td>{formatFileSize(attachment.sizeBytes)}</td>
                <td>{attachment.uploadedAt.slice(0, 10)}</td>
                <td>
                  <label className="inv-attachment-toggle">
                    <input
                      type="checkbox"
                      checked={attachment.includeWhenSending}
                      onChange={() => void toggleInclude(attachment)}
                      disabled={!canManage || busy}
                    />
                    <span>{attachment.includeWhenSending ? 'Ja' : 'Nee'}</span>
                  </label>
                </td>
                <td>
                  <button
                    type="button"
                    className="inv-link"
                    onClick={() =>
                      void downloadInvoiceAttachment(invoiceId, attachment.id, attachment.fileName).catch(() =>
                        showError('De bijlage kon niet worden gedownload.'),
                      )
                    }
                  >
                    Download
                  </button>
                  {isDraft && canManage && (
                    <button type="button" className="inv-link inv-link-danger" onClick={() => setRemoveTarget(attachment)} disabled={busy}>
                      Verwijderen
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
          title="Bijlage verwijderen"
          message={`Bijlage '${removeTarget.fileName}' verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteInvoiceAttachment(invoiceId, removeTarget.id)
              showSuccess('Bijlage verwijderd.')
              setRemoveTarget(null)
              reload()
            } catch (err) {
              showError(describeApiError(err, 'De bijlage kon niet worden verwijderd.').message)
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
