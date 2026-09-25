import { useCallback, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column, type SortState } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { searchDrivers } from '../../drivers/api/driversApi'
import { euro } from '../../invoices/types'
import { getTrailerOptions } from '../../trailers/api/trailersApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import { listActivityTypes } from '../api/activityTypesApi'
import { cancelDossier, searchDossiers } from '../api/dossiersApi'
import { DossierConfirmDialog } from '../components/DossierConfirmDialog'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierListItem, type DossierSortKey, type DossierStatus } from '../types'
import {
  activeFilterKeys,
  EMPTY_DOSSIER_FILTERS,
  parseDossierSearchParams,
  toSearchDossiersParams,
  toSearchParams,
  todayRange,
  weekRangeOf,
  type DossierFilterKey,
  type DossierSearchFilters,
} from './dossierSearchParams'
import './dossiers.css'

const EMPTY = '—'
const PAGE_SIZE = 25

/** Filters that live in the collapsible "Meer filters" panel (the bar shows the rest). */
const ADVANCED_KEYS: readonly DossierFilterKey[] = [
  'dossierNumber',
  'orderNumber',
  'customerReference',
  'customerNumber',
  'confirmationSource',
  'confirmedFrom',
  'confirmedTo',
  'createdFrom',
  'createdTo',
  'planningFrom',
  'planningTo',
  'driverId',
  'vehicleId',
  'licensePlate',
  'trailerId',
  'activityTypeId',
  'loadingCity',
  'unloadingCity',
  'postalCode',
  'countryCode',
  'priceStatus',
  'invoiceStatus',
  'hasCmr',
]

type Option = SearchableSelectOption

interface Lookups {
  drivers: Option[]
  vehicles: Option[]
  trailers: Option[]
  activityTypes: Option[]
}

const EMPTY_LOOKUPS: Lookups = { drivers: [], vehicles: [], trailers: [], activityTypes: [] }

/** Tekstcel die bij overloop afkapt met een ellipsis en de volledige waarde als tooltip toont. */
function TruncatedCell({ value }: { value: string | null }) {
  if (!value) return <>{EMPTY}</>
  return (
    <span className="dossier-list-truncate" title={value}>
      {value}
    </span>
  )
}

/**
 * Dossiers: paged, URL-synced search over GET /api/dossiers/search. Every filter, the sort and
 * the page live in the querystring, so refresh/back/share keep the exact view. The bar holds the
 * primary filters; "Meer filters" reveals the grouped advanced panel. Row actions (bevestigen,
 * annuleren) reuse the lifecycle endpoints of the detail page.
 */
