import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { euro } from '../../invoices/types'
import {
  cancelPeppolTransmission,
  listPeppolTransmissions,
  retryPeppolTransmission,
  PEPPOL_KIND_LABELS,
  PEPPOL_STATUS_LABELS,
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
  const { showSuccess, showError } = useToast()
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [result, setResult] = useState<{
    items: PeppolTransmissionRow[]
    totalCount: number
    error: string | null
    loadedKey: string
  }>({ items: [], totalCount: 0, error: null, loadedKey: '' })
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
        setResult({ items: data.items, totalCount: data.totalCount, error: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setResult((current) => ({
          ...current,
          error: 'De Peppol-transmissies konden niet worden geladen.',
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

  async function retry(id: string) {
    setBusyId(id)
    try {
      await retryPeppolTransmission(id)
      showSuccess('Transmissie opnieuw in de wachtrij gezet.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De transmissie kon niet opnieuw worden aangeboden.').message)
    } finally {
      setBusyId(null)
    }
  }

  async function cancel(row: PeppolTransmissionRow) {
    setBusyId(row.id)
    try {
      await cancelPeppolTransmission(row.id)
      showSuccess('Transmissie geannuleerd.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De transmissie kon niet worden geannuleerd.').message)
    } finally {
      setBusyId(null)
      setCancelTarget(null)
    }
  }

  const columns: Column<PeppolTransmissionRow>[] = [
    { key: 'invoice', header: 'Factuur', render: (r) => r.invoiceNumber },
    { key: 'kind', header: 'Type', render: (r) => PEPPOL_KIND_LABELS[r.documentKind] ?? r.documentKind },
    { key: 'customer', header: 'Klant', render: (r) => r.customerName },
    { key: 'total', header: 'Bedrag', align: 'right', render: (r) => euro(r.total, r.currency) },
    { key: 'date', header: 'Datum', render: (r) => r.invoiceDate },
    { key: 'environment', header: 'Omgeving', render: (r) => r.environment },
    {
      key: 'status',
      header: 'Status',
      render: (r) => <Badge tone={peppolStatusTone(r.status)}>{peppolStatusLabel(r.status)}</Badge>,
    },
    {
      key: 'providerMessageId',
      header: 'Provider-referentie',
      render: (r) => (r.providerMessageId ? <code>{r.providerMessageId}</code> : '—'),
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
      key: 'attempts',
      header: 'Poging/versie',
      render: (r) => `${r.retryCount + 1}× / v${r.payloadVersion}`,
    },
    {
      key: 'actions',
      header: 'Acties',
      render: (r) => (
        <span className="edi-row-actions">
          {canRetry && (r.status === 'Failed' || r.status === 'Rejected') && (
            <Button variant="ghost" disabled={busyId === r.id} onClick={() => void retry(r.id)}>
              Opnieuw
            </Button>
          )}
          {canCancel && r.status === 'Queued' && (
            <Button variant="ghost" disabled={busyId === r.id} onClick={() => setCancelTarget(r)}>
              Annuleren
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
        searchPlaceholder="Zoeken op factuurnummer of klant..."
      >
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
          {/* Transmissies ontstaan als Queued; Draft/Validated bestaan alleen als enum-waarden. */}
          {(Object.keys(PEPPOL_STATUS_LABELS) as PeppolTransmissionStatus[])
            .filter((s) => s !== 'Draft' && s !== 'Validated')
            .map((s) => (
              <option key={s} value={s}>
                {PEPPOL_STATUS_LABELS[s]}
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
        emptyMessage="Geen Peppol-transmissies gevonden."
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />

      {cancelTarget && (
        <ConfirmDialog
          title="Transmissie annuleren"
          message={`Weet je zeker dat je de Peppol-verzending van factuur ${cancelTarget.invoiceNumber} wilt annuleren?`}
          confirmLabel="Annuleren"
          destructive
          busy={busyId === cancelTarget.id}
          onConfirm={() => void cancel(cancelTarget)}
          onCancel={() => setCancelTarget(null)}
        />
      )}
    </div>
  )
}
