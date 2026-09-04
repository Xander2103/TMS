import { useEffect, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Pagination } from '../../../components/ui/Pagination'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import {
  analyzeOrderImportFile,
  getOrderImportBatch,
  listOrderImportBatches,
  listOrderImportProfiles,
  uploadOrderImport,
  type OrderImportBatch,
  type OrderImportBatchDetail,
  type OrderImportBatchStatus,
  type OrderImportProfile,
  type OrderImportProfileMatch,
  type OrderImportRow,
  type OrderImportRowStatus,
} from '../api/orderImportsApi'
import { ImportProfilesPanel } from '../components/ImportProfilesPanel'
import { formatDateTime } from '../../../utils/dates'
import './order-imports.css'

const PAGE_SIZE = 25

const TAB_IDS = ['importeren', 'profielen', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

const BATCH_STATUS_LABELS: Record<OrderImportBatchStatus, string> = {
  Validated: 'orderImports.batchStatus.Validated',
  Processed: 'orderImports.batchStatus.Processed',
  Failed: 'orderImports.batchStatus.Failed',
}

const BATCH_STATUS_TONE: Record<OrderImportBatchStatus, BadgeTone> = {
  Validated: 'info',
  Processed: 'success',
  Failed: 'danger',
}

const ROW_STATUS_LABELS: Record<OrderImportRowStatus, string> = {
  Created: 'orderImports.rowStatus.Created',
  Skipped: 'orderImports.rowStatus.Skipped',
  Error: 'orderImports.rowStatus.Error',
}

const ROW_STATUS_TONE: Record<OrderImportRowStatus, BadgeTone> = {
  Created: 'success',
  Skipped: 'warning',
  Error: 'danger',
}

/**
 * Excel-import (P13 + 2026-09 rework): one sidebar entry, three local tabs — Importeren (the
 * existing upload/dry-run flow), Importprofielen (reusable column→TMS-field mappings) and
 * Importhistoriek (the persisted batch history). Tab state follows the app convention:
 * `?tab=`, replace-navigation, first tab = empty search.
 */
export function OrderImportsPage() {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [searchParams, setSearchParams] = useSearchParams()

  const canView = hasPermission('orders.view') || hasPermission('orders.manage')
  const canUpload = hasPermission('orders.create') || hasPermission('orders.manage')

  const requestedTab = searchParams.get('tab')
  const tab: TabId = TAB_IDS.includes(requestedTab as TabId) ? (requestedTab as TabId) : 'importeren'

  function setTab(next: string) {
    setSearchParams(next === 'importeren' ? {} : { tab: next }, { replace: true })
  }

  // ---------------------------------------------------------------- history (tab: historiek)

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
    { search: '', page, pageSize: PAGE_SIZE, errorMessage: t('orderImports.historyError') },
  )

  // ---------------------------------------------------------------- upload form (tab: importeren)

  const [profiles, setProfiles] = useState<OrderImportProfile[]>([])
  const [profileId, setProfileId] = useState('')
  const [customerOptions, setCustomerOptions] = useState<{ value: string; label: string }[]>([])
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [file, setFile] = useState<File | null>(null)
  const [fileInputKey, setFileInputKey] = useState(0)
  const [dryRun, setDryRun] = useState(true)
  const [uploading, setUploading] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  /** Best saved-profile match for the chosen file (≥90% auto-selects, ≥60% suggests). */
  const [profileMatch, setProfileMatch] = useState<OrderImportProfileMatch | null>(null)

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
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) {
          setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name, keywords: c.customerNumber })))
        }
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [canUpload])

  // Profiles usable for the chosen customer: generic ones + that customer's own.
  const selectableProfiles = profiles.filter((p) => !p.customerId || p.customerId === customerId)

  async function handleFileChosen(chosen: File | null) {
    setFile(chosen)
    setProfileMatch(null)
    if (!chosen) return
    try {
      // Header-based recognition against SAVED profiles; wrong guesses are worse than none,
      // so only a very strong match is applied automatically (and never a foreign customer's).
      const analysis = await analyzeOrderImportFile(chosen)
      const best = analysis.profileMatches.find((m) => !m.customerId || m.customerId === customerId)
      if (!best || best.matchPercent < 60) return
      setProfileMatch(best)
      if (best.matchPercent >= 90) setProfileId(best.profileId)
    } catch {
      // Recognition is a convenience — a failed analysis never blocks the manual flow.
    }
  }

  // ---------------------------------------------------------------- batch detail

  const [detail, setDetail] = useState<OrderImportBatchDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  async function openDetail(batch: OrderImportBatch) {
    setDetailLoading(true)
    try {
      setDetail(await getOrderImportBatch(batch.id))
    } catch {
      showError(t('orderImports.detailError'))
    } finally {
      setDetailLoading(false)
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (uploading) return
    setFormError(null)
    if (!profileId) {
      setFormError(t('orderImports.form.chooseProfile'))
      return
    }
    if (!customerId) {
      setFormError(t('orderImports.form.chooseCustomerError'))
      return
    }
    if (!file) {
      setFormError(t('orderImports.form.chooseFile'))
      return
    }
    setUploading(true)
    try {
      const result = await uploadOrderImport({ file, profileId, customerId, dryRun })
      setDetail(result)
      showSuccess(
        dryRun
          ? t('orderImports.form.validatedToast', {
              success: result.batch.successCount,
              total: result.batch.rowCount,
            })
          : t('orderImports.form.processedToast', {
              orders: t('orderImports.form.processedOrders', { count: result.batch.successCount }),
              failures: t('orderImports.form.processedFailures', { count: result.batch.failureCount }),
            }),
      )
      setFile(null)
      setProfileMatch(null)
      setFileInputKey((key) => key + 1)
      setPage(1)
      reload()
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : t('orderImports.form.uploadFailed'))
    } finally {
      setUploading(false)
    }
  }

  const columns: Column<OrderImportBatch>[] = [
    { key: 'fileName', header: t('orderImports.columns.file'), render: (row) => row.fileName },
    { key: 'customer', header: t('orderImports.columns.customer'), render: (row) => row.customerName },
    { key: 'profile', header: t('orderImports.columns.profile'), width: '140px', render: (row) => row.profileName },
    {
      key: 'status',
      header: t('orderImports.columns.status'),
      width: '140px',
      render: (row) => (
        <Badge tone={BATCH_STATUS_TONE[row.status]}>
          {row.dryRun
            ? t('orderImports.batchStatusDryRun', { status: t(BATCH_STATUS_LABELS[row.status]) })
            : t(BATCH_STATUS_LABELS[row.status])}
        </Badge>
      ),
    },
    {
      key: 'counts',
      header: t('orderImports.columns.rows'),
      width: '160px',
      render: (row) =>
        t('orderImports.counts', { success: row.successCount, failure: row.failureCount, total: row.rowCount }),
    },
    {
      key: 'createdAt',
      header: t('orderImports.columns.uploaded'),
      width: '160px',
      render: (row) => formatDateTime(row.createdAt),
    },
    {
      key: 'actions',
      header: '',
      width: '90px',
      render: (row) => (
        <button type="button" className="oi-link" onClick={() => openDetail(row)}>
          {t('orderImports.details')}
        </button>
      ),
    },
  ]

  const rowColumns: Column<OrderImportRow>[] = [
    { key: 'rowNumber', header: t('orderImports.columns.row'), width: '70px', render: (row) => String(row.rowNumber) },
    {
      key: 'reference',
      header: t('orderImports.columns.reference'),
      width: '160px',
      render: (row) => row.externalReference ?? '—',
    },
    {
      key: 'status',
      header: t('orderImports.columns.status'),
      width: '140px',
      render: (row) => <Badge tone={ROW_STATUS_TONE[row.status]}>{t(ROW_STATUS_LABELS[row.status])}</Badge>,
    },
    {
      key: 'error',
      header: t('orderImports.columns.message'),
      render: (row) => (row.error ? <span className="oi-row-error">{row.error}</span> : '—'),
    },
  ]

  const detailSection = (detail || detailLoading) && (
    <section className="oi-detail" aria-label={t('orderImports.detailSection')}>
      {detailLoading || !detail ? (
        <p>{t('orderImports.detailLoading')}</p>
      ) : (
        <>
          <h3>
            {detail.batch.fileName} — {detail.batch.customerName}
            {detail.batch.dryRun ? ` ${t('orderImports.dryRunTitleSuffix')}` : ''}
          </h3>
          <DataTable
            columns={rowColumns}
            rows={detail.rows}
            rowKey={(row) => String(row.rowNumber)}
            emptyMessage={t('orderImports.emptyRows')}
          />
        </>
      )}
    </section>
  )

  if (!canView && !canUpload) {
    return (
      <div>
        <Breadcrumbs items={[{ label: t('navigation.menu.excelImport') }]} />
        <PageHeader title={t('navigation.menu.excelImport')} />
        <p>{t('orderImports.noPermission')}</p>
      </div>
    )
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.excelImport') }]} />
      <PageHeader title={t('navigation.menu.excelImport')} subtitle={t('orderImports.subtitle')} />

      <Tabs
        tabs={[
          { id: 'importeren', label: t('orderImports.tabs.import') },
          { id: 'profielen', label: t('orderImports.tabs.profiles') },
          { id: 'historiek', label: t('orderImports.tabs.history') },
        ]}
        activeId={tab}
        onChange={setTab}
      />

      {tab === 'importeren' && (
        <TabPanel tabId="importeren">
          {canUpload ? (
            <section className="oi-upload-card" aria-label={t('orderImports.uploadSection')}>
              <form className="oi-upload-form" onSubmit={(event) => void handleSubmit(event)} noValidate>
                <div className="oi-upload-grid">
                  <FormField label={t('orderImports.form.customer')} htmlFor="oi-customer" required>
                    <SearchableSelect
                      id="oi-customer"
                      value={customerId}
                      onChange={(value) => {
                        setCustomerId(value)
                        setProfileMatch(null)
                      }}
                      options={customerOptions}
                      placeholder={t('orderImports.form.customerSearchPlaceholder')}
                      disabled={uploading}
                    />
                  </FormField>
                  <FormField label={t('orderImports.form.profile')} htmlFor="oi-profile" required>
                    <SearchableSelect
                      id="oi-profile"
                      value={profileId || null}
                      onChange={(value) => setProfileId(value ?? '')}
                      options={selectableProfiles.map((profile) => ({
                        value: profile.id,
                        label: profile.name,
                        description: profile.customerName ?? undefined,
                      }))}
                      placeholder={t('orderImports.form.noProfiles')}
                      clearable={false}
                      disabled={uploading}
                    />
                  </FormField>
                </div>
                <FormField label={t('orderImports.form.file')} htmlFor="oi-file" required>
                  <input
                    key={fileInputKey}
                    id="oi-file"
                    type="file"
                    accept=".xlsx"
                    onChange={(e) => void handleFileChosen(e.target.files?.[0] ?? null)}
                    disabled={uploading}
                  />
                </FormField>
                {profileMatch && (
                  <p className="oi-hint" role="note">
                    {t('orderImports.form.profileRecognized', {
                      name: profileMatch.name,
                      percent: profileMatch.matchPercent,
                    })}
                    {profileMatch.matchPercent < 90 && profileId !== profileMatch.profileId && (
                      <>
                        {' '}
                        <button type="button" className="oi-link" onClick={() => setProfileId(profileMatch.profileId)}>
                          {t('orderImports.form.useRecognizedProfile')}
                        </button>
                      </>
                    )}
                  </p>
                )}
                <label className="oi-checkbox">
                  <input
                    type="checkbox"
                    checked={dryRun}
                    onChange={(e) => setDryRun(e.target.checked)}
                    disabled={uploading}
                  />
                  {t('orderImports.form.dryRun')}
                </label>
                <div className="oi-section-actions">
                  <Button type="submit" disabled={uploading}>
                    {uploading
                      ? t('orderImports.form.busy')
                      : dryRun
                        ? t('orderImports.form.validate')
                        : t('orderImports.form.import')}
                  </Button>
                </div>
              </form>
              {formError && (
                <div className="oi-form-error" role="alert">
                  {formError}
                </div>
              )}
            </section>
          ) : (
            <p className="oi-hint">{t('orderImports.noUploadPermission')}</p>
          )}
          {detailSection}
        </TabPanel>
      )}

      {tab === 'profielen' && (
        <TabPanel tabId="profielen">
          <ImportProfilesPanel />
        </TabPanel>
      )}

      {tab === 'historiek' && (
        <TabPanel tabId="historiek">
          <DataTable
            columns={columns}
            rows={batches}
            rowKey={(row) => row.id}
            isLoading={isLoading}
            error={listError}
            emptyMessage={t('orderImports.empty')}
            loadingMessage={t('orderImports.loading')}
          />
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
          {detailSection}
        </TabPanel>
      )}
    </div>
  )
}
