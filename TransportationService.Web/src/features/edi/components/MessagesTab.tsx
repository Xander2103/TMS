import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  listMessages,
  listPartners,
  replayMessage,
  STATUS_LABELS,
  STATUS_TONE,
  type EdiDirection,
  type EdiMessageRow,
  type EdiPartner,
  type EdiStatus,
} from '../api/ediApi'
import { formatDateTime } from '../../../utils/dates'
import { MessageDetailModal } from './MessageDetailModal'

const PAGE_SIZE = 25

interface MessagesTabProps {
  /** Whether the "Opnieuw" action is shown (edi.retry or edi.manage). */
  canRetry: boolean
}

/** "Berichten" tab: the EDI inbox/outbox with direction/status/partner filters, search on the
 * external reference, and replay for Failed/DeadLettered rows. */
export function MessagesTab({ canRetry }: MessagesTabProps) {
  const { showSuccess, showError } = useToast()
  const [direction, setDirection] = useState<EdiDirection | ''>('')
  const [status, setStatus] = useState<EdiStatus | ''>('')
  const [partnerId, setPartnerId] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [partners, setPartners] = useState<EdiPartner[]>([])
  const [result, setResult] = useState<{ items: EdiMessageRow[]; totalCount: number; error: string | null; loadedKey: string }>({
    items: [],
    totalCount: 0,
    error: null,
    loadedKey: '',
  })
  const [detailId, setDetailId] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const requestKey = JSON.stringify({ direction, status, partnerId, search: search.trim(), page, reloadToken })

  useEffect(() => {
    let mounted = true
    listMessages({
      direction: direction || undefined,
      status: status || undefined,
      partnerId: partnerId || undefined,
      search: search.trim() || undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((data) => {
        if (!mounted) return
        setResult({ items: data.items, totalCount: data.totalCount, error: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setResult((current) => ({ ...current, error: 'De EDI-berichten konden niet worden geladen.', loadedKey: requestKey }))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  useEffect(() => {
    listPartners()
      .then(setPartners)
      .catch(() => {})
  }, [])

  const { items, totalCount, error: loadError } = result
  const isLoading = result.loadedKey !== requestKey

  async function replay(id: string) {
    setBusyId(id)
    try {
      await replayMessage(id)
      showSuccess('Bericht opnieuw verwerkt.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De verwerking mislukte opnieuw.').message)
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<EdiMessageRow>[] = [
    { key: 'createdAt', header: 'Ontvangen/verzonden', render: (r) => formatDateTime(r.createdAt) },
    { key: 'direction', header: 'Richting', render: (r) => (r.direction === 'Inbound' ? '↓ in' : '↑ uit') },
    { key: 'partner', header: 'Partner', render: (r) => <code>{r.partnerCode}</code> },
    { key: 'type', header: 'Type', render: (r) => r.messageType },
    { key: 'externalReference', header: 'Externe referentie', render: (r) => r.externalReference ?? '—' },
    {
      key: 'status',
      header: 'Status',
      render: (r) => (
        <>
          <Badge tone={STATUS_TONE[r.status]}>{STATUS_LABELS[r.status]}</Badge>
          {r.mappingIssue && <Badge tone="warning">mapping</Badge>}
          {r.attemptCount > 1 && ` (${r.attemptCount}×)`}
        </>
      ),
    },
    {
      key: 'error',
      header: 'Fout',
      render: (r) => (
        <span className="edi-error" title={r.errorDetail ?? undefined}>
          {r.errorDetail ?? '—'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Acties',
      render: (r) => (
        <span className="edi-row-actions">
          <Button variant="ghost" onClick={() => setDetailId(r.id)}>
            Detail
          </Button>
          {canRetry && (r.status === 'Failed' || r.status === 'DeadLettered') && (
            <Button variant="ghost" disabled={busyId === r.id} onClick={() => void replay(r.id)}>
              Opnieuw
            </Button>
          )}
        </span>
      ),
    },
  ]

  return (
    <div>
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoeken op externe referentie..."
      >
        <select
          className="ui-filter-select"
          aria-label="Richting"
          value={direction}
          onChange={(e) => {
            setDirection(e.target.value as EdiDirection | '')
            setPage(1)
          }}
        >
          <option value="">Alle richtingen</option>
          <option value="Inbound">Inkomend</option>
          <option value="Outbound">Uitgaand</option>
        </select>
        <select
          className="ui-filter-select"
          aria-label="Status"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as EdiStatus | '')
            setPage(1)
          }}
        >
          <option value="">Alle statussen</option>
          {(Object.keys(STATUS_LABELS) as EdiStatus[]).map((s) => (
            <option key={s} value={s}>
              {STATUS_LABELS[s]}
            </option>
          ))}
        </select>
        <select
          className="ui-filter-select"
          aria-label="Partner"
          value={partnerId}
          onChange={(e) => {
            setPartnerId(e.target.value)
            setPage(1)
          }}
        >
          <option value="">Alle partners</option>
          {partners.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </FilterBar>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={loadError}
        emptyMessage="Geen EDI-berichten gevonden."
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />

      {detailId && (
        <MessageDetailModal
          id={detailId}
          canRetry={canRetry}
          onClose={() => setDetailId(null)}
          onReplayed={() => {
            setDetailId(null)
            setReloadToken((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}
