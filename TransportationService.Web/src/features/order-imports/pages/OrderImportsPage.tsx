import { useEffect, useState, type FormEvent } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import {
  getOrderImportBatch,
  listOrderImportBatches,
  listOrderImportProfiles,
  uploadOrderImport,
  type OrderImportBatch,
  type OrderImportBatchDetail,
  type OrderImportBatchStatus,
  type OrderImportProfile,
  type OrderImportRow,
  type OrderImportRowStatus,
} from '../api/orderImportsApi'
import { formatDateTime } from '../../../utils/dates'
import './order-imports.css'

const PAGE_SIZE = 25

const BATCH_STATUS_LABELS: Record<OrderImportBatchStatus, string> = {
  Validated: 'Gevalideerd',
  Processed: 'Verwerkt',
  Failed: 'Mislukt',
}

const BATCH_STATUS_TONE: Record<OrderImportBatchStatus, BadgeTone> = {
  Validated: 'info',
  Processed: 'success',
  Failed: 'danger',
}

const ROW_STATUS_LABELS: Record<OrderImportRowStatus, string> = {
  Created: 'Aangemaakt',
  Skipped: 'Overgeslagen',
  Error: 'Fout',
}

const ROW_STATUS_TONE: Record<OrderImportRowStatus, BadgeTone> = {
  Created: 'success',
  Skipped: 'warning',
  Error: 'danger',
}

