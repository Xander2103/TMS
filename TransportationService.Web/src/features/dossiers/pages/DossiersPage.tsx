import { useEffect, useId, useRef, useState } from 'react'
import { SlidersHorizontal } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column, type SortState } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { searchDrivers } from '../../drivers/api/driversApi'
import { euro } from '../../invoices/types'
import { getTrailerOptions } from '../../trailers/api/trailersApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import { listActivityTypes } from '../api/activityTypesApi'
import { cancelDossier, reopenDossier, searchDossiers } from '../api/dossiersApi'
import { DossierConfirmDialog } from '../components/DossierConfirmDialog'
import { DossierFilterPanel } from '../components/DossierFilterPanel'
import { EMPTY_DOSSIER_FILTER_LOOKUPS, type DossierFilterLookups } from '../components/dossierFilterLookups'
import { DossierReasonDialog } from '../components/DossierReasonDialog'
import { DossierRowActions } from '../components/DossierRowActions'
import { dossierVersionConflict } from '../dossierConflict'
import { dossierListConfirmation, dossierListPriceStatus } from '../dossierDisplay'
import {
  DOSSIER_STATUS_LABELS,
  DOSSIER_STATUS_TONE,
  type DossierListItem,
  type DossierSortKey,
  type DossierStatus,
} from '../types'
import {
  activeFilterChips,
  clearChipPatch,
  DOSSIER_STATUSES,
  EMPTY_DOSSIER_FILTERS,
  toSearchDossiersParams,
  wholeMonthOf,
  type DossierFilterChip,
  type DossierFilterKey,
} from './dossierSearchParams'
import { useDossierFilters } from './useDossierFilters'
import './dossiers.css'

const EMPTY = '—'
const PAGE_SIZE = 25

/** Server sort keys offered in "Sorteren op"; the first is the server default. */
const SORT_KEYS: readonly DossierSortKey[] = ['date', 'number', 'customer', 'status', 'confirmedAt', 'planningDate', 'createdAt']

/** Lookup filters whose chip shows the chosen name; the rest read "Label: waarde". */
const LOOKUP_KEYS = ['customerId', 'transportTypeId', 'activityTypeId', 'driverId', 'vehicleId', 'trailerId'] as const
type LookupKey = (typeof LOOKUP_KEYS)[number]

const LOOKUP_SOURCE: Record<LookupKey, keyof DossierFilterLookups> = {
  customerId: 'customers',
  transportTypeId: 'transportTypes',
  activityTypeId: 'activityTypes',
  driverId: 'drivers',
  vehicleId: 'vehicles',
  trailerId: 'trailers',
}

function isLookupKey(key: DossierFilterKey): key is LookupKey {
  return (LOOKUP_KEYS as readonly string[]).includes(key)
}

type RowDialog = { kind: 'confirm' | 'cancel' | 'reopen'; row: DossierListItem }

/** Tekstcel die bij overloop afkapt met een ellipsis en de volledige waarde als tooltip toont. */
function TruncatedCell({ value }: { value: string | null }) {
  if (!value) return <>{EMPTY}</>
  return (
    <span className="dossier-list-truncate" title={value}>
      {value}
    </span>
  )
}

/** "september 2026" in the active language. */
function monthLabel({ year, month }: { year: number; month: number }): string {
  return new Intl.DateTimeFormat(getActiveLocale(), { month: 'long', year: 'numeric' }).format(new Date(year, month - 1, 1))
}

function capitalise(value: string): string {
  return value.charAt(0).toLocaleUpperCase() + value.slice(1)
}

/**
 * Dossiers: paged, URL-synced search over GET /api/dossiers/search. Every filter, the sort and
 * the page live in the querystring — the ONE source of truth — so refresh/back/share keep the
 * exact view. The bar is search + status + "Filters"; the extended panel is closed by default
 * and only a view on that same state, so closing it never drops a filter: the "Filters (N)"
 * count and the chips keep showing what is active. Row actions reuse the lifecycle dialogs and
 * endpoints of the detail page.
 */
