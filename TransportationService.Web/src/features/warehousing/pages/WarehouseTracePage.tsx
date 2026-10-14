import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import '../../../components/ui/FilterBar.css'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  getCustomerStorage,
  getWarehouseOverview,
  listWarehouseLocations,
  listWarehouses,
  submitWarehouseScan,
  traceWarehousePackage,
  type StorageBilling,
  type WarehouseLocation,
  type WarehouseOverview,
  type WarehouseScanType,
  type WarehouseTrace,
} from '../api/warehousingApi'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import type { Warehouse } from '../types'
import { formatDateTime } from '../../../utils/dates'
import { labelFor } from '../../../utils/labels'
import './warehousing.css'

/** Vertaalsleutels — renderen als t(SCAN_TYPE_LABELS[type]). */
const SCAN_TYPE_LABELS: Record<WarehouseScanType, string> = {
  Received: 'warehousing.scanType.Received',
  Moved: 'warehousing.scanType.Moved',
  Staged: 'warehousing.scanType.Staged',
  Return: 'warehousing.scanType.Return',
}

/**
 * Wave 4 §3+§4: het magazijnstation zonder rit. Eén barcode-invoer beantwoordt "waar is X"
 * (trace) en voert de trip-loze scans uit (ontvangst, verplaatsen, klaarzetten, retour);
 * daaronder het magazijnoverzicht: wat staat waar, wat had vandaag buiten gemoeten en wat
 * wacht op morgen.
 */