/** P13: automated Excel order import — upload with dry run, batch history, per-row outcome. */
export function OrderImportsPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()

  const canView = hasPermission('orders.view') || hasPermission('orders.manage')
  const canUpload = hasPermission('orders.create') || hasPermission('orders.manage')

  const [page, setPage] = useState(1)
  const {
    items: batches,
    totalCount,
    isLoading,
    error: listError,
    reload,
  } = usePagedQuery<OrderImportBatch>(
    (args) =>
      canView
        ? listOrderImportBatches(args.page, args.pageSize)
        : Promise.resolve({ items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE }),
    { search: '', page, pageSize: PAGE_SIZE, errorMessage: 'De importgeschiedenis kon niet worden geladen.' },
  )

  // ---------------------------------------------------------------- upload form

  const [profiles, setProfiles] = useState<OrderImportProfile[]>([])
  const [profileId, setProfileId] = useState('')
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [customerSearch, setCustomerSearch] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [fileInputKey, setFileInputKey] = useState(0)
  const [dryRun, setDryRun] = useState(true)
  const [uploading, setUploading] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  useEffect(() => {
    if (!canUpload) return
    let mounted = true
    listOrderImportProfiles()
      .then((data) => {
        if (!mounted) return
        setProfiles(data)
        if (data.length > 0) setProfileId((current) => current || data[0].id)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [canUpload])

  useEffect(() => {
    if (!canUpload) return
    let mounted = true
    searchCustomers({ search: customerSearch || undefined, isActive: true, page: 1, pageSize: 20 })
      .then((result) => {
        if (mounted) setCustomers(result.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [canUpload, customerSearch])

  // ---------------------------------------------------------------- batch detail

  const [detail, setDetail] = useState<OrderImportBatchDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  async function openDetail(batch: OrderImportBatch) {
    setDetailLoading(true)
    try {
      setDetail(await getOrderImportBatch(batch.id))
    } catch {
      showError('De batchdetails konden niet worden geladen.')
    } finally {
      setDetailLoading(false)
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!profileId) {
      setFormError('Kies een importprofiel.')
      return
    }
    if (!customerId) {
      setFormError('Kies een klant.')
      return
    }
    if (!file) {
      setFormError('Kies een Excel-bestand (.xlsx).')
      return
    }
    setUploading(true)
    try {
      const result = await uploadOrderImport({ file, profileId, customerId, dryRun })
      setDetail(result)
      showSuccess(
        dryRun
          ? `Gevalideerd: ${result.batch.successCount} van ${result.batch.rowCount} rijen zijn in orde.`
          : `Verwerkt: ${result.batch.successCount} opdracht(en) aangemaakt, ${result.batch.failureCount} fout(en).`,
      )
      setFile(null)
      setFileInputKey((key) => key + 1)
      setPage(1)
      reload()
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'De import is mislukt.')
    } finally {
      setUploading(false)
    }
  }

  const columns: Column<OrderImportBatch>[] = [
    { key: 'fileName', header: 'Bestand', render: (row) => row.fileName },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName },
    { key: 'profile', header: 'Profiel', width: '140px', render: (row) => row.profileName },
    {
      key: 'status',
      header: 'Status',
      width: '140px',
      render: (row) => (
        <Badge tone={BATCH_STATUS_TONE[row.status]}>
          {row.dryRun ? `${BATCH_STATUS_LABELS[row.status]} (proef)` : BATCH_STATUS_LABELS[row.status]}
        </Badge>
      ),
    },
    {
      key: 'counts',
      header: 'Rijen',
      width: '160px',
      render: (row) => `${row.successCount} ok / ${row.failureCount} fout / ${row.rowCount} totaal`,
    },
    {
      key: 'createdAt',
      header: 'Geüpload',
      width: '160px',
      render: (row) => formatDateTime(row.createdAt),
    },
    {
      key: 'actions',
      header: '',
      width: '90px',
      render: (row) => (
        <button type="button" className="oi-link" onClick={() => openDetail(row)}>
          Details
        </button>
      ),
    },
  ]

  const rowColumns: Column<OrderImportRow>[] = [
    { key: 'rowNumber', header: 'Rij', width: '70px', render: (row) => String(row.rowNumber) },
    { key: 'reference', header: 'Referentie', width: '160px', render: (row) => row.externalReference ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '140px',
      render: (row) => <Badge tone={ROW_STATUS_TONE[row.status]}>{ROW_STATUS_LABELS[row.status]}</Badge>,
    },
    {
      key: 'error',
      header: 'Melding',
      render: (row) => (row.error ? <span className="oi-row-error">{row.error}</span> : '—'),
    },
  ]

  if (!canView && !canUpload) {
    return (
      <div>
        <Breadcrumbs items={[{ label: 'Excel-import' }]} />
        <PageHeader title="Excel-import" />
        <p>Je hebt geen rechten om opdrachtimporten te bekijken.</p>
      </div>
    )
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Excel-import' }]} />
      <PageHeader title="Excel-import" subtitle="Opdrachten automatisch aanmaken vanuit een Excel-bestand" />

      {canUpload && (
        <section className="oi-upload-card" aria-label="Nieuw bestand importeren">
          <form className="oi-upload-form" onSubmit={handleSubmit} noValidate>
            <FormField label="Importprofiel" htmlFor="oi-profile" required>
              <select
                id="oi-profile"
                value={profileId}
                onChange={(e) => setProfileId(e.target.value)}
                disabled={uploading}
              >
                {profiles.length === 0 && <option value="">Geen profielen</option>}
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.name}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Klant zoeken" htmlFor="oi-customer-search">
              <input
                id="oi-customer-search"
                value={customerSearch}
                onChange={(e) => setCustomerSearch(e.target.value)}
                placeholder="Zoek op naam of nummer..."
                disabled={uploading}
              />
            </FormField>
            <FormField label="Klant" htmlFor="oi-customer" required>
              <select
                id="oi-customer"
                value={customerId}
                onChange={(e) => setCustomerId(e.target.value)}
                disabled={uploading}
              >
                <option value="">Kies een klant</option>
                {customers.map((customer) => (
                  <option key={customer.id} value={customer.id}>
                    {customer.name} ({customer.customerNumber})
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Bestand (.xlsx)" htmlFor="oi-file" required>
              <input
                key={fileInputKey}
                id="oi-file"
                type="file"
                accept=".xlsx"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                disabled={uploading}
              />
            </FormField>
            <label className="oi-checkbox">
              <input
                type="checkbox"
                checked={dryRun}
                onChange={(e) => setDryRun(e.target.checked)}
                disabled={uploading}
              />
              Enkel valideren (proefdraaien)
            </label>
            <Button type="submit" disabled={uploading}>
              {uploading ? 'Bezig…' : dryRun ? 'Valideren' : 'Importeren'}
            </Button>
          </form>
          {formError && (
            <div className="oi-form-error" role="alert">
              {formError}
            </div>
          )}
        </section>
      )}

      <DataTable
        columns={columns}
        rows={batches}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={listError}
        emptyMessage="Nog geen importen."
        loadingMessage="Importen laden..."
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />

      {(detail || detailLoading) && (
        <section className="oi-detail" aria-label="Batchdetails">
          {detailLoading || !detail ? (
            <p>Details laden...</p>
          ) : (
            <>
              <h3>
                {detail.batch.fileName} — {detail.batch.customerName}
                {detail.batch.dryRun ? ' (proefdraaien)' : ''}
              </h3>
              <DataTable
                columns={rowColumns}
                rows={detail.rows}
                rowKey={(row) => String(row.rowNumber)}
                emptyMessage="Geen rijen in deze batch."
              />
            </>
          )}
        </section>
      )}
    </div>
  )
}
