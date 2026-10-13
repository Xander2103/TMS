import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { listOutbox, rejectOutboxMessage, releaseOutboxMessage, retryOutboxMessage } from '../api/notificationAdminApi'
import { kindLabel, type MessageChannel, type OutboxRow, type OutboxStatus } from '../types'
import { formatDateTime } from '../../../utils/dates'

const PAGE_SIZE = 25

/** Translation keys per channel; render via t(CHANNEL_LABELS[channel]). */
const CHANNEL_LABELS: Record<MessageChannel, string> = {
  Email: 'notificationAdmin.outbox.channelEmail',
  Sms: 'notificationAdmin.outbox.channelSms',
}

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
  const { t } = useLocale()
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
        setResult((current) => ({ ...current, error: t('notificationAdmin.outbox.loadFailed'), loadedKey: requestKey }))
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
      showSuccess(t('notificationAdmin.outbox.retried'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('notificationAdmin.outbox.retryFailed')))
    }
  }

  async function release(id: string) {
    try {
      await releaseOutboxMessage(id)
      showSuccess(t('notificationAdmin.outbox.released'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('notificationAdmin.outbox.releaseFailed')))
    }
  }

  async function confirmReject(id: string) {
    try {
      await rejectOutboxMessage(id, rejectReason.trim() || null)
      showSuccess(t('notificationAdmin.outbox.rejected'))
      setRejectTargetId(null)
      setRejectReason('')
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('notificationAdmin.outbox.rejectFailed')))
    }
  }

  const baseColumns: Column<OutboxRow>[] = [
    { key: 'createdAt', header: t('notificationAdmin.outbox.columns.date'), render: (r) => formatDateTime(r.createdAt) },
    { key: 'channel', header: t('notificationAdmin.outbox.columns.channel'), render: (r) => t(CHANNEL_LABELS[r.channel]) },
    { key: 'kind', header: t('notificationAdmin.outbox.columns.kind'), render: (r) => kindLabel(t, r.kind) },
    {
      key: 'recipient',
      header: t('notificationAdmin.outbox.columns.recipient'),
      render: (r) => (r.recipientName ? `${r.recipientName} · ${r.recipientAddress}` : r.recipientAddress),
    },
  ]

  const columns: Column<OutboxRow>[] =
    variant === 'sent'
      ? [
          ...baseColumns,
          { key: 'subject', header: t('notificationAdmin.outbox.columns.subject'), render: (r) => r.subject ?? '—' },
          {
            key: 'related',
            header: t('notificationAdmin.outbox.columns.related'),
            render: (r) => <RelatedEntityCell type={r.relatedEntityType} id={r.relatedEntityId} />,
          },
        ]
      : variant === 'review'
      ? [
          ...baseColumns,
          { key: 'subject', header: t('notificationAdmin.outbox.columns.subject'), render: (r) => r.subject ?? '—' },
          {
            key: 'related',
            header: t('notificationAdmin.outbox.columns.related'),
            render: (r) => <RelatedEntityCell type={r.relatedEntityType} id={r.relatedEntityId} />,
          },
          {
            key: 'actions',
            header: '',
            render: (r) =>
              rejectTargetId === r.id ? (
                <span className="notification-admin-inline-field">
                  <input
                    aria-label={t('notificationAdmin.outbox.rejectReasonAria', { recipient: r.recipientAddress })}
                    placeholder={t('notificationAdmin.outbox.rejectReasonPlaceholder')}
                    value={rejectReason}
                    onChange={(e) => setRejectReason(e.target.value)}
                  />
                  <Button variant="ghost" onClick={() => void confirmReject(r.id)}>
                    {t('notificationAdmin.outbox.confirmReject')}
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setRejectTargetId(null)
                      setRejectReason('')
                    }}
                  >
                    {t('ui.actions.cancel')}
                  </Button>
                </span>
              ) : (
                <>
                  <Button variant="ghost" onClick={() => void release(r.id)}>
                    {t('notificationAdmin.outbox.release')}
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setRejectTargetId(r.id)
                      setRejectReason('')
                    }}
                  >
                    {t('notificationAdmin.outbox.reject')}
                  </Button>
                </>
              ),
          },
        ]
      : [
          ...baseColumns,
          {
            key: 'failure',
            header: t('notificationAdmin.outbox.columns.failure'),
            render: (r) => (
              <span title={r.failureReason ?? undefined}>
                {r.failureReason ?? (r.status === 'Suppressed' ? t('notificationAdmin.outbox.suppressedReason') : '—')}
              </span>
            ),
          },
          { key: 'attempts', header: t('notificationAdmin.outbox.columns.attempts'), align: 'right', render: (r) => r.attemptCount },
          {
            key: 'actions',
            header: '',
            render: (r) => (
              <Button variant="ghost" onClick={() => void retry(r.id)}>
                {t('notificationAdmin.outbox.retry')}
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
        searchPlaceholder={t('notificationAdmin.outbox.searchPlaceholder')}
      >
        <select
          className="ui-filter-select"
          aria-label={t('notificationAdmin.outbox.channelFilter')}
          value={channel}
          onChange={(e) => {
            setChannel(e.target.value as MessageChannel | '')
            setPage(1)
          }}
        >
          <option value="">{t('notificationAdmin.outbox.allChannels')}</option>
          <option value="Email">{t('notificationAdmin.outbox.channelEmail')}</option>
          <option value="Sms">{t('notificationAdmin.outbox.channelSms')}</option>
        </select>
        <input
          className="ui-filter-select"
          aria-label={t('notificationAdmin.outbox.kindFilter')}
          placeholder={t('notificationAdmin.outbox.kindPlaceholder')}
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
            {t('notificationAdmin.outbox.showSuppressed')}
            {suppressedOnly && <Badge tone="neutral">{t('notificationAdmin.outbox.suppressedBadge')}</Badge>}
          </label>
        )}
      </FilterBar>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={loadError}
        emptyMessage={t('notificationAdmin.outbox.empty')}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