export function WarehouseTracePage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showError } = useToast()
  const canScan = hasPermission('scanning.execute')

  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [warehouseId, setWarehouseId] = useState('')
  const [locations, setLocations] = useState<WarehouseLocation[]>([])
  const [locationId, setLocationId] = useState('')
  const [barcode, setBarcode] = useState('')
  const [scanType, setScanType] = useState<WarehouseScanType>('Received')
  const [trace, setTrace] = useState<WarehouseTrace | null>(null)
  const [traceMissing, setTraceMissing] = useState(false)
  const [feedback, setFeedback] = useState<{ level: string; message: string } | null>(null)
  const [overview, setOverview] = useState<WarehouseOverview | null>(null)
  const [busy, setBusy] = useState(false)

  // Wave 5 §2: opslag per klant en periode (pallet-dagen uit de bewegingsklok).
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [storageCustomerId, setStorageCustomerId] = useState('')
  const [storageFrom, setStorageFrom] = useState(() => new Date().toISOString().slice(0, 8) + '01')
  const [storageTo, setStorageTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [storage, setStorage] = useState<StorageBilling | null>(null)

  useEffect(() => {
    listWarehouses()
      .then((data) => {
        setWarehouses(data)
        if (data.length > 0) setWarehouseId((current) => current || data[0].id)
      })
      .catch(() => {})
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomers(result.items))
      .catch(() => {})
  }, [])

  async function lookupStorage() {
    if (!storageCustomerId || !storageFrom || !storageTo) return
    try {
      setStorage(await getCustomerStorage(storageCustomerId, storageFrom, storageTo))
    } catch (err) {
      showError(localizeApiError(t, err, t('warehousing.trace.storageFailed')))
    }
  }

  useEffect(() => {
    if (!warehouseId) return
    listWarehouseLocations(warehouseId)
      .then(setLocations)
      .catch(() => setLocations([]))
    getWarehouseOverview(warehouseId)
      .then(setOverview)
      .catch(() => setOverview(null))
  }, [warehouseId])

  async function lookup() {
    if (!barcode.trim()) return
    setTraceMissing(false)
    try {
      setTrace(await traceWarehousePackage(barcode.trim()))
    } catch {
      setTrace(null)
      setTraceMissing(true)
    }
  }

  async function scan(event: FormEvent) {
    event.preventDefault()
    if (!barcode.trim()) return
    if (scanType === 'Moved' && !locationId) {
      showError(t('warehousing.trace.chooseTargetLocation'))
      return
    }
    setBusy(true)
    setFeedback(null)
    try {
      const result = await submitWarehouseScan({
        barcode: barcode.trim(),
        scanType,
        warehouseLocationId: locationId || null,
        clientEventId: crypto.randomUUID(),
      })
      setFeedback({ level: result.level, message: result.message })
      await lookup()
      if (warehouseId) {
        getWarehouseOverview(warehouseId).then(setOverview).catch(() => {})
      }
    } catch (err) {
      showError(localizeApiError(t, err, t('warehousing.trace.scanFailed')))
    } finally {
      setBusy(false)
    }
  }

  const activeLocations = locations.filter((l) => l.isActive)

  type TraceEvent = WarehouseTrace['lastEvents'][number]
  const eventColumns: Column<TraceEvent & { index: number }>[] = [
    { key: 'event', header: t('warehousing.trace.eventHeader'), render: (event) => labelFor(t, event.eventType, 'packages.eventType', 'packages.events') },
    { key: 'time', header: t('warehousing.trace.timeHeader'), render: (event) => <span className="nowrap">{formatDateTime(event.occurredAt)}</span> },
    { key: 'location', header: t('warehousing.trace.locationHeader'), render: (event) => event.locationCode ?? '—' },
    { key: 'trip', header: t('warehousing.trace.tripHeader'), render: (event) => event.tripNumber ?? '—' },
  ]
  type StorageRow = StorageBilling['perOrder'][number]
  const storageColumns: Column<StorageRow>[] = [
    { key: 'order', header: t('warehousing.trace.orderHeader'), render: (row) => <span className="nowrap">{row.orderNumber}</span> },
    { key: 'colli', header: t('warehousing.trace.colliHeader'), align: 'right', render: (row) => row.packageCount },
    { key: 'palletDays', header: t('warehousing.trace.palletDaysHeader'), align: 'right', render: (row) => row.palletDays },
  ]

  return (
    <div className="wh-page">
      <Breadcrumbs items={[{ label: t('warehousing.trace.breadcrumbWarehouse') }, { label: t('warehousing.trace.breadcrumbTrace') }]} />
      <PageHeader
        title={t('warehousing.trace.title')}
        subtitle={t('warehousing.trace.subtitle')}
      />

      <form className="ui-filter-bar wh-trace-bar" onSubmit={(e) => void scan(e)} aria-label={t('warehousing.trace.scanBarAria')}>
        <input
          className="ui-filter-search"
          value={barcode}
          onChange={(e) => setBarcode(e.target.value)}
          placeholder={t('warehousing.trace.barcodePlaceholder')}
          aria-label={t('warehousing.trace.barcodeAria')}
          autoFocus
          disabled={busy}
        />
        <Button type="button" variant="secondary" onClick={() => void lookup()} disabled={busy || !barcode.trim()}>
          {t('warehousing.trace.search')}
        </Button>
        {canScan && (
          <>
            <select className="ui-filter-select" value={scanType} onChange={(e) => setScanType(e.target.value as WarehouseScanType)} aria-label={t('warehousing.trace.scanTypeAria')} disabled={busy}>
              {(Object.keys(SCAN_TYPE_LABELS) as WarehouseScanType[]).map((value) => (
                <option key={value} value={value}>{t(SCAN_TYPE_LABELS[value])}</option>
              ))}
            </select>
            <select className="ui-filter-select" value={warehouseId} onChange={(e) => { setWarehouseId(e.target.value); setLocationId('') }} aria-label={t('warehousing.trace.warehouseAria')} disabled={busy}>
              {warehouses.map((w) => (
                <option key={w.id} value={w.id}>{w.name}</option>
              ))}
            </select>
            <select className="ui-filter-select" value={locationId} onChange={(e) => setLocationId(e.target.value)} aria-label={t('warehousing.trace.locationAria')} disabled={busy}>
              <option value="">{t('warehousing.trace.noLocation')}</option>
              {activeLocations.map((l) => (
                <option key={l.id} value={l.id}>{l.code} — {l.name}</option>
              ))}
            </select>
            <Button type="submit" disabled={busy || !barcode.trim()}>
              {t(SCAN_TYPE_LABELS[scanType])}
            </Button>
          </>
        )}
      </form>

      {feedback && (
        <p className={`wh-trace-feedback wh-trace-feedback-${feedback.level.toLowerCase()}`} role="status">
          {feedback.message}
        </p>
      )}
      {traceMissing && <p className="wh-muted">{t('warehousing.trace.notFound')}</p>}

      {trace && (
        <section className="wh-card">
          <div className="wh-card-head">
            <div>
              <h2>{trace.packageNumber}</h2>
              <p className="wh-muted">
                {trace.orderNumber} · {trace.customerName}
                {trace.description && ` · ${trace.description}`}
              </p>
            </div>
            <div className="wh-card-actions">
              <Badge tone="info">{labelFor(t, trace.lifecycleStatus, 'packages.status', 'packages.status.statusCode')}</Badge>
              {trace.exceptionStatus !== 'None' && <Badge tone="warning">{labelFor(t, trace.exceptionStatus, 'packages.exceptionState', 'exceptions.status')}</Badge>}
            </div>
          </div>
          <p>
            {t('warehousing.trace.location')}{' '}
            {trace.locationCode
              ? <strong>{trace.warehouseName} · {trace.locationCode} — {trace.locationName}</strong>
              : <span className="wh-muted">{t('warehousing.trace.noLocationRegistered')}</span>}
          </p>
          <DataTable
            columns={eventColumns}
            rows={trace.lastEvents.map((event, index) => ({ ...event, index }))}
            rowKey={(event) => String(event.index)}
            density="compact"
          />
        </section>
      )}

      {overview && (
        <section className="wh-card">
          <h2>{t('warehousing.trace.overviewTitle', { warehouse: overview.warehouseName })}</h2>
          {overview.shouldHaveLeftToday.length > 0 && (
            <p className="wh-trace-alert" role="alert">
              {t('warehousing.trace.shouldHaveLeft', { packages: overview.shouldHaveLeftToday.map((p) => p.packageNumber).join(', ') })}
            </p>
          )}
          {overview.waitsForTomorrow.length > 0 && (
            <p className="wh-muted">
              {t('warehousing.trace.waitsForTomorrow', { packages: overview.waitsForTomorrow.map((p) => p.packageNumber).join(', ') })}
            </p>
          )}
          {overview.locations.every((l) => l.packages.length === 0) && (
            <p className="wh-muted">{t('warehousing.trace.noPackages')}</p>
          )}
          {overview.locations.filter((l) => l.packages.length > 0).map((location) => (
            <div key={location.locationId} className="wh-location-zone">
              <span>
                <strong>{location.code}</strong> — {location.name}:{' '}
                {location.packages.map((p) => `${p.packageNumber} (${p.orderNumber})`).join(', ')}
              </span>
            </div>
          ))}
        </section>
      )}

      <section className="wh-card">
        <h2>{t('warehousing.trace.storageTitle')}</h2>
        <p className="wh-muted">{t('warehousing.trace.storageHint')}</p>
        <div className="ui-filter-bar wh-trace-bar" role="group" aria-label={t('warehousing.trace.storageBarAria')}>
          <select className="ui-filter-select" value={storageCustomerId} onChange={(e) => setStorageCustomerId(e.target.value)} aria-label={t('warehousing.trace.customerAria')}>
            <option value="">{t('warehousing.trace.selectCustomer')}</option>
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>{customer.name}</option>
            ))}
          </select>
          <input className="ui-filter-select" type="date" value={storageFrom} onChange={(e) => setStorageFrom(e.target.value)} aria-label={t('warehousing.trace.fromAria')} />
          <input className="ui-filter-select" type="date" value={storageTo} onChange={(e) => setStorageTo(e.target.value)} aria-label={t('warehousing.trace.toAria')} />
          <Button type="button" variant="secondary" onClick={() => void lookupStorage()} disabled={!storageCustomerId}>
            {t('warehousing.trace.calculate')}
          </Button>
        </div>
        {storage && (
          <div>
            <p>
              {t('warehousing.trace.total')} <strong>{t('warehousing.trace.palletDays', { count: storage.totalPalletDays })}</strong>
              {storage.openStays > 0 && (
                <span className="wh-muted"> · {t('warehousing.trace.stillPresent', { count: storage.openStays })}</span>
              )}
            </p>
            {storage.perOrder.length > 0 && (
              <DataTable
                columns={storageColumns}
                rows={storage.perOrder}
                rowKey={(row) => row.transportOrderId}
                density="compact"
              />
            )}
            {storage.perWarehouse.length > 1 && (
              <p className="wh-muted">
                {storage.perWarehouse.map((w) => `${w.warehouseName}: ${w.palletDays}`).join(' · ')}
              </p>
            )}
          </div>
        )}
      </section>
    </div>
  )
}
