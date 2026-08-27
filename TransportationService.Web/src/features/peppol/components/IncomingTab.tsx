import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import {
  listPeppolIncoming,
  rejectPeppolIncoming,
  reviewPeppolIncoming,
  PEPPOL_INCOMING_STATUS_LABEL_KEYS,
  PEPPOL_INCOMING_STATUS_TONE,
  type PeppolIncomingDocument,
  type PeppolIncomingStatus,
} from '../api/peppolApi'
import { formatDateTime } from '../../../utils/dates'

const PAGE_SIZE = 25

/** Vertaalsleutels per documentKind — renderen als t(KIND_LABEL_KEYS[kind]). */
const KIND_LABEL_KEYS: Record<string, string> = {
  SupplierInvoice: 'peppol.incomingKind.SupplierInvoice',
  SupplierCreditNote: 'peppol.incomingKind.SupplierCreditNote',
  StatusMessage: 'peppol.incomingKind.StatusMessage',
}

interface DecisionState {
  document: PeppolIncomingDocument
  action: 'review' | 'reject'
}

/** "Inkomend": via Peppol ontvangen documenten met verwerk/afwijs-acties (peppol.view_incoming). */
export function IncomingTab() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  // Foutstates als vertaalsleutel; vertaling gebeurt pas bij render.
  const [result, setResult] = useState<{
    items: PeppolIncomingDocument[]
    totalCount: number
    errorKey: string | null
    loadedKey: string
  }>({ items: [], totalCount: 0, errorKey: null, loadedKey: '' })
  const [decision, setDecision] = useState<DecisionState | null>(null)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)

  const requestKey = JSON.stringify({ status, page, reloadToken })

  useEffect(() => {
    let mounted = true
    listPeppolIncoming({ status: status || undefined, page, pageSize: PAGE_SIZE })
      .then((data) => {
        if (!mounted) return
        setResult({ items: data.items, totalCount: data.totalCount, errorKey: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setResult((current) => ({
          ...current,
          errorKey: 'peppol.incoming.loadFailed',
          loadedKey: requestKey,
        }))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const { items, totalCount, errorKey: loadErrorKey } = result
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
        showSuccess(t('peppol.incoming.reviewed'))
      } else {
        await rejectPeppolIncoming(decision.document.id, trimmed)
        showSuccess(t('peppol.incoming.rejected'))
      }
      setDecision(null)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('peppol.incoming.actionFailed')))
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<PeppolIncomingDocument>[] = [
    {
      key: 'supplier',
      header: t('peppol.incoming.colSupplier'),
      render: (r) => r.supplierName ?? <code>{r.supplierParticipant}</code>,
    },
    { key: 'documentNumber', header: t('peppol.incoming.colDocumentNumber'), render: (r) => r.documentNumber },
    {
      key: 'kind',
      header: t('peppol.incoming.colKind'),
      render: (r) => (KIND_LABEL_KEYS[r.documentKind] ? t(KIND_LABEL_KEYS[r.documentKind]) : r.documentKind),
    },
    {
      key: 'amount',
      header: t('peppol.incoming.colAmount'),
      align: 'right',
      render: (r) => (r.amount != null ? euro(r.amount, r.currency ?? 'EUR') : '—'),
    },
    { key: 'receivedAt', header: t('peppol.incoming.colReceivedAt'), render: (r) => formatDateTime(r.receivedAt) },
    {
      key: 'status',
      header: t('peppol.incoming.colStatus'),
      render: (r) => (
        <Badge tone={PEPPOL_INCOMING_STATUS_TONE[r.status] ?? 'neutral'}>
          {PEPPOL_INCOMING_STATUS_LABEL_KEYS[r.status] ? t(PEPPOL_INCOMING_STATUS_LABEL_KEYS[r.status]) : r.status}
        </Badge>
      ),
    },
    { key: 'note', header: t('peppol.incoming.colNote'), render: (r) => r.reviewNote ?? '—' },
    {
      key: 'actions',
      header: t('peppol.incoming.colActions'),
      render: (r) => (
        <span className="edi-row-actions">
          {(r.status === 'Received' || r.status === 'NeedsReview') && (
            <>
              <Button variant="ghost" onClick={() => openDecision(r, 'review')}>
                {t('peppol.incoming.markReviewed')}
              </Button>
              <Button variant="ghost" onClick={() => openDecision(r, 'reject')}>
                {t('peppol.incoming.reject')}
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
          aria-label={t('peppol.incoming.statusFilterLabel')}
          value={status}
          onChange={(e) => {
            setStatus(e.target.value)
            setPage(1)
          }}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          {(Object.keys(PEPPOL_INCOMING_STATUS_LABEL_KEYS) as PeppolIncomingStatus[]).map((s) => (
            <option key={s} value={s}>
              {t(PEPPOL_INCOMING_STATUS_LABEL_KEYS[s])}
            </option>
          ))}
        </select>
      </div>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={loadErrorKey ? t(loadErrorKey) : null}
        emptyMessage={t('peppol.incoming.empty')}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />

      {decision && (
        <Modal
          title={decision.action === 'review' ? t('peppol.incoming.markReviewed') : t('peppol.incoming.rejectTitle')}
          onClose={() => setDecision(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDecision(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button
                variant={decision.action === 'reject' ? 'danger' : 'primary'}
                onClick={() => void confirmDecision()}
                disabled={busy}
              >
                {busy
                  ? t('peppol.incoming.busy')
                  : decision.action === 'review'
                    ? t('peppol.incoming.markReviewed')
                    : t('peppol.incoming.reject')}
              </Button>
            </>
          }
        >
          <p>
            {decision.action === 'review'
              ? t('peppol.incoming.confirmReview', { documentNumber: decision.document.documentNumber })
              : t('peppol.incoming.confirmReject', { documentNumber: decision.document.documentNumber })}
          </p>
          <label className="peppol-note-label">
            {t('peppol.incoming.noteLabel')}
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
