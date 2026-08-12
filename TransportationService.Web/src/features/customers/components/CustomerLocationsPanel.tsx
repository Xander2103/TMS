import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { searchLocations, setLocationActive, setLocationDefaults } from '../../locations/api/locationsApi'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationListItem, type LocationType } from '../../locations/types'

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
        setError('Locaties konden niet worden geladen.')
        setLoadedKey(requestKey)
      })
    return () => {
      mounted = false
    }
  }, [customerId, search, typeFilter, showInactive, reloadToken, requestKey])

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  async function runRowAction(row: LocationListItem, action: () => Promise<unknown>, successMessage: string) {
    setBusyId(row.id)
    try {
      await action()
      toast.showSuccess(successMessage)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'De actie kon niet worden uitgevoerd.').message)
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<LocationListItem>[] = [
    {
      key: 'name',
      header: 'Locatie',
      render: (row) => (
        <>
          <Link to={`/locations/${row.id}`}>{row.name}</Link>{' '}
          <span className="customer-locations-code">({row.code})</span>
        </>
      ),
    },
    { key: 'type', header: 'Type', render: (row) => LOCATION_TYPE_LABELS[row.type] },
    { key: 'city', header: 'Plaats', render: (row) => row.city ?? '—' },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <span className="customer-locations-badges">
          {row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
          {row.isDefaultLoadingLocation && <Badge tone="info">Standaard laden</Badge>}
          {row.isDefaultUnloadingLocation && <Badge tone="info">Standaard lossen</Badge>}
          {row.isDefaultBillingLocation && <Badge tone="info">Standaard facturatie</Badge>}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Acties',
      render: (row: LocationListItem) => (
        <span className="customer-locations-actions">
          <Link to={`/locations/${row.id}`}>Volledig bewerken</Link>
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
                        'Standaard laadlocatie ingesteld.',
                      )
                    }
                  >
                    Maak standaard laden
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
                        'Standaard loslocatie ingesteld.',
                      )
                    }
                  >
                    Maak standaard lossen
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
                        'Standaard facturatieadres ingesteld.',
                      )
                    }
                  >
                    Maak standaard facturatie
                  </Button>
                )}
                {row.isActive ? (
                  <Button variant="ghost" disabled={busyId === row.id} onClick={() => setConfirmDeactivate(row)}>
                    Deactiveren
                  </Button>
                ) : (
                  <Button
                    variant="ghost"
                    disabled={busyId === row.id}
                    onClick={() => runRowAction(row, () => setLocationActive(row.id, true), 'Locatie geheractiveerd.')}
                  >
                    Heractiveren
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
          placeholder="Zoeken op naam, code of plaats…"
          aria-label="Locaties zoeken"
        />
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as LocationType | '')}
          aria-label="Filter op type"
        >
          <option value="">Alle types</option>
          {LOCATION_TYPES.map((type) => (
            <option key={type} value={type}>
              {LOCATION_TYPE_LABELS[type]}
            </option>
          ))}
        </select>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
          Toon inactieve
        </label>
        {canCreate && (
          <span className="customer-locations-new">
            <Button onClick={() => setShowQuickCreate(true)}>+ Adres toevoegen</Button>
            {/* Full form (all sections) with this customer preselected via the query param. */}
            <Link to={`/locations/new?customerId=${customerId}`}>Nieuwe locatie voor deze klant</Link>
          </span>
        )}
      </div>

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Deze klant heeft nog geen locaties."
        loadingMessage="Locaties laden..."
      />

      {showQuickCreate && (
        <LocationQuickCreateDialog
          customerId={customerId}
          onClose={(created) => {
            setShowQuickCreate(false)
            if (created) {
              toast.showSuccess(`Locatie '${created.name}' aangemaakt.`)
              reload()
            }
          }}
        />
      )}

      {confirmDeactivate && (
        <ConfirmDialog
          title="Locatie deactiveren"
          message={`'${confirmDeactivate.name}' deactiveren? De locatie blijft behouden voor bestaande opdrachten, maar is niet meer kiesbaar voor nieuwe.`}
          confirmLabel="Deactiveren"
          busy={busyId === confirmDeactivate.id}
          onConfirm={async () => {
            const row = confirmDeactivate
            await runRowAction(row, () => setLocationActive(row.id, false), 'Locatie gedeactiveerd.')
            setConfirmDeactivate(null)
          }}
          onCancel={() => setConfirmDeactivate(null)}
        />
      )}
    </div>
  )
}
