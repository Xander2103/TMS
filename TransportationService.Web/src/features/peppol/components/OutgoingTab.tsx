import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import {
  cancelPeppolTransmission,
  listPeppolTransmissions,
  retryPeppolTransmission,
  PEPPOL_KIND_LABEL_KEYS,
  PEPPOL_STATUS_LABEL_KEYS,
  peppolStatusLabel,
  peppolStatusTone,
  type PeppolTransmissionRow,
  type PeppolTransmissionStatus,
} from '../api/peppolApi'

const PAGE_SIZE = 25

interface OutgoingTabProps {
  /** "Opnieuw" op Failed/Rejected-rijen (peppol.retry). */
  canRetry: boolean
  /** "Annuleren" op Queued-rijen (peppol.send). */
  canCancel: boolean
}

/** "Uitgaand": de Peppol-outbox met status/zoekfilter, retry en annulering. */
export function OutgoingTab({ canRetry, canCancel }: OutgoingTabProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  // Foutstates als vertaalsleutel; vertaling gebeurt pas bij render.
  const [result, setResult] = useState<{
    items: PeppolTransmissionRow[]
    totalCount: number
    errorKey: string | null
    loadedKey: string
  }>({ items: [], totalCount: 0, errorKey: null, loadedKey: '' })
  const [busyId, setBusyId] = useState<string | null>(null)
  const [cancelTarget, setCancelTarget] = useState<PeppolTransmissionRow | null>(null)

  const requestKey = JSON.stringify({ status, search: search.trim(), page, reloadToken })

  useEffect(() => {
    let mounted = true
    listPeppolTransmissions({
      status: status || undefined,
      search: search.trim() || undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((data) => {
        if (!mounted) return
        setResult({ items: data.items, totalCount: data.totalCount, errorKey: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setResult((current) => ({
          ...current,
          errorKey: 'peppol.outgoing.loadFailed',
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

  async function retry(id: string) {
    setBusyId(id)
    try {
      await retryPeppolTransmission(id)
      showSuccess(t('peppol.outgoing.retryQueued'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('peppol.outgoing.retryFailed')))
    } finally {
      setBusyId(null)
    }
  }

  async function cancel(row: PeppolTransmissionRow) {
    setBusyId(row.id)
    try {
      await cancelPeppolTransmission(row.id)
      showSuccess(t('peppol.outgoing.cancelled'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('peppol.outgoing.cancelFailed')))
    } finally {
      setBusyId(null)
      setCancelTarget(null)
    }
  }

  const columns: Column<PeppolTransmissionRow>[] = [
    { key: 'invoice', header: t('peppol.outgoing.colInvoice'), render: (r) => r.invoiceNumber },
    {
      key: 'kind',
      header: t('peppol.outgoing.colKind'),
      render: (r) => (PEPPOL_KIND_LABEL_KEYS[r.documentKind] ? t(PEPPOL_KIND_LABEL_KEYS[r.documentKind]) : r.documentKind),
    },
    { key: 'customer', header: t('peppol.outgoing.colCustomer'), render: (r) => r.customerName },
    { key: 'total', header: t('peppol.outgoing.colAmount'), align: 'right', render: (r) => euro(r.total, r.currency) },
    { key: 'date', header: t('peppol.outgoing.colDate'), render: (r) => r.invoiceDate },
    { key: 'environment', header: t('peppol.outgoing.colEnvironment'), render: (r) => r.environment },
    {
      key: 'status',
      header: t('peppol.outgoing.colStatus'),
      render: (r) => <Badge tone={peppolStatusTone(r.status)}>{peppolStatusLabel(t, r.status)}</Badge>,
    },
    {
      key: 'providerMessageId',
      header: t('peppol.outgoing.colProviderRef'),
      render: (r) => (r.providerMessageId ? <code>{r.providerMessageId}</code> : '—'),
    },
    {
      key: 'error',
      header: t('peppol.outgoing.colError'),
      render: (r) => (
        <span className="edi-error" title={r.errorDetail ?? undefined}>
          {r.errorDetail ?? '—'}
        </span>
      ),
    },
    {
      key: 'attempts',
      header: t('peppol.outgoing.colAttempts'),
      render: (r) => `${r.retryCount + 1}× / v${r.payloadVersion}`,
    },
    {
      key: 'actions',
      header: t('peppol.outgoing.colActions'),
      render: (r) => (
        <span className="edi-row-actions">
          {canRetry && (r.status === 'Failed' || r.status === 'Rejected') && (
            <Button variant="ghost" disabled={busyId === r.id} onClick={() => void retry(r.id)}>
              {t('peppol.outgoing.retry')}
            </Button>
          )}
          {canCancel && r.status === 'Queued' && (
            <Button variant="ghost" disabled={busyId === r.id} onClick={() => setCancelTarget(r)}>
              {t('ui.actions.cancel')}
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
        searchPlaceholder={t('peppol.outgoing.searchPlaceholder')}
      >
        <select
          className="ui-filter-select"
          aria-label={t('peppol.outgoing.statusFilterLabel')}
          value={status}
          onChange={(e) => {
            setStatus(e.target.value)
            setPage(1)
          }}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          {/* Transmissies ontstaan als Queued; Draft/Validated bestaan alleen als enum-waarden. */}
          {(Object.keys(PEPPOL_STATUS_LABEL_KEYS) as PeppolTransmissionStatus[])
            .filter((s) => s !== 'Draft' && s !== 'Validated')
            .map((s) => (
              <option key={s} value={s}>
                {t(PEPPOL_STATUS_LABEL_KEYS[s])}
              </option>
            ))}
        </select>
      </FilterBar>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={loadErrorKey ? t(loadErrorKey) : null}
        emptyMessage={t('peppol.outgoing.empty')}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />

      {cancelTarget && (
        <ConfirmDialog
          title={t('peppol.outgoing.cancelTitle')}
          message={t('peppol.outgoing.cancelMessage', { invoiceNumber: cancelTarget.invoiceNumber })}
          confirmLabel={t('ui.actions.cancel')}
          destructive
          busy={busyId === cancelTarget.id}
          onConfirm={() => void cancel(cancelTarget)}
          onCancel={() => setCancelTarget(null)}
        />
      )}
    </div>
  )
}