export function DossiersPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const toast = useToast()
  const canManage = hasPermission('dossiers.manage')
  const canReopen = hasPermission('dossiers.reopen')

  const { filters, apply } = useDossierFilters()

  const chips = activeFilterChips(filters)
  const hasActiveFilters = chips.length > 0 || filters.search !== ''
  const [panelOpen, setPanelOpen] = useState(false)
  const panelId = useId()

  // --- lookups ---------------------------------------------------------------------------------
  // Loaded once, the first time they are needed: the panel opens, or an id filter is set in the
  // URL (so its chip can show a name instead of an id). The one-shot guard is a ref, so the result
  // is deliberately NOT tied to an effect cleanup: StrictMode's mount/unmount/mount would otherwise
  // discard the only load ever started.
  const needsLookups = panelOpen || LOOKUP_KEYS.some((key) => filters[key] !== '')
  const [lookups, setLookups] = useState<DossierFilterLookups>(EMPTY_DOSSIER_FILTER_LOOKUPS)
  const lookupsRequested = useRef(false)
  useEffect(() => {
    if (!needsLookups || lookupsRequested.current) return
    lookupsRequested.current = true
    const settle = <T,>(promise: Promise<T>, fallback: T) => promise.catch(() => fallback)
    Promise.all([
      settle(searchCustomers({ isActive: true, page: 1, pageSize: 200 }).then((r) => r.items), []),
      settle(searchDrivers({ isActive: true, page: 1, pageSize: 200 }).then((r) => r.items), []),
      settle(getVehicleOptions(), []),
      settle(getTrailerOptions(), []),
      settle(listActivityTypes(), []),
    ]).then(([customers, drivers, vehicles, trailers, activityTypes]) => {
      const typeOption = (a: { id: string; name: string }): SearchableSelectOption => ({ value: a.id, label: a.name })
      setLookups({
        customers: customers.map((c) => ({ value: c.id, label: c.name })),
        drivers: drivers.map((d) => ({ value: d.id, label: d.fullName })),
        vehicles: vehicles.map((v) => ({ value: v.id, label: `${v.internalNumber} · ${v.licensePlate}` })),
        trailers: trailers.map((tr) => ({ value: tr.id, label: `${tr.internalNumber} · ${tr.licensePlate}` })),
        transportTypes: activityTypes.filter((a) => a.hasStops).map(typeOption),
        activityTypes: activityTypes.map(typeOption),
      })
    })
  }, [needsLookups])

  // --- data ------------------------------------------------------------------------------------
  const { search, page, ...extra } = filters
  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<DossierListItem>(
    (args) => searchDossiers({ ...toSearchDossiersParams(filters, args.pageSize), search: args.search || undefined, page: args.page }),
    {
      search,
      page,
      pageSize: PAGE_SIZE,
      errorMessage: t('dossiers.list.loadFailed'),
      extra,
    },
  )

  const sort: SortState | null = filters.sort ? { key: filters.sort, dir: filters.dir || 'asc' } : null

  // --- row actions -----------------------------------------------------------------------------
  const [dialog, setDialog] = useState<RowDialog | null>(null)

  /**
   * Runs a reason-based lifecycle action (cancel, reopen) of a row. A version conflict (someone
   * changed the dossier since the list loaded) closes the dialog and refreshes the list; any
   * other rejection is rethrown as a readable Error so the dialog shows the server's explanation
   * (e.g. planned orders block a cancellation) and keeps the entered reason.
   */
  async function submitLifecycle(action: () => Promise<unknown>, successMessage: string, failedMessage: string) {
    try {
      await action()
    } catch (err) {
      if (dossierVersionConflict(err)) {
        setDialog(null)
        toast.showError(t('dossiers.lifecycle.conflict'))
        reload()
        return
      }
      throw new Error(describeApiError(err, failedMessage).message, { cause: err })
    }
    setDialog(null)
    toast.showSuccess(successMessage)
    reload()
  }

  // --- chip labels -----------------------------------------------------------------------------
  const fieldLabel = (key: DossierFilterKey) => t(`dossiers.filters.fields.${key}`)

  const enumLabels: Partial<Record<DossierFilterKey, Record<string, string>>> = {
    confirmationSource: { Manual: t('dossiers.filters.options.sourceManual'), Automatic: t('dossiers.filters.options.sourceAutomatic') },
    priceStatus: {
      Priced: t('dossiers.filters.options.priced'),
      Partial: t('dossiers.filters.options.partial'),
      Unpriced: t('dossiers.filters.options.unpriced'),
    },
    invoiceStatus: {
      NotInvoiced: t('dossiers.filters.options.notInvoiced'),
      Draft: t('dossiers.filters.options.invoiceDraft'),
      Sent: t('dossiers.filters.options.invoiceSent'),
      Paid: t('dossiers.filters.options.invoicePaid'),
    },
    hasCmr: { true: t('dossiers.filters.options.cmrYes'), false: t('dossiers.filters.options.cmrNo') },
  }

  function rangeValue(from: string, to: string): string {
    if (from && to) return t('dossiers.filters.ranges.between', { from: formatDate(from), to: formatDate(to) })
    return from ? t('dossiers.filters.ranges.since', { date: formatDate(from) }) : t('dossiers.filters.ranges.until', { date: formatDate(to) })
  }

  /** Compact chip text: the value alone where it speaks for itself, "Label: waarde" otherwise. */
  function chipLabel(chip: DossierFilterChip): string {
    if (chip.kind === 'range') {
      const from = filters[chip.keys[0]]
      const to = filters[chip.keys[1]]
      const month = from && to ? wholeMonthOf(from, to) : null
      // The dossier date is THE date of the list: a whole month reads as just "September 2026".
      if (month && chip.id === 'date') return capitalise(monthLabel(month))
      return `${t(`dossiers.filters.ranges.${chip.id}`)}: ${month ? monthLabel(month) : rangeValue(from, to)}`
    }
    const key = chip.id
    const value = filters[key]
    if (key === 'status') return t(DOSSIER_STATUS_LABELS[value as DossierStatus])
    if (isLookupKey(key)) {
      // Until the lookup has loaded the chip names the filter, never a raw id.
      return lookups[LOOKUP_SOURCE[key]].find((o) => o.value === value)?.label ?? fieldLabel(key)
    }
    const shown = key === 'countryCode' ? value.toUpperCase() : (enumLabels[key]?.[value] ?? value)
    return `${fieldLabel(key)}: ${shown}`
  }

  // --- columns ---------------------------------------------------------------------------------
  // Widths in px (the table text has a fixed px size). Klant and Transport have none: they share
  // what the fixed columns leave — see the budget in dossiers.css.
  const columns: Column<DossierListItem>[] = [
    {
      key: 'number',
      header: t('dossiers.list.columns.number'),
      width: '108px',
      sortKey: 'number',
      render: (row) => (
        <span className="dossier-list-number">
          <code>{row.dossierNumber}</code>
          {row.openIncidentCount > 0 && (
            <Badge tone="warning" title={t('dossiers.list.openIncidents', { count: row.openIncidentCount })}>
              {row.openIncidentCount}
            </Badge>
          )}
        </span>
      ),
    },
    {
      key: 'status',
      header: t('dossiers.list.columns.status'),
      width: '190px',
      sortKey: 'status',
      render: (row) => <StatusCell row={row} />,
    },
    {
      key: 'customer',
      header: t('dossiers.list.columns.customer'),
      sortKey: 'customer',
      render: (row) => (
        <span className="dossier-list-stack">
          <TruncatedCell value={row.customerName} />
          {row.customerNumber && <span className="dossier-list-muted">{row.customerNumber}</span>}
        </span>
      ),
    },
    {
      key: 'reference',
      header: t('dossiers.list.columns.reference'),
      width: '112px',
      render: (row) => <TruncatedCell value={row.customerReference} />,
    },
    {
      key: 'date',
      header: t('dossiers.list.columns.date'),
      width: '136px',
      sortKey: 'date',
      render: (row) => (row.dossierDate ? formatDate(row.dossierDate) : EMPTY),
    },
    {
      key: 'transport',
      header: t('dossiers.list.columns.transport'),
      render: (row) => <TransportCell row={row} />,
    },
    {
      key: 'planning',
      header: t('dossiers.list.columns.planning'),
      width: '144px',
      sortKey: 'planningDate',
      render: (row) => <PlanningCell row={row} />,
    },
    {
      key: 'price',
      header: t('dossiers.list.columns.price'),
      width: '140px',
      align: 'right',
      render: (row) => <PriceCell row={row} />,
    },
    {
      key: 'invoice',
      header: t('dossiers.list.columns.invoice'),
      width: '100px',
      render: (row) => (row.invoiceStatus ? t(`dossiers.list.invoice.${row.invoiceStatus}`) : EMPTY),
    },
    {
      key: 'actions',
      header: '',
      width: '48px',
      align: 'right',
      render: (row) => {
        const isOpen = row.status === 'Open'
        return (
          <DossierRowActions
            dossierNumber={row.dossierNumber}
            onOpen={() => navigate(`/dossiers/${row.id}`)}
            onConfirm={canManage && isOpen ? () => setDialog({ kind: 'confirm', row }) : undefined}
            onCancel={canManage && isOpen ? () => setDialog({ kind: 'cancel', row }) : undefined}
            onReopen={canReopen && !isOpen ? () => setDialog({ kind: 'reopen', row }) : undefined}
          />
        )
      },
    },
  ]

  // --- render ----------------------------------------------------------------------------------
  return (
    <div>
      <PageHeader
        title={t('dossiers.list.title')}
        subtitle={t('dossiers.list.subtitle')}
        action={canManage ? <Button onClick={() => navigate('/dossiers/new')}>{t('dossiers.list.new')}</Button> : undefined}
      />

      <div className="dossier-filters">
        <FilterBar
          search={filters.search}
          onSearchChange={(value) => apply({ search: value }, { replace: true })}
          searchPlaceholder={t('dossiers.list.searchPlaceholder')}
        >
          <select
            className="ui-filter-select"
            aria-label={t('dossiers.filters.fields.status')}
            value={filters.status}
            onChange={(e) => apply({ status: e.target.value as '' | DossierStatus })}
          >
            <option value="">{t('dossiers.filters.allStatuses')}</option>
            {DOSSIER_STATUSES.map((status) => (
              <option key={status} value={status}>
                {t(DOSSIER_STATUS_LABELS[status])}
              </option>
            ))}
          </select>
          <button
            type="button"
            className="dossier-filter-toggle"
            aria-expanded={panelOpen}
            aria-controls={panelId}
            data-active={chips.length > 0 ? 'true' : undefined}
            onClick={() => setPanelOpen((open) => !open)}
          >
            <SlidersHorizontal size={16} aria-hidden="true" />
            {chips.length > 0 ? t('dossiers.filters.toggleCount', { count: chips.length }) : t('dossiers.filters.toggle')}
          </button>
        </FilterBar>

        {panelOpen && <DossierFilterPanel id={panelId} filters={filters} lookups={lookups} onChange={apply} />}

        {hasActiveFilters && (
          <div className="dossier-active-filters">
            <ul className="dossier-active-chips" aria-label={t('dossiers.filters.activeLabel')}>
              {filters.search !== '' && (
                <li className="dossier-active-chip">
                  <span>{t('dossiers.filters.searchChip', { term: filters.search })}</span>
                  <button type="button" aria-label={t('dossiers.filters.removeSearch')} onClick={() => apply({ search: '' })}>
                    ✕
                  </button>
                </li>
              )}
              {chips.map((chip) => {
                const label = chipLabel(chip)
                return (
                  <li key={chip.id} className="dossier-active-chip">
                    <span>{label}</span>
                    <button type="button" aria-label={t('dossiers.filters.remove', { label })} onClick={() => apply(clearChipPatch(chip))}>
                      ✕
                    </button>
                  </li>
                )
              })}
            </ul>
            {/* Back to the default view: every filter AND the search text; the sort is not a filter. */}
            <button
              type="button"
              className="link-button dossier-clear-filters"
              onClick={() => apply({ ...EMPTY_DOSSIER_FILTERS, sort: filters.sort, dir: filters.dir })}
            >
              {t('dossiers.filters.clear')}
            </button>
          </div>
        )}
      </div>

      <div className="dossier-list-toolbar">
        <span className="dossier-list-count" aria-live="polite">
          {!isLoading && !error ? t('dossiers.list.resultCount', { count: totalCount }) : ''}
        </span>
        <label className="dossier-list-sort">
          <span>{t('dossiers.list.sortLabel')}</span>
          <select
            value={filters.sort || SORT_KEYS[0]}
            // The direction resets so the server picks the natural one (newest first for dates, A→Z for text).
            onChange={(e) => apply({ sort: e.target.value === SORT_KEYS[0] ? '' : (e.target.value as DossierSortKey), dir: '' })}
          >
            {SORT_KEYS.map((key) => (
              <option key={key} value={key}>
                {t(`dossiers.list.sort.${key}`)}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="dossier-list">
        <DataTable
          columns={columns}
          rows={items}
          rowKey={(row) => row.id}
          isLoading={isLoading}
          error={error}
          emptyMessage={t('dossiers.list.empty')}
          loadingMessage={t('dossiers.list.loading')}
          onRowClick={(row) => navigate(`/dossiers/${row.id}`)}
          sort={sort}
          onSortChange={(next) => apply({ sort: next.key as DossierSortKey, dir: next.dir })}
        />
      </div>
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />

      {dialog?.kind === 'confirm' && (
        <DossierConfirmDialog
          dossierId={dialog.row.id}
          dossierNumber={dialog.row.dossierNumber}
          version={dialog.row.version ?? undefined}
          onConfirmed={() => {
            setDialog(null)
            toast.showSuccess(t('dossiers.lifecycle.confirmed'))
            reload()
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === 'cancel' && (
        <DossierReasonDialog
          title={`${t('dossiers.lifecycle.cancelTitle')} — ${dialog.row.dossierNumber}`}
          intro={t('dossiers.lifecycle.cancelIntro')}
          reasonLabel={t('dossiers.lifecycle.cancelReason')}
          confirmLabel={t('dossiers.lifecycle.cancelTitle')}
          destructive
          onClose={() => setDialog(null)}
          onSubmit={(reason) =>
            submitLifecycle(
              () => cancelDossier(dialog.row.id, reason, dialog.row.version ?? undefined),
              t('dossiers.lifecycle.cancelled'),
              t('dossiers.lifecycle.cancelFailed'),
            )
          }
        />
      )}
      {dialog?.kind === 'reopen' && (
        <DossierReasonDialog
          title={`${t('dossiers.lifecycle.reopenTitle')} — ${dialog.row.dossierNumber}`}
          intro={t('dossiers.lifecycle.reopenIntro')}
          reasonLabel={t('dossiers.lifecycle.reopenReason')}
          confirmLabel={t('dossiers.lifecycle.reopen')}
          onClose={() => setDialog(null)}
          onSubmit={(reason) =>
            submitLifecycle(
              () => reopenDossier(dialog.row.id, reason, dialog.row.version ?? undefined),
              t('dossiers.lifecycle.reopened'),
              t('dossiers.lifecycle.reopenFailed'),
            )
          }
        />
      )}
    </div>
  )
}

/** Status badge with the confirmation metadata of a confirmed dossier on a compact second line. */
function StatusCell({ row }: { row: DossierListItem }) {
  const { t } = useLocale()
  const confirmation = dossierListConfirmation(row, t)
  return (
    <span className="dossier-list-stack" title={confirmation?.title}>
      <span>
        <Badge tone={DOSSIER_STATUS_TONE[row.status]}>{t(DOSSIER_STATUS_LABELS[row.status])}</Badge>
      </span>
      {confirmation && <span className="dossier-list-muted">{confirmation.line}</span>}
    </span>
  )
}

/** "Antwerpen → Gent · 2 opdr." — the route ends plus a compact unit count. */
function TransportCell({ row }: { row: DossierListItem }) {
  const { t } = useLocale()
  const route =
    row.firstLoadingCity || row.lastUnloadingCity ? `${row.firstLoadingCity ?? '?'} → ${row.lastUnloadingCity ?? '?'}` : null
  const count =
    row.orderCount > 0
      ? t('dossiers.list.orderCountShort', { count: row.orderCount })
      : row.activityCount > 0
        ? t('dossiers.list.activityCountShort', { count: row.activityCount })
        : null
  if (!route && !count) return <>{EMPTY}</>
  return (
    <span className="dossier-list-transport" title={route ?? undefined}>
      {route && <span className="dossier-list-truncate">{route}</span>}
      {count && <span className="dossier-list-muted">{count}</span>}
    </span>
  )
}

/**
 * Planning date with who/what drives. The summaries come from the server: one name/plate when
 * exactly one driver/vehicle is assigned, "2 chauffeurs"/"2 voertuigen" otherwise — never one
 * arbitrary driver presented as THE driver of the dossier.
 */
function PlanningCell({ row }: { row: DossierListItem }) {
  const assignment = [row.driverSummary, row.vehicleSummary].filter(Boolean).join(' · ')
  if (!row.planningDate && !assignment) return <>{EMPTY}</>
  return (
    <span className="dossier-list-stack">
      <span>{row.planningDate ? formatDate(row.planningDate) : EMPTY}</span>
      {assignment && (
        <span className="dossier-list-muted" title={assignment}>
          {assignment}
        </span>
      )}
    </span>
  )
}

/**
 * Amount plus one consistent status line (Geprijsd / Gedeeltelijk / Niet geprijsd / Gratis).
 * `agreedPriceTotal === null` means not priced and never renders as € 0,00; a real € 0,00 stays
 * visible and keeps its ⚠ ("bewust?"). A partially priced amount is a sum over the priced units,
 * not a dossier total — the status line says so.
 */
function PriceCell({ row }: { row: DossierListItem }) {
  const { t } = useLocale()
  const status = dossierListPriceStatus(row)
  if (row.agreedPriceTotal === null && !status) return <>{EMPTY}</>
  const counts = { priced: row.pricedActivityCount, total: row.billableActivityCount }
  return (
    <span className="dossier-list-price">
      {row.agreedPriceTotal !== null && (
        <span className="dossier-list-amount">
          {euro(row.agreedPriceTotal)}
          {row.zeroPricedActivityCount > 0 && (
            <span
              className="dossier-list-zero"
              role="img"
              aria-label={t('dossierSheet.list.zeroPricedTitle', { count: row.zeroPricedActivityCount })}
              title={t('dossierSheet.list.zeroPricedTitle', { count: row.zeroPricedActivityCount })}
            >
              ⚠
            </span>
          )}
        </span>
      )}
      {status && (
        <span
          className="dossier-list-price-status"
          data-status={status}
          title={status === 'PartiallyPriced' ? t('dossierSheet.list.partialPricedTitle', counts) : undefined}
        >
          {t(`dossiers.list.priceStatus.${status}`, counts)}
        </span>
      )}
    </span>
  )
}
