import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { searchLocations, setLocationActive, setLocationDefaults } from '../../locations/api/locationsApi'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import { LOCATION_TYPE_LABEL_KEYS, LOCATION_TYPES, type LocationListItem, type LocationType } from '../../locations/types'

interface CustomerLocationsPanelProps {
  customerId: string
}

/**
 * Locations tab on the customer detail page: view/search this customer's locations, set the
 * default loading/unloading site, deactivate/reactivate, and jump to the full location form.
 * Uses the central locations module — no separate customer-location entity.
 */
export function CustomerLocationsPanel({ customerId }: CustomerLocationsPanelProps) {
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canEdit = hasPermission('locations.edit')
  const canCreate = hasPermission('locations.create')

  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState<LocationType | ''>('')
  const [showInactive, setShowInactive] = useState(false)
  const [rows, setRows] = useState<LocationListItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedKey, setLoadedKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [confirmDeactivate, setConfirmDeactivate] = useState<LocationListItem | null>(null)
  const [showQuickCreate, setShowQuickCreate] = useState(false)

  const requestKey = `${customerId}|${search}|${typeFilter}|${showInactive}|${reloadToken}`
  const isLoading = loadedKey !== requestKey

  useEffect(() => {
    let mounted = true
    searchLocations({
      customerId,
      search: search || undefined,
      type: typeFilter || undefined,
      isActive: showInactive ? undefined : true,
      page: 1,
      pageSize: 100,
    })
      .then((result) => {
        if (!mounted) return
        setRows(result.items)
        setError(null)
        setLoadedKey(requestKey)
      })
      .catch(() => {
        if (!mounted) return
        setError(t('customers.locations.loadFailed'))
        setLoadedKey(requestKey)
      })
    return () => {
      mounted = false
    }
  }, [customerId, search, typeFilter, showInactive, reloadToken, requestKey, t])

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  async function runRowAction(row: LocationListItem, action: () => Promise<unknown>, successMessage: string) {
    setBusyId(row.id)
    try {
      await action()
      toast.showSuccess(successMessage)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('customers.locations.actionFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<LocationListItem>[] = [
    {
      key: 'name',
      header: t('customers.locations.columnLocation'),
      render: (row) => (
        <>
          <Link to={`/locations/${row.id}`}>{row.name}</Link>{' '}
          <span className="customer-locations-code">({row.code})</span>
        </>
      ),
    },
    { key: 'type', header: t('customers.locations.columnType'), render: (row) => t(LOCATION_TYPE_LABEL_KEYS[row.type]) },
    { key: 'city', header: t('customers.locations.columnCity'), render: (row) => row.city ?? '—' },
    {
      key: 'status',
      header: t('customers.locations.columnStatus'),
      render: (row) => (
        <span className="customer-locations-badges">
          {row.isActive ? (
            <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
          ) : (
            <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
          )}
          {row.isDefaultLoadingLocation && <Badge tone="info">{t('customers.locations.defaultLoadingBadge')}</Badge>}
          {row.isDefaultUnloadingLocation && <Badge tone="info">{t('customers.locations.defaultUnloadingBadge')}</Badge>}
          {row.isDefaultBillingLocation && <Badge tone="info">{t('customers.locations.defaultBillingBadge')}</Badge>}
        </span>
      ),
    },
    {
      key: 'actions',
      header: t('customers.locations.columnActions'),
      render: (row: LocationListItem) => (
        <span className="customer-locations-actions">
          <Link to={`/locations/${row.id}`}>{t('customers.locations.fullEdit')}</Link>
          {canEdit && (
            <>
              {!row.isDefaultLoadingLocation && row.isActive && (
                  <Button
                    variant="ghost"
                    disabled={busyId === row.id}
                    onClick={() =>
                      runRowAction(
                        row,
                        () =>
                          setLocationDefaults(row.id, {
                            isDefaultLoadingLocation: true,
                            isDefaultUnloadingLocation: row.isDefaultUnloadingLocation,
                            isDefaultBillingLocation: row.isDefaultBillingLocation,
                          }),
                        t('customers.locations.defaultLoadingSet'),
                      )
                    }
                  >
                    {t('customers.locations.makeDefaultLoading')}
                  </Button>
                )}
                {!row.isDefaultUnloadingLocation && row.isActive && (
                  <Button
                    variant="ghost"
                    disabled={busyId === row.id}
                    onClick={() =>
                      runRowAction(
                        row,
                        () =>
                          setLocationDefaults(row.id, {
                            isDefaultLoadingLocation: row.isDefaultLoadingLocation,
                            isDefaultUnloadingLocation: true,
                            isDefaultBillingLocation: row.isDefaultBillingLocation,
                          }),
                        t('customers.locations.defaultUnloadingSet'),
                      )
                    }
                  >
                    {t('customers.locations.makeDefaultUnloading')}
                  </Button>
                )}
                {!row.isDefaultBillingLocation && row.isActive && (
                  <Button
                    variant="ghost"
                    disabled={busyId === row.id}
                    onClick={() =>
                      runRowAction(
                        row,
                        () =>
                          setLocationDefaults(row.id, {
                            isDefaultLoadingLocation: row.isDefaultLoadingLocation,
                            isDefaultUnloadingLocation: row.isDefaultUnloadingLocation,
                            isDefaultBillingLocation: true,
                          }),
                        t('customers.locations.defaultBillingSet'),
                      )
                    }
                  >
                    {t('customers.locations.makeDefaultBilling')}
                  </Button>
                )}
                {row.isActive ? (
                  <Button variant="ghost" disabled={busyId === row.id} onClick={() => setConfirmDeactivate(row)}>
                    {t('customers.locations.deactivate')}
                  </Button>
                ) : (
                  <Button
                    variant="ghost"
                    disabled={busyId === row.id}
                    onClick={() =>
                      runRowAction(row, () => setLocationActive(row.id, true), t('customers.locations.reactivated'))
                    }
                  >
                    {t('customers.locations.reactivate')}
                  </Button>
                )}
            </>
          )}
        </span>
      ),
    },
  ]

  return (
    <div className="customer-locations">
      <div className="customer-locations-toolbar">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('customers.locations.searchPlaceholder')}
          aria-label={t('customers.locations.searchAria')}
        />
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as LocationType | '')}
          aria-label={t('customers.locations.typeFilterAria')}
        >
          <option value="">{t('customers.locations.allTypes')}</option>
          {LOCATION_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(LOCATION_TYPE_LABEL_KEYS[type])}
            </option>
          ))}
        </select>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
          {t('customers.locations.showInactive')}
        </label>
        {canCreate && (
          <span className="customer-locations-new">
            <Button onClick={() => setShowQuickCreate(true)}>{t('customers.locations.addAddress')}</Button>
            {/* Full form (all sections) with this customer preselected via the query param. */}
            <Link to={`/locations/new?customerId=${customerId}`}>{t('customers.locations.newForCustomer')}</Link>
          </span>
        )}
      </div>

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('customers.locations.empty')}
        loadingMessage={t('customers.locations.loading')}
      />

      {showQuickCreate && (
        <LocationQuickCreateDialog
          customerId={customerId}
          onClose={(created) => {
            setShowQuickCreate(false)
            if (created) {
              toast.showSuccess(t('customers.locations.created', { name: created.name }))
              reload()
            }
          }}
        />
      )}

      {confirmDeactivate && (
        <ConfirmDialog
          title={t('customers.locations.deactivateTitle')}
          message={t('customers.locations.deactivateMessage', { name: confirmDeactivate.name })}
          confirmLabel={t('customers.locations.deactivate')}
          busy={busyId === confirmDeactivate.id}
          onConfirm={async () => {
            const row = confirmDeactivate
            await runRowAction(row, () => setLocationActive(row.id, false), t('customers.locations.deactivated'))
            setConfirmDeactivate(null)
          }}
          onCancel={() => setConfirmDeactivate(null)}
        />
      )}
    </div>
  )
}
