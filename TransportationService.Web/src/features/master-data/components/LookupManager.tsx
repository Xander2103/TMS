import { useMemo, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { createLookupApi } from '../api/lookupApi'
import { LookupFormDialog } from './LookupFormDialog'
import type { LookupItem } from '../types'
import type { LookupResourceConfig } from '../lookupRegistry'
import { LOOKUP_GROUP_LABELS } from '../lookupRegistry'
import './LookupManager.css'

type DialogState = { mode: 'create' } | { mode: 'edit'; item: LookupItem } | null

export function LookupManager({ config }: { config: LookupResourceConfig }) {
  const api = useMemo(() => createLookupApi(config.basePath), [config.basePath])
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canManage = hasPermission(config.managePermission)

  // config.title/singular are translation keys (see lookupRegistry) — resolve once per render.
  const title = t(config.title)
  const singular = t(config.singular)

  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)
  const [dialog, setDialog] = useState<DialogState>(null)
  const [deleteTarget, setDeleteTarget] = useState<LookupItem | null>(null)
  const [isDeleting, setIsDeleting] = useState(false)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<LookupItem>(
    (args) => api.search(args),
    { search, isActive: activeFilter, page, errorMessage: t('masterData.list.loadFailed', { title }) },
  )

  // Reset to page 1 whenever a filter changes.
  function handleSearchChange(value: string) {
    setSearch(value)
    setPage(1)
  }
  function handleActiveFilterChange(value: boolean | undefined) {
    setActiveFilter(value)
    setPage(1)
  }

  function handleSaved(_item: LookupItem, wasCreate: boolean) {
    setDialog(null)
    reload()
    toast.showSuccess(wasCreate ? t('masterData.toasts.added') : t('masterData.toasts.updated'))
  }

  async function confirmDelete() {
    if (!deleteTarget) return
    setIsDeleting(true)
    try {
      await api.remove(deleteTarget.id)
      toast.showSuccess(t('masterData.toasts.deleted'))
      setDeleteTarget(null)
      // Step back a page if we just removed the last row on it.
      if (items.length === 1 && page > 1) setPage(page - 1)
      else reload()
    } catch (error) {
      const message =
        error instanceof ApiError && error.status === 409
          ? t('masterData.errors.inUse')
          : t('masterData.errors.deleteFailed')
      toast.showError(message)
    } finally {
      setIsDeleting(false)
    }
  }

  const columns: Column<LookupItem>[] = [
    { key: 'code', header: t('masterData.list.columns.code'), render: (row) => <code>{row.code}</code>, width: '140px' },
    { key: 'name', header: t('masterData.list.columns.name'), render: (row) => row.name },
    {
      key: 'description',
      header: t('masterData.list.columns.description'),
      render: (row) => row.description ?? <span className="lookup-muted">—</span>,
    },
    {
      key: 'status',
      header: t('masterData.list.columns.status'),
      width: '120px',
      render: (row) =>
        row.isActive ? (
          <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
        ) : (
          <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
        ),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: '',
            align: 'right',
            width: '160px',
            render: (row) => (
              <div className="lookup-row-actions" onClick={(event) => event.stopPropagation()}>
                <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', item: row })}>
                  {t('ui.actions.edit')}
                </Button>
                <Button variant="ghost" onClick={() => setDeleteTarget(row)}>
                  {t('ui.actions.delete')}
                </Button>
              </div>
            ),
          } satisfies Column<LookupItem>,
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs
        items={[
          { label: t('navigation.menu.groups.masterData') },
          { label: t(LOOKUP_GROUP_LABELS[config.group]) },
          { label: title },
        ]}
      />
      <PageHeader
        title={title}
        action={canManage && <Button onClick={() => setDialog({ mode: 'create' })}>{t('masterData.list.new', { singular })}</Button>}
      />

      <FilterBar
        search={search}
        onSearchChange={handleSearchChange}
        searchPlaceholder={t('masterData.list.searchPlaceholder')}
        activeFilter={activeFilter}
        onActiveFilterChange={handleActiveFilterChange}
      />

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('masterData.list.empty', { titleLower: title.toLowerCase() })}
        loadingMessage={t('masterData.list.loading', { title })}
        onRowClick={(row) => setDialog({ mode: 'edit', item: row })}
      />

      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />

      {dialog && (
        <LookupFormDialog
          config={config}
          api={api}
          item={dialog.mode === 'edit' ? dialog.item : undefined}
          onSaved={handleSaved}
          onClose={() => setDialog(null)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('masterData.deleteDialog.title', { singular })}
          message={t('masterData.deleteDialog.message', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={isDeleting}
          onConfirm={confirmDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
