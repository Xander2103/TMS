import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { CircleMinus, OctagonAlert, PackageX, TriangleAlert, type LucideIcon } from 'lucide-react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { useLocale } from '../../../i18n/localeContext'
import { getInventoryOverview, type InventoryOverviewRow } from '../inventoryApi'
import {
  INVENTORY_STATUSES,
  INVENTORY_STATUS_LABELS,
  INVENTORY_STATUS_TONES,
  type InventoryStatus,
} from '../inventoryStatus'
import { formatDate } from '../../../utils/dates'
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

/** Voorraadoverzicht over alle sjablonen/varianten met statusfilters en drempelkolommen.
 * `?status=` (bijv. vanaf de dashboardtegels) activeert het statusfilter vooraf. */
export function InventoryOverviewPage() {
  const { t } = useLocale()
  const [searchParams] = useSearchParams()
  const [rows, setRows] = useState<InventoryOverviewRow[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<InventoryStatus | ''>(() => {
    const requested = searchParams.get('status')
    return INVENTORY_STATUSES.includes(requested as InventoryStatus) ? (requested as InventoryStatus) : ''
  })
  const [search, setSearch] = useState('')

  useEffect(() => {
    let mounted = true
    getInventoryOverview()
      .then((data) => {
        if (!mounted) return
        setRows(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('issuedItems.overview.loadFailed')
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
      header: t('issuedItems.overview.colItem'),
      render: (row) => (
        <span>
          {row.name}
          {row.variantLabel && <span className="inventory-overview-variant"> — {row.variantLabel}</span>}
        </span>
      ),
    },
    { key: 'categorie', header: t('issuedItems.overview.colCategory'), render: (row) => row.category },
    { key: 'locatie', header: t('issuedItems.overview.colLocation'), render: (row) => row.storageLocation ?? '—' },
    {
      key: 'voorraad',
      header: t('issuedItems.overview.colStock'),
      align: 'right',
      render: (row) => `${row.currentStock}${row.unit ? ` ${row.unit}` : ''}`,
    },
    { key: 'waarschuwing', header: t('issuedItems.overview.colWarning'), align: 'right', render: (row) => formatLevel(row.warningLevel) },
    { key: 'minimum', header: t('issuedItems.overview.colMinimum'), align: 'right', render: (row) => formatLevel(row.minimumLevel) },
    { key: 'doel', header: t('issuedItems.overview.colTarget'), align: 'right', render: (row) => formatLevel(row.targetLevel) },
    {
      key: 'status',
      header: t('issuedItems.overview.colStatus'),
      render: (row) => <Badge tone={INVENTORY_STATUS_TONES[row.status]}>{t(INVENTORY_STATUS_LABELS[row.status])}</Badge>,
    },
    {
      key: 'mutatie',
      header: t('issuedItems.overview.colLastMovement'),
      render: (row) => (row.lastMovementAt ? formatDate(row.lastMovementAt) : '—'),
    },
    {
      key: 'acties',
      header: <span aria-label={t('issuedItems.tab.colActions')} />,
      render: (row) => (
        <Link className="issued-items-link" to={`/settings/issued-item-templates/${row.templateId}?tab=voorraad`}>
          {t('issuedItems.overview.detail')}
        </Link>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('issuedItems.overview.title')}
        subtitle={t('issuedItems.overview.subtitle')}
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
              <span className="inventory-overview-tile-label">{t(INVENTORY_STATUS_LABELS[status])}</span>
              <span className="inventory-overview-tile-count">{counts.get(status) ?? 0}</span>
            </button>
          )
        })}
      </div>

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder={t('issuedItems.overview.searchPlaceholder')}>
        <select
          className="ui-filter-select"
          aria-label={t('issuedItems.overview.statusFilter')}
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as InventoryStatus | '')}
        >
          <option value="">{t('issuedItems.overview.allStatuses')}</option>
          {INVENTORY_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(INVENTORY_STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </FilterBar>

      <DataTable
        columns={columns}
        rows={visible}
        rowKey={(row) => `${row.templateId}:${row.variantId ?? ''}`}
        isLoading={rows === null && !loadErrorKey}
        error={loadErrorKey ? t(loadErrorKey) : null}
        emptyMessage={
          (rows ?? []).length === 0 ? t('issuedItems.overview.emptyNone') : t('issuedItems.overview.emptyFiltered')
        }
      />
    </div>
  )
}