export function DossiersPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const toast = useToast()
  const canManage = hasPermission('dossiers.manage')

  const [searchParams, setSearchParams] = useSearchParams()
  const filters = useMemo(() => parseDossierSearchParams(searchParams), [searchParams])

  /**
   * Any filter change restarts at page 1 (a shrunken result set must not strand the user past
   * the end); an explicit `page` in the patch wins. Typing replaces the history entry so the
   * back button skips keystrokes; discrete choices push, so back undoes them one by one.
   */
  const apply = useCallback(
    (patch: Partial<DossierSearchFilters>, options: { replace?: boolean } = {}) => {
      setSearchParams(
        (current) => toSearchParams({ ...parseDossierSearchParams(current), ...patch, page: patch.page ?? 1 }),
        options,
      )
    },
    [setSearchParams],
  )

  const activeKeys = activeFilterKeys(filters)
  const hasActiveFilters = activeKeys.length > 0 || filters.search !== ''
  const [advancedOpen, setAdvancedOpen] = useState(() => activeKeys.some((key) => ADVANCED_KEYS.includes(key)))

  // --- lookups ---------------------------------------------------------------------------------
  const [customerOptions, setCustomerOptions] = useState<Option[]>([])
  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name })))
      })
      .catch(() => {
        /* filter stays usable without options */
      })
    return () => {
      mounted = false
    }
  }, [])

  // Advanced lookups load once, the first time they are needed (panel open or an id filter set in
  // the URL, so its chip can show a name instead of an id).
  const needsLookups = advancedOpen || Boolean(filters.driverId || filters.vehicleId || filters.trailerId || filters.activityTypeId)
  const [lookups, setLookups] = useState<Lookups>(EMPTY_LOOKUPS)
  const lookupsRequested = useRef(false)
  useEffect(() => {
    if (!needsLookups || lookupsRequested.current) return
    lookupsRequested.current = true
    let mounted = true
    const settle = <T,>(promise: Promise<T>, fallback: T) => promise.catch(() => fallback)
    Promise.all([
      settle(searchDrivers({ isActive: true, page: 1, pageSize: 200 }).then((r) => r.items), []),
      settle(getVehicleOptions(), []),
      settle(getTrailerOptions(), []),
      settle(listActivityTypes(), []),
    ]).then(([drivers, vehicles, trailers, activityTypes]) => {
      if (!mounted) return
      setLookups({
        drivers: drivers.map((d) => ({ value: d.id, label: d.fullName })),
        vehicles: vehicles.map((v) => ({ value: v.id, label: `${v.internalNumber} · ${v.licensePlate}` })),
        trailers: trailers.map((tr) => ({ value: tr.id, label: `${tr.internalNumber} · ${tr.licensePlate}` })),
        activityTypes: activityTypes.map((a) => ({ value: a.id, label: a.name })),
      })
    })
    return () => {
      mounted = false
    }
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
  const [confirmTarget, setConfirmTarget] = useState<DossierListItem | null>(null)
  const [cancelTarget, setCancelTarget] = useState<DossierListItem | null>(null)

  // --- labels ----------------------------------------------------------------------------------
  const optionLabel = (options: Option[], value: string) => options.find((o) => o.value === value)?.label ?? value

  const enumLabels: Partial<Record<DossierFilterKey, Record<string, string>>> = {
    status: {
      Open: t('dossiers.filters.open'),
      Closed: t('dossiers.filters.confirmed'),
      Cancelled: t('dossiers.filters.cancelled'),
    },
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

  function fieldLabel(key: DossierFilterKey): string {
    switch (key) {
      case 'status':
        return t('dossiers.filters.statusLabel')
      case 'customerId':
        return t('dossiers.filters.customer')
      case 'dateFrom':
        return t('dossiers.filters.dateFrom')
      case 'dateTo':
        return t('dossiers.filters.dateTo')
      default:
        return t(`dossiers.filters.fields.${key}`)
    }
  }

  function valueLabel(key: DossierFilterKey): string {
    const value = filters[key]
    switch (key) {
      case 'customerId':
        return optionLabel(customerOptions, value)
      case 'driverId':
        return optionLabel(lookups.drivers, value)
      case 'vehicleId':
        return optionLabel(lookups.vehicles, value)
      case 'trailerId':
        return optionLabel(lookups.trailers, value)
      case 'activityTypeId':
        return optionLabel(lookups.activityTypes, value)
      default:
        return enumLabels[key]?.[value] ?? value
    }
  }

  // --- columns ---------------------------------------------------------------------------------
  const columns: Column<DossierListItem>[] = [
    {
      key: 'number',
      header: t('dossiers.list.columns.number'),
      width: '7rem',
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
      width: '6.5rem',
      sortKey: 'status',
      render: (row) => <Badge tone={DOSSIER_STATUS_TONE[row.status]}>{t(DOSSIER_STATUS_LABELS[row.status])}</Badge>,
    },
    {
      key: 'customer',
      header: t('dossiers.list.columns.customer'),
      sortKey: 'customer',
      width: '11rem',
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
      width: '8rem',
      render: (row) => <TruncatedCell value={row.customerReference} />,
    },
    {
      key: 'date',
      header: t('dossiers.list.columns.date'),
      width: '6.5rem',
      sortKey: 'date',
      render: (row) => (row.dossierDate ? formatDate(row.dossierDate) : EMPTY),
    },
    {
      key: 'transport',
      header: t('dossiers.list.columns.transport'),
      width: '12rem',
      render: (row) => <TransportCell row={row} />,
    },
    {
      key: 'driver',
      header: t('dossiers.list.columns.driver'),
      width: '10rem',
      render: (row) =>
        row.driverSummary || row.vehicleSummary ? (
          <span className="dossier-list-stack">
            {row.driverSummary && <TruncatedCell value={row.driverSummary} />}
            {row.vehicleSummary && <span className="dossier-list-muted">{row.vehicleSummary}</span>}
          </span>
        ) : (
          EMPTY
        ),
    },
    {
      key: 'confirmed',
      header: t('dossiers.list.columns.confirmed'),
      width: '7rem',
      sortKey: 'confirmedAt',
      render: (row) => (row.confirmedAt ? formatDate(row.confirmedAt) : EMPTY),
    },
    {
      key: 'price',
      header: t('dossiers.list.columns.price'),
      width: '7rem',
      align: 'right',
      // `null` = nog niet geprijsd; een echte € 0,00 (geprijsd op nul) blijft zichtbaar en krijgt
      // een ⚠ (bewust? — nooit vervangt het icoon het bedrag). Een gedeeltelijk geprijsd dossier
      // (niet elke factureerbare activiteit heeft een prijs) krijgt een marker: het bedrag is een
      // som over de geprijsde eenheden, geen dossiertotaal.
      render: (row) =>
        row.agreedPriceTotal === null ? (
          EMPTY
        ) : (
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
            {row.pricedActivityCount < row.billableActivityCount && (
              <span
                className="dossier-list-partial"
                title={t('dossierSheet.list.partialPricedTitle', { priced: row.pricedActivityCount, total: row.billableActivityCount })}
              >
                {t('dossierSheet.list.partialPriced', { priced: row.pricedActivityCount, total: row.billableActivityCount })}
              </span>
            )}
          </span>
        ),
    },
    {
      key: 'invoice',
      header: t('dossiers.list.columns.invoice'),
      width: '6.5rem',
      render: (row) => (row.invoiceStatus ? t(`dossiers.list.invoice.${row.invoiceStatus}`) : EMPTY),
    },
  ]

  if (canManage) {
    columns.push({
      key: 'actions',
      header: '',
      width: '3rem',
      align: 'right',
      render: (row) => (
        <RowActions
          row={row}
          onOpen={() => navigate(`/dossiers/${row.id}`)}
          onConfirm={row.status === 'Open' ? () => setConfirmTarget(row) : undefined}
          onCancel={row.status === 'Open' ? () => setCancelTarget(row) : undefined}
        />
      ),
    })
  }

  // --- render ----------------------------------------------------------------------------------
  const statusOptions: { value: '' | DossierStatus; label: string }[] = [
    { value: '', label: t('dossiers.filters.all') },
    { value: 'Open', label: t('dossiers.filters.open') },
    { value: 'Closed', label: t('dossiers.filters.confirmed') },
    { value: 'Cancelled', label: t('dossiers.filters.cancelled') },
  ]
  const today = todayRange()
  const week = weekRangeOf()
  const rangeIs = (range: { dateFrom: string; dateTo: string }) => filters.dateFrom === range.dateFrom && filters.dateTo === range.dateTo

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
          <div className="dossier-filter-status" role="group" aria-label={t('dossiers.filters.statusLabel')}>
            {statusOptions.map((option) => (
              <button
                key={option.value || 'all'}
                type="button"
                className="dossier-filter-chip"
                aria-pressed={filters.status === option.value}
                onClick={() => apply({ status: option.value })}
              >
                {option.label}
              </button>
            ))}
          </div>
          <div className="dossier-filter-customer">
            <SearchableSelect
              ariaLabel={t('dossiers.filters.customer')}
              placeholder={t('dossiers.filters.allCustomers')}
              value={filters.customerId || null}
              onChange={(value) => apply({ customerId: value ?? '' })}
              options={customerOptions}
            />
          </div>
          <div className="dossier-filter-dates">
            <input
              type="date"
              aria-label={t('dossiers.filters.dateFrom')}
              value={filters.dateFrom}
              max={filters.dateTo || undefined}
              onChange={(e) => apply({ dateFrom: e.target.value }, { replace: true })}
            />
            <span aria-hidden="true">–</span>
            <input
              type="date"
              aria-label={t('dossiers.filters.dateTo')}
              value={filters.dateTo}
              min={filters.dateFrom || undefined}
              onChange={(e) => apply({ dateTo: e.target.value }, { replace: true })}
            />
          </div>
          <div className="dossier-filter-quick">
            <button type="button" className="dossier-filter-chip" aria-pressed={rangeIs(today)} onClick={() => apply(today)}>
              {t('dossiers.filters.today')}
            </button>
            <button type="button" className="dossier-filter-chip" aria-pressed={rangeIs(week)} onClick={() => apply(week)}>
              {t('dossiers.filters.thisWeek')}
            </button>
          </div>
          <button
            type="button"
            className="dossier-filter-more"
            aria-expanded={advancedOpen}
            aria-controls="dossier-advanced-filters"
            onClick={() => setAdvancedOpen((open) => !open)}
          >
            {advancedOpen ? t('dossiers.filters.less') : t('dossiers.filters.more')}
          </button>
        </FilterBar>

        {advancedOpen && (
          <AdvancedFilters filters={filters} lookups={lookups} onChange={apply} fieldLabel={fieldLabel} />
        )}

        {hasActiveFilters && (
          <div className="dossier-active-filters">
            {activeKeys.length > 0 && (
              <ul className="dossier-active-chips" aria-label={t('dossiers.filters.activeLabel')}>
                {activeKeys.map((key) => (
                  <li key={key} className="dossier-active-chip">
                    <span>
                      {fieldLabel(key)}: {valueLabel(key)}
                    </span>
                    <button
                      type="button"
                      aria-label={t('dossiers.filters.remove', { label: fieldLabel(key) })}
                      onClick={() => apply({ [key]: '' })}
                    >
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <button type="button" className="link-button dossier-clear-filters" onClick={() => setSearchParams(toSearchParams(EMPTY_DOSSIER_FILTERS))}>
              {t('dossiers.filters.clear')}
            </button>
          </div>
        )}
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

      {confirmTarget && (
        <DossierConfirmDialog
          dossierId={confirmTarget.id}
          dossierNumber={confirmTarget.dossierNumber}
          onConfirmed={() => {
            setConfirmTarget(null)
            toast.showSuccess(t('dossiers.lifecycle.confirmed'))
            reload()
          }}
          onClose={() => setConfirmTarget(null)}
        />
      )}
      {cancelTarget && (
        <CancelDossierDialog
          dossierId={cancelTarget.id}
          dossierNumber={cancelTarget.dossierNumber}
          onCancelled={() => {
            setCancelTarget(null)
            toast.showSuccess(t('dossiers.lifecycle.cancelled'))
            reload()
          }}
          onClose={() => setCancelTarget(null)}
        />
      )}
    </div>
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

// --- advanced filter panel -----------------------------------------------------------------------

interface AdvancedFiltersProps {
  filters: DossierSearchFilters
  lookups: Lookups
  onChange: (patch: Partial<DossierSearchFilters>, options?: { replace?: boolean }) => void
  fieldLabel: (key: DossierFilterKey) => string
}

function AdvancedFilters({ filters, lookups, onChange, fieldLabel }: AdvancedFiltersProps) {
  const { t } = useLocale()
  const idPrefix = useId()
  const id = (key: DossierFilterKey) => `${idPrefix}-${key}`

  const textField = (key: DossierFilterKey, type: 'text' | 'date' = 'text') => (
    <FormField key={key} label={fieldLabel(key)} htmlFor={id(key)}>
      <input
        id={id(key)}
        type={type}
        value={filters[key]}
        onChange={(e) => onChange({ [key]: e.target.value }, { replace: true })}
        autoComplete="off"
      />
    </FormField>
  )

  const selectField = (key: DossierFilterKey, options: { value: string; label: string }[]) => (
    <FormField key={key} label={fieldLabel(key)} htmlFor={id(key)}>
      <select id={id(key)} value={filters[key]} onChange={(e) => onChange({ [key]: e.target.value })}>
        <option value="">{t('dossiers.filters.any')}</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FormField>
  )

  const lookupField = (key: DossierFilterKey, options: Option[]) => (
    <FormField key={key} label={fieldLabel(key)} htmlFor={id(key)}>
      <SearchableSelect
        id={id(key)}
        value={filters[key] || null}
        onChange={(value) => onChange({ [key]: value ?? '' })}
        options={options}
        placeholder={t('dossiers.filters.any')}
      />
    </FormField>
  )

  const group = (name: string, children: ReactNode) => (
    <fieldset className="dossier-advanced-group">
      <legend>{t(`dossiers.filters.groups.${name}`)}</legend>
      <div className="dossier-advanced-fields">{children}</div>
    </fieldset>
  )

  return (
    <div id="dossier-advanced-filters" className="dossier-advanced">
      {group('identification', [
        textField('dossierNumber'),
        textField('orderNumber'),
        textField('customerReference'),
        textField('customerNumber'),
      ])}
      {group('lifecycle', [
        selectField('confirmationSource', [
          { value: 'Manual', label: t('dossiers.filters.options.sourceManual') },
          { value: 'Automatic', label: t('dossiers.filters.options.sourceAutomatic') },
        ]),
        textField('confirmedFrom', 'date'),
        textField('confirmedTo', 'date'),
        textField('createdFrom', 'date'),
        textField('createdTo', 'date'),
      ])}
      {group('planning', [
        textField('planningFrom', 'date'),
        textField('planningTo', 'date'),
        lookupField('driverId', lookups.drivers),
        lookupField('vehicleId', lookups.vehicles),
        textField('licensePlate'),
        lookupField('trailerId', lookups.trailers),
      ])}
      {group('transport', [
        selectField('activityTypeId', lookups.activityTypes),
        textField('loadingCity'),
        textField('unloadingCity'),
        textField('postalCode'),
        textField('countryCode'),
      ])}
      {group('commercial', [
        selectField('priceStatus', [
          { value: 'Priced', label: t('dossiers.filters.options.priced') },
          { value: 'Partial', label: t('dossiers.filters.options.partial') },
          { value: 'Unpriced', label: t('dossiers.filters.options.unpriced') },
        ]),
        selectField('invoiceStatus', [
          { value: 'NotInvoiced', label: t('dossiers.filters.options.notInvoiced') },
          { value: 'Draft', label: t('dossiers.filters.options.invoiceDraft') },
          { value: 'Sent', label: t('dossiers.filters.options.invoiceSent') },
          { value: 'Paid', label: t('dossiers.filters.options.invoicePaid') },
        ]),
      ])}
      {group('documents', [
        selectField('hasCmr', [
          { value: 'true', label: t('dossiers.filters.options.cmrYes') },
          { value: 'false', label: t('dossiers.filters.options.cmrNo') },
        ]),
      ])}
    </div>
  )
}

// --- row action menu -----------------------------------------------------------------------------

interface RowActionsProps {
  row: DossierListItem
  onOpen: () => void
  onConfirm?: () => void
  onCancel?: () => void
}

/**
 * Compact "⋯" menu per row. The popover renders through a portal (the table wrapper scrolls and
 * would clip it) and is positioned from the button's rect; it closes on outside click, Escape,
 * scroll and resize. Clicks never reach the row (which navigates).
 */
function RowActions({ row, onOpen, onConfirm, onCancel }: RowActionsProps) {
  const { t } = useLocale()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ top: number; right: number } | null>(null)
  const open = position !== null

  function toggle() {
    if (open) {
      setPosition(null)
      return
    }
    const rect = buttonRef.current?.getBoundingClientRect()
    setPosition(rect ? { top: rect.bottom + 4, right: Math.max(8, window.innerWidth - rect.right) } : { top: 0, right: 8 })
  }

  useEffect(() => {
    if (!open) return
    const close = () => setPosition(null)
    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return
      close()
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        close()
        buttonRef.current?.focus()
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
    }
  }, [open])

  const select = (action: () => void) => () => {
    setPosition(null)
    action()
  }

  return (
    <span
      className="dossier-row-actions"
      onClick={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
    >
      <button
        ref={buttonRef}
        type="button"
        className="dossier-row-actions-button"
        aria-label={t('dossiers.list.actions', { number: row.dossierNumber })}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={toggle}
      >
        ⋯
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            className="dossier-row-menu"
            role="menu"
            style={{ top: position.top, right: position.right }}
            onClick={(event) => event.stopPropagation()}
          >
            <button type="button" role="menuitem" onClick={select(onOpen)}>
              {t('dossiers.list.open')}
            </button>
            {onConfirm && (
              <button type="button" role="menuitem" onClick={select(onConfirm)}>
                {t('dossiers.lifecycle.confirm')}
              </button>
            )}
            {onCancel && (
              <button type="button" role="menuitem" className="dossier-row-menu-danger" onClick={select(onCancel)}>
                {t('dossiers.lifecycle.cancel')}
              </button>
            )}
          </div>,
          document.body,
        )}
    </span>
  )
}

// --- cancel dialog -------------------------------------------------------------------------------

interface CancelDossierDialogProps {
  dossierId: string
  dossierNumber: string
  onCancelled: () => void
  onClose: () => void
}

/** Reason prompt for "Annuleren" from the list; the detail page has its own richer flow. */
function CancelDossierDialog({ dossierId, dossierNumber, onCancelled, onClose }: CancelDossierDialogProps) {
  const { t } = useLocale()
  const [reason, setReason] = useState('')
  const [validation, setValidation] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit() {
    const trimmed = reason.trim()
    if (!trimmed) {
      setValidation(t('dossiers.lifecycle.reasonRequired'))
      return
    }
    setValidation(null)
    setError(null)
    setBusy(true)
    try {
      await cancelDossier(dossierId, trimmed)
      onCancelled()
    } catch {
      setError(t('dossiers.lifecycle.cancelFailed'))
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`${t('dossiers.lifecycle.cancelTitle')} — ${dossierNumber}`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.close')}
          </Button>
          <Button variant="danger" onClick={() => void submit()} disabled={busy}>
            {busy ? t('ui.actions.busy') : t('dossiers.lifecycle.cancelTitle')}
          </Button>
        </>
      }
    >
      <p className="dossier-cancel-intro">{t('dossiers.lifecycle.cancelIntro')}</p>
      <FormField label={t('dossiers.lifecycle.cancelReason')} htmlFor="dossier-list-cancel-reason" error={validation ?? undefined}>
        <textarea
          id="dossier-list-cancel-reason"
          rows={3}
          value={reason}
          maxLength={1000}
          onChange={(e) => setReason(e.target.value)}
          aria-invalid={validation ? true : undefined}
        />
      </FormField>
      {error && (
        <p className="dossier-cancel-error" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
