import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
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

  return (
    <div>
      <Breadcrumbs items={[{ label: t('warehousing.trace.breadcrumbWarehouse') }, { label: t('warehousing.trace.breadcrumbTrace') }]} />
      <PageHeader
        title={t('warehousing.trace.title')}
        subtitle={t('warehousing.trace.subtitle')}
      />

      <form className="wh-trace-bar" onSubmit={(e) => void scan(e)}>
        <input
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
            <select value={scanType} onChange={(e) => setScanType(e.target.value as WarehouseScanType)} aria-label={t('warehousing.trace.scanTypeAria')} disabled={busy}>
              {(Object.keys(SCAN_TYPE_LABELS) as WarehouseScanType[]).map((value) => (
                <option key={value} value={value}>{t(SCAN_TYPE_LABELS[value])}</option>
              ))}
            </select>
            <select value={warehouseId} onChange={(e) => { setWarehouseId(e.target.value); setLocationId('') }} aria-label={t('warehousing.trace.warehouseAria')} disabled={busy}>
              {warehouses.map((w) => (
                <option key={w.id} value={w.id}>{w.name}</option>
              ))}
            </select>
            <select value={locationId} onChange={(e) => setLocationId(e.target.value)} aria-label={t('warehousing.trace.locationAria')} disabled={busy}>
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
              <Badge tone="info">{trace.lifecycleStatus}</Badge>
              {trace.exceptionStatus !== 'None' && <Badge tone="warning">{trace.exceptionStatus}</Badge>}
            </div>
          </div>
          <p>
            {t('warehousing.trace.location')}{' '}
            {trace.locationCode
              ? <strong>{trace.warehouseName} · {trace.locationCode} — {trace.locationName}</strong>
              : <span className="wh-muted">{t('warehousing.trace.noLocationRegistered')}</span>}
          </p>
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>{t('warehousing.trace.eventHeader')}</th>
                <th>{t('warehousing.trace.timeHeader')}</th>
                <th>{t('warehousing.trace.locationHeader')}</th>
                <th>{t('warehousing.trace.tripHeader')}</th>
              </tr>
            </thead>
            <tbody>
              {trace.lastEvents.map((event, index) => (
                <tr key={index}>
                  <td>{event.eventType}</td>
                  <td>{formatDateTime(event.occurredAt)}</td>
                  <td>{event.locationCode ?? '—'}</td>
                  <td>{event.tripNumber ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
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
        <div className="wh-trace-bar">
          <select value={storageCustomerId} onChange={(e) => setStorageCustomerId(e.target.value)} aria-label={t('warehousing.trace.customerAria')}>
            <option value="">{t('warehousing.trace.selectCustomer')}</option>
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>{customer.name}</option>
            ))}
          </select>
          <input type="date" value={storageFrom} onChange={(e) => setStorageFrom(e.target.value)} aria-label={t('warehousing.trace.fromAria')} />
          <input type="date" value={storageTo} onChange={(e) => setStorageTo(e.target.value)} aria-label={t('warehousing.trace.toAria')} />
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
              <table className="issued-items-table">
                <thead>
                  <tr>
                    <th>{t('warehousing.trace.orderHeader')}</th>
                    <th>{t('warehousing.trace.colliHeader')}</th>
                    <th>{t('warehousing.trace.palletDaysHeader')}</th>
                  </tr>
                </thead>
                <tbody>
                  {storage.perOrder.map((row) => (
                    <tr key={row.transportOrderId}>
                      <td>{row.orderNumber}</td>
                      <td>{row.packageCount}</td>
                      <td>{row.palletDays}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
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
