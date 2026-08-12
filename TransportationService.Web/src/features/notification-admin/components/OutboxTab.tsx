import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { listOutbox, rejectOutboxMessage, releaseOutboxMessage, retryOutboxMessage } from '../api/notificationAdminApi'
import { kindLabel, type MessageChannel, type OutboxRow, type OutboxStatus } from '../types'

const PAGE_SIZE = 25

const CHANNEL_LABELS: Record<MessageChannel, string> = { Email: 'E-mail', Sms: 'SMS' }

/** Routes with a known detail page; other related-entity types render as plain text. */
const RELATED_ENTITY_ROUTES: Record<string, string> = {
  TransportOrder: '/transport-orders',
  Invoice: '/invoices',
}

function RelatedEntityCell({ type, id }: { type: string | null; id: string | null }) {
  if (!type || !id) return <span className="notification-admin-muted">—</span>
  const base = RELATED_ENTITY_ROUTES[type]
  if (!base) return <span>{type} · {id}</span>
  return <Link to={`${base}/${id}`}>{type} · {id}</Link>
}

interface OutboxTabProps {
  /** 'sent' shows the delivered-message columns; 'failed' shows failure reason + retry;
   * 'review' (P9) shows review-held messages with release/reject actions. */
  variant: 'sent' | 'failed' | 'review'
  /** Failed tab only: lets the user flip between genuinely failed and deliberately suppressed rows. */
  includeSuppressedToggle?: boolean
}

/** Shared outbox table for "Verzonden berichten" (Status=Sent), "Mislukte berichten"
 * (Status=Failed, with a toggle to Suppressed) and "Wacht op controle"
 * (Status=AwaitingReview) — same filters, different columns. */
