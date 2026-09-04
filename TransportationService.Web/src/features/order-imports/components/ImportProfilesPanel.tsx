import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { formatDate } from '../../../utils/dates'
import {
  deleteOrderImportProfile,
  listOrderImportProfiles,
  type OrderImportProfile,
} from '../api/orderImportsApi'
import { ImportProfileEditor } from './ImportProfileEditor'

/**
 * "Importprofielen" tab: teach TransportationService how a customer's Excel files are laid
 * out, once, so future imports interpret them automatically. Overview ↔ editor swap within
 * the tab; the mapping itself lives in ImportProfileEditor.
 */
export function ImportProfilesPanel() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('order_imports.manage_profiles') || hasPermission('orders.manage')

  const [profiles, setProfiles] = useState<OrderImportProfile[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  /** undefined = overview; null = create; profile = edit. */
  const [editing, setEditing] = useState<OrderImportProfile | null | undefined>(undefined)
  const [deleteTarget, setDeleteTarget] = useState<OrderImportProfile | null>(null)

  const reload = useCallback(() => {
    listOrderImportProfiles(true)
      .then((data) => {
        setProfiles(data)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('orderImports.profiles.loadFailed')))
  }, [t])

  useEffect(() => {
    reload()
  }, [reload])

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await deleteOrderImportProfile(target.id)
      showSuccess(t('orderImports.profiles.deleted'))
      reload()
    } catch (err) {
      showError(describeApiError(err, t('orderImports.profiles.deleteFailed')).message)
    }
  }

  if (editing !== undefined) {
    return (
      <ImportProfileEditor
        profile={editing}
        onClose={(saved) => {
          setEditing(undefined)
          if (saved) reload()
        }}
      />
    )
  }

  const columns: Column<OrderImportProfile>[] = [
    { key: 'name', header: t('orderImports.profiles.columnName'), render: (row) => row.name },
    {
      key: 'customer',
      header: t('orderImports.profiles.columnCustomer'),
      render: (row) => row.customerName ?? '—',
    },
    {
      key: 'type',
      header: t('orderImports.profiles.columnType'),
      render: () => t('orderImports.profileEditor.importTypeTransport'),
    },
    {
      key: 'mappings',
      header: t('orderImports.profiles.columnMappings'),
      render: (row) => t('orderImports.profiles.fieldCount', { count: row.mappedFieldCount }),
    },
    {
      key: 'updated',
      header: t('orderImports.profiles.columnUpdated'),
      render: (row) => formatDate(row.updatedAt),
    },
    {
      key: 'status',
      header: t('orderImports.profiles.columnStatus'),
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
            header: <span aria-label={t('orderImports.profiles.actionsAria')} />,
            align: 'right' as const,
            render: (row: OrderImportProfile) => (
              <span className="oi-row-actions">
                <button type="button" className="oi-link" onClick={() => setEditing(row)}>
                  {t('ui.actions.edit')}
                </button>
                <button type="button" className="oi-link oi-link-danger" onClick={() => setDeleteTarget(row)}>
                  {t('ui.actions.delete')}
                </button>
              </span>
            ),
          },
        ]
      : []),
  ]

  return (
    <section aria-label={t('orderImports.tabs.profiles')}>
      <p className="oi-hint">{t('orderImports.profiles.intro')}</p>
      {canManage && (
        <div className="oi-section-actions">
          <Button onClick={() => setEditing(null)}>{t('orderImports.profiles.create')}</Button>
        </div>
      )}
      {loadError ? (
        <p className="oi-form-error" role="alert">
          {loadError}
        </p>
      ) : profiles !== null && profiles.length === 0 ? (
        <EmptyState
          message={t('orderImports.profiles.empty')}
          action={canManage ? <Button onClick={() => setEditing(null)}>{t('orderImports.profiles.create')}</Button> : undefined}
        />
      ) : (
        <DataTable
          columns={columns}
          rows={profiles ?? []}
          rowKey={(row) => row.id}
          isLoading={profiles === null}
          loadingMessage={t('orderImports.profiles.loading')}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('orderImports.profiles.deleteTitle')}
          message={t('orderImports.profiles.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
