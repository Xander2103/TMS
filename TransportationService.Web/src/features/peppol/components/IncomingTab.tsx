import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { euro } from '../../invoices/types'
import {
  listPeppolIncoming,
  rejectPeppolIncoming,
  reviewPeppolIncoming,
  PEPPOL_INCOMING_STATUS_LABELS,
  PEPPOL_INCOMING_STATUS_TONE,
  type PeppolIncomingDocument,
  type PeppolIncomingStatus,
} from '../api/peppolApi'
import { formatDateTime } from '../../../utils/dates'

const PAGE_SIZE = 25

const KIND_LABELS: Record<string, string> = {
  SupplierInvoice: 'Leveranciersfactuur',
  SupplierCreditNote: 'Leverancierscreditnota',
  StatusMessage: 'Statusbericht',
}

interface DecisionState {
  document: PeppolIncomingDocument
  action: 'review' | 'reject'
}

/** "Inkomend": via Peppol ontvangen documenten met verwerk/afwijs-acties (peppol.view_incoming). */
export function IncomingTab() {
  const { showSuccess, showError } = useToast()
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [result, setResult] = useState<{
    items: PeppolIncomingDocument[]
    totalCount: number
    error: string | null
    loadedKey: string
  }>({ items: [], totalCount: 0, error: null, loadedKey: '' })
  const [decision, setDecision] = useState<DecisionState | null>(null)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)

  const requestKey = JSON.stringify({ status, page, reloadToken })

  useEffect(() => {
    let mounted = true
    listPeppolIncoming({ status: status || undefined, page, pageSize: PAGE_SIZE })
      .then((data) => {
        if (!mounted) return
        setResult({ items: data.items, totalCount: data.totalCount, error: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setResult((current) => ({
          ...current,
          error: 'De inkomende Peppol-documenten konden niet worden geladen.',
          loadedKey: requestKey,
        }))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const { items, totalCount, error: loadError } = result
  const isLoading = result.loadedKey !== requestKey

  function openDecision(document: PeppolIncomingDocument, action: 'review' | 'reject') {
    setNote('')
    setDecision({ document, action })
  }

  async function confirmDecision() {
    if (!decision) return
    const trimmed = note.trim() || null
    setBusy(true)
    try {
      if (decision.action === 'review') {
        await reviewPeppolIncoming(decision.document.id, trimmed)
        showSuccess('Document gemarkeerd als verwerkt.')
      } else {
        await rejectPeppolIncoming(decision.document.id, trimmed)
        showSuccess('Document afgewezen.')
      }
      setDecision(null)
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De actie kon niet worden uitgevoerd.').message)
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<PeppolIncomingDocument>[] = [
    {
      key: 'supplier',
      header: 'Leverancier',
      render: (r) => r.supplierName ?? <code>{r.supplierParticipant}</code>,
    },
    { key: 'documentNumber', header: 'Documentnr', render: (r) => r.documentNumber },
    { key: 'kind', header: 'Soort', render: (r) => KIND_LABELS[r.documentKind] ?? r.documentKind },
    {
      key: 'amount',
      header: 'Bedrag',
      align: 'right',
      render: (r) => (r.amount != null ? euro(r.amount, r.currency ?? 'EUR') : '—'),
    },
    { key: 'receivedAt', header: 'Ontvangen', render: (r) => formatDateTime(r.receivedAt) },
    {
      key: 'status',
      header: 'Status',
      render: (r) => (
        <Badge tone={PEPPOL_INCOMING_STATUS_TONE[r.status] ?? 'neutral'}>
          {PEPPOL_INCOMING_STATUS_LABELS[r.status] ?? r.status}
        </Badge>
      ),
    },
    { key: 'note', header: 'Notitie', render: (r) => r.reviewNote ?? '—' },
    {
      key: 'actions',
      header: 'Acties',
      render: (r) => (
        <span className="edi-row-actions">
          {(r.status === 'Received' || r.status === 'NeedsReview') && (
            <>
              <Button variant="ghost" onClick={() => openDecision(r, 'review')}>
                Markeer als verwerkt
              </Button>
              <Button variant="ghost" onClick={() => openDecision(r, 'reject')}>
                Afwijzen
              </Button>
            </>
          )}
        </span>
      ),
    },
  ]

  return (
    <div>
      <div className="ui-filter-bar">
        <select
          className="ui-filter-select"
          aria-label="Status"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value)
            setPage(1)
          }}
        >
          <option value="">Alle statussen</option>
          {(Object.keys(PEPPOL_INCOMING_STATUS_LABELS) as PeppolIncomingStatus[]).map((s) => (
            <option key={s} value={s}>
              {PEPPOL_INCOMING_STATUS_LABELS[s]}
            </option>
          ))}
        </select>
      </div>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={loadError}
        emptyMessage="Geen inkomende Peppol-documenten gevonden."
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />

      {decision && (
        <Modal
          title={decision.action === 'review' ? 'Markeer als verwerkt' : 'Document afwijzen'}
          onClose={() => setDecision(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDecision(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button
                variant={decision.action === 'reject' ? 'danger' : 'primary'}
                onClick={() => void confirmDecision()}
                disabled={busy}
              >
                {busy ? 'Bezig…' : decision.action === 'review' ? 'Markeer als verwerkt' : 'Afwijzen'}
              </Button>
            </>
          }
        >
          <p>
            {decision.action === 'review'
              ? `Document ${decision.document.documentNumber} markeren als verwerkt?`
              : `Document ${decision.document.documentNumber} afwijzen?`}
          </p>
          <label className="peppol-note-label">
            Notitie (optioneel)
            <textarea
              rows={3}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              disabled={busy}
              maxLength={1000}
            />
          </label>
        </Modal>
      )}
    </div>
  )
}