export function OutboxTab({ variant, includeSuppressedToggle = false }: OutboxTabProps) {
  const { showSuccess, showError } = useToast()
  const [search, setSearch] = useState('')
  const [channel, setChannel] = useState<MessageChannel | ''>('')
  const [kind, setKind] = useState('')
  const [suppressedOnly, setSuppressedOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [result, setResult] = useState<{ items: OutboxRow[]; totalCount: number; error: string | null; loadedKey: string }>({
    items: [],
    totalCount: 0,
    error: null,
    loadedKey: '',
  })

  const [rejectTargetId, setRejectTargetId] = useState<string | null>(null)
  const [rejectReason, setRejectReason] = useState('')

  const status: OutboxStatus =
    variant === 'sent' ? 'Sent' : variant === 'review' ? 'AwaitingReview' : suppressedOnly ? 'Suppressed' : 'Failed'
  // Identifies the current request; isLoading is derived from whether the loaded result matches
  // it, so state is only ever mutated inside the async .then/.catch callbacks below (never
  // synchronously in the effect body) — mirrors hooks/usePagedQuery.ts.
  const requestKey = JSON.stringify({ status, kind, channel, search: search.trim(), page, reloadToken })

  useEffect(() => {
    let mounted = true
    listOutbox({
      status,
      kind: kind || undefined,
      channel: channel || undefined,
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
        setResult((current) => ({ ...current, error: 'De berichten konden niet worden geladen.', loadedKey: requestKey }))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const { items, totalCount, error: loadError } = result
  const isLoading = result.loadedKey !== requestKey

  async function retry(id: string) {
    try {
      await retryOutboxMessage(id)
      showSuccess('Bericht opnieuw in de wachtrij gezet.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'Opnieuw verzenden is mislukt.').message)
    }
  }

  async function release(id: string) {
    try {
      await releaseOutboxMessage(id)
      showSuccess('Bericht vrijgegeven — het wordt verzonden.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'Vrijgeven is mislukt.').message)
    }
  }

  async function confirmReject(id: string) {
    try {
      await rejectOutboxMessage(id, rejectReason.trim() || null)
      showSuccess('Bericht afgewezen — het wordt niet verzonden.')
      setRejectTargetId(null)
      setRejectReason('')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'Afwijzen is mislukt.').message)
    }
  }

  const baseColumns: Column<OutboxRow>[] = [
    { key: 'createdAt', header: 'Datum', render: (r) => r.createdAt.slice(0, 16).replace('T', ' ') },
    { key: 'channel', header: 'Kanaal', render: (r) => CHANNEL_LABELS[r.channel] },
    { key: 'kind', header: 'Soort', render: (r) => kindLabel(r.kind) },
    {
      key: 'recipient',
      header: 'Ontvanger',
      render: (r) => (r.recipientName ? `${r.recipientName} · ${r.recipientAddress}` : r.recipientAddress),
    },
  ]

  const columns: Column<OutboxRow>[] =
    variant === 'sent'
      ? [
          ...baseColumns,
          { key: 'subject', header: 'Onderwerp', render: (r) => r.subject ?? '—' },
          {
            key: 'related',
            header: 'Gekoppelde entiteit',
            render: (r) => <RelatedEntityCell type={r.relatedEntityType} id={r.relatedEntityId} />,
          },
        ]
      : variant === 'review'
      ? [
          ...baseColumns,
          { key: 'subject', header: 'Onderwerp', render: (r) => r.subject ?? '—' },
          {
            key: 'related',
            header: 'Gekoppelde entiteit',
            render: (r) => <RelatedEntityCell type={r.relatedEntityType} id={r.relatedEntityId} />,
          },
          {
            key: 'actions',
            header: '',
            render: (r) =>
              rejectTargetId === r.id ? (
                <span className="notification-admin-inline-field">
                  <input
                    aria-label={`Reden van afwijzing voor ${r.recipientAddress}`}
                    placeholder="Reden van afwijzing"
                    value={rejectReason}
                    onChange={(e) => setRejectReason(e.target.value)}
                  />
                  <Button variant="ghost" onClick={() => void confirmReject(r.id)}>
                    Bevestig afwijzen
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setRejectTargetId(null)
                      setRejectReason('')
                    }}
                  >
                    Annuleren
                  </Button>
                </span>
              ) : (
                <>
                  <Button variant="ghost" onClick={() => void release(r.id)}>
                    Vrijgeven
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setRejectTargetId(r.id)
                      setRejectReason('')
                    }}
                  >
                    Afwijzen
                  </Button>
                </>
              ),
          },
        ]
      : [
          ...baseColumns,
          {
            key: 'failure',
            header: 'Foutreden',
            render: (r) => (
              <span title={r.failureReason ?? undefined}>
                {r.failureReason ?? (r.status === 'Suppressed' ? 'Onderdrukt (voorkeur/opt-out)' : '—')}
              </span>
            ),
          },
          { key: 'attempts', header: 'Pogingen', align: 'right', render: (r) => r.attemptCount },
          {
            key: 'actions',
            header: '',
            render: (r) => (
              <Button variant="ghost" onClick={() => void retry(r.id)}>
                Opnieuw proberen
              </Button>
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
        searchPlaceholder="Zoeken op ontvanger..."
      >
        <select
          className="ui-filter-select"
          aria-label="Kanaal"
          value={channel}
          onChange={(e) => {
            setChannel(e.target.value as MessageChannel | '')
            setPage(1)
          }}
        >
          <option value="">Alle kanalen</option>
          <option value="Email">E-mail</option>
          <option value="Sms">SMS</option>
        </select>
        <input
          className="ui-filter-select"
          aria-label="Soort"
          placeholder="Filter op soort (code)"
          value={kind}
          onChange={(e) => {
            setKind(e.target.value)
            setPage(1)
          }}
        />
        {includeSuppressedToggle && (
          <label className="notification-admin-inline-toggle">
            <input
              type="checkbox"
              checked={suppressedOnly}
              onChange={(e) => {
                setSuppressedOnly(e.target.checked)
                setPage(1)
              }}
            />
            Onderdrukte berichten tonen
            {suppressedOnly && <Badge tone="neutral">onderdrukt</Badge>}
          </label>
        )}
      </FilterBar>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={loadError}
        emptyMessage="Geen berichten gevonden."
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
