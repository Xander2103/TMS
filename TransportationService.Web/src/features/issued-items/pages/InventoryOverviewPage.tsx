import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleMinus, OctagonAlert, PackageX, TriangleAlert, type LucideIcon } from 'lucide-react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { getInventoryOverview, type InventoryOverviewRow } from '../inventoryApi'
import {
  INVENTORY_STATUSES,
  INVENTORY_STATUS_LABELS,
  INVENTORY_STATUS_TONES,
  type InventoryStatus,
} from '../inventoryStatus'
import './InventoryOverviewPage.css'

/** Statussen waarvoor bovenaan een teltegel staat (klik = filter). */
const TILE_STATUSES: { status: InventoryStatus; icon: LucideIcon }[] = [
  { status: 'LowStock', icon: TriangleAlert },
  { status: 'CriticalStock', icon: OctagonAlert },
  { status: 'OutOfStock', icon: PackageX },
  { status: 'NegativeStock', icon: CircleMinus },
]

function formatLevel(value: number | null): string {
  return value != null ? String(value) : '—'
}

/** Voorraadoverzicht over alle sjablonen/varianten met statusfilters en drempelkolommen. */
export function InventoryOverviewPage() {
  const [rows, setRows] = useState<InventoryOverviewRow[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<InventoryStatus | ''>('')
  const [search, setSearch] = useState('')

  useEffect(() => {
    let mounted = true
    getInventoryOverview()
      .then((data) => {
        if (!mounted) return
        setRows(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Het voorraadoverzicht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  const counts = useMemo(() => {
    const result = new Map<InventoryStatus, number>()
    for (const row of rows ?? []) {
      result.set(row.status, (result.get(row.status) ?? 0) + 1)
    }
    return result
  }, [rows])

  const visible = useMemo(() => {
    const query = search.trim().toLowerCase()
    return (rows ?? []).filter((row) => {
      if (statusFilter && row.status !== statusFilter) return false
      if (query) {
        const haystack = `${row.name} ${row.variantLabel ?? ''}`.toLowerCase()
        if (!haystack.includes(query)) return false
      }
      return true
    })
  }, [rows, statusFilter, search])

  const columns: Column<InventoryOverviewRow>[] = [
    {
      key: 'artikel',
      header: 'Artikel',
      render: (row) => (
        <span>
          {row.name}
          {row.variantLabel && <span className="inventory-overview-variant"> — {row.variantLabel}</span>}
        </span>
      ),
    },
    { key: 'categorie', header: 'Categorie', render: (row) => row.category },
    { key: 'locatie', header: 'Locatie', render: (row) => row.storageLocation ?? '—' },
    {
      key: 'voorraad',
      header: 'Huidige voorraad',
      align: 'right',
      render: (row) => `${row.currentStock}${row.unit ? ` ${row.unit}` : ''}`,
    },
    { key: 'waarschuwing', header: 'Waarschuwing', align: 'right', render: (row) => formatLevel(row.warningLevel) },
    { key: 'minimum', header: 'Minimum', align: 'right', render: (row) => formatLevel(row.minimumLevel) },
    { key: 'doel', header: 'Doel', align: 'right', render: (row) => formatLevel(row.targetLevel) },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge tone={INVENTORY_STATUS_TONES[row.status]}>{INVENTORY_STATUS_LABELS[row.status]}</Badge>,
    },
    {
      key: 'mutatie',
      header: 'Laatste mutatie',
      render: (row) => (row.lastMovementAt ? new Date(row.lastMovementAt).toLocaleDateString('nl-BE') : '—'),
    },
    {
      key: 'acties',
      header: <span aria-label="Acties" />,
      render: (row) => (
        <Link className="issued-items-link" to={`/settings/issued-item-templates/${row.templateId}?tab=voorraad`}>
          Detail
        </Link>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Voorraad"
        subtitle="Actuele voorraadstanden en drempels van alle bedrijfsmiddelen."
      />

      <div className="inventory-overview-tiles">
        {TILE_STATUSES.map(({ status, icon: Icon }) => {
          const active = statusFilter === status
          return (
            <button
              key={status}
              type="button"
              className={`inventory-overview-tile inventory-overview-tile-${INVENTORY_STATUS_TONES[status]}${active ? ' inventory-overview-tile-active' : ''}`}
              aria-pressed={active}
              onClick={() => setStatusFilter(active ? '' : status)}
            >
              <Icon size={18} aria-hidden="true" />
              <span className="inventory-overview-tile-label">{INVENTORY_STATUS_LABELS[status]}</span>
              <span className="inventory-overview-tile-count">{counts.get(status) ?? 0}</span>
            </button>
          )
        })}
      </div>

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder="Zoeken op artikel…">
        <select
          className="ui-filter-select"
          aria-label="Statusfilter"
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as InventoryStatus | '')}
        >
          <option value="">Alle statussen</option>
          {INVENTORY_STATUSES.map((status) => (
            <option key={status} value={status}>
              {INVENTORY_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </FilterBar>

      <DataTable
        columns={columns}
        rows={visible}
        rowKey={(row) => `${row.templateId}:${row.variantId ?? ''}`}
        isLoading={rows === null && !loadError}
        error={loadError}
        emptyMessage={
          (rows ?? []).length === 0
            ? 'Nog geen artikelen met voorraadbeheer.'
            : 'Geen artikelen voor deze filters.'
        }
      />
    </div>
  )
}
