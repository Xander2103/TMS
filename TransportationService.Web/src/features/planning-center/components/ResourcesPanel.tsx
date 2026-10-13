import { useMemo, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import type { TranslateFn } from '../../../i18n/localeContext'
import { formatInteger } from '../../../utils/numbers'
import {
  DRAG_MIME, encodeDragPayload,
  type PlanningDriver, type PlanningResources, type PlanningTrailer, type PlanningVehicle,
} from '../types'

type ResourceTab = 'drivers' | 'vehicles' | 'trailers'

interface ResourcesPanelProps {
  resources: PlanningResources | null
  isLoading: boolean
  /** Ids of pinned resources (pinned-first ordering, user preference). */
  pinnedIds: ReadonlySet<string>
  onTogglePin: (entityType: 'Driver' | 'Vehicle' | 'Trailer', id: string, label: string) => void
}

/** Right zone: draggable drivers / vehicles / trailers with availability context. */
export function ResourcesPanel({ resources, isLoading, pinnedIds, onTogglePin }: ResourcesPanelProps) {
  const { t } = useLocale()
  const [tab, setTab] = useState<ResourceTab>('drivers')
  const [search, setSearch] = useState('')
  const [onlyAvailable, setOnlyAvailable] = useState(false)

  const term = search.trim().toLowerCase()

  const drivers = useMemo(() => {
    const list = (resources?.drivers ?? [])
      .filter((d) => !term || d.name.toLowerCase().includes(term) || d.driverNumber.toLowerCase().includes(term))
      .filter((d) => !onlyAvailable || (d.isActive && !d.isBlocked && d.assignments.length === 0 && d.absences.length === 0))
    return sortPinnedFirst(list, pinnedIds, (d) => d.id)
  }, [resources, term, onlyAvailable, pinnedIds])

  const vehicles = useMemo(() => {
    const list = (resources?.vehicles ?? [])
      .filter((v) => !term || v.internalNumber.toLowerCase().includes(term) || v.licensePlate.toLowerCase().includes(term))
      .filter((v) => !onlyAvailable || (v.isActive && v.operationalStatus === 'Available' && v.assignments.length === 0))
    return sortPinnedFirst(list, pinnedIds, (v) => v.id)
  }, [resources, term, onlyAvailable, pinnedIds])

  const trailers = useMemo(() => {
    const list = (resources?.trailers ?? [])
      .filter((tr) => !term || tr.internalNumber.toLowerCase().includes(term) || tr.licensePlate.toLowerCase().includes(term))
      .filter((tr) => !onlyAvailable || (tr.isActive && tr.operationalStatus === 'Available' && tr.assignments.length === 0))
    return sortPinnedFirst(list, pinnedIds, (tr) => tr.id)
  }, [resources, term, onlyAvailable, pinnedIds])

  return (
    <section className="pc-panel pc-resources" aria-label={t('planningCenter.resources.label')}>
      <header className="pc-panel-header">
        <h2>{t('planningCenter.resources.title')}</h2>
      </header>
      <div className="pc-tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'drivers'} className={tab === 'drivers' ? 'pc-tab-active' : ''} onClick={() => setTab('drivers')}>
          {t('planningCenter.resources.tabDrivers')}
        </button>
        <button role="tab" aria-selected={tab === 'vehicles'} className={tab === 'vehicles' ? 'pc-tab-active' : ''} onClick={() => setTab('vehicles')}>
          {t('planningCenter.resources.tabVehicles')}
        </button>
        <button role="tab" aria-selected={tab === 'trailers'} className={tab === 'trailers' ? 'pc-tab-active' : ''} onClick={() => setTab('trailers')}>
          {t('planningCenter.resources.tabTrailers')}
        </button>
      </div>
      <div className="pc-filters">
        <input
          type="search"
          placeholder={t('planningCenter.resources.searchPlaceholder')}
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          aria-label={t('planningCenter.resources.searchLabel')}
        />
        <label className="pc-check">
          <input type="checkbox" checked={onlyAvailable} onChange={(event) => setOnlyAvailable(event.target.checked)} />
          {t('planningCenter.resources.onlyAvailable')}
        </label>
      </div>
      <div className="pc-resource-list" role="list">
        {isLoading && <p className="pc-muted">{t('planningCenter.resources.loading')}</p>}
        {!isLoading && tab === 'drivers' && drivers.map((driver) => (
          <DriverCard key={driver.id} driver={driver} pinned={pinnedIds.has(driver.id)} onTogglePin={onTogglePin} t={t} />
        ))}
        {!isLoading && tab === 'vehicles' && vehicles.map((vehicle) => (
          <VehicleCard key={vehicle.id} vehicle={vehicle} pinned={pinnedIds.has(vehicle.id)} onTogglePin={onTogglePin} t={t} />
        ))}
        {!isLoading && tab === 'trailers' && trailers.map((trailer) => (
          <TrailerCard key={trailer.id} trailer={trailer} pinned={pinnedIds.has(trailer.id)} onTogglePin={onTogglePin} t={t} />
        ))}
      </div>
    </section>
  )
}

function sortPinnedFirst<T>(items: T[], pinned: ReadonlySet<string>, idOf: (item: T) => string): T[] {
  return [...items].sort((a, b) => Number(pinned.has(idOf(b))) - Number(pinned.has(idOf(a))))
}

function PinButton({ pinned, onClick, t }: { pinned: boolean; onClick: () => void; t: TranslateFn }) {
  const label = pinned ? t('planningCenter.resources.unpin') : t('planningCenter.resources.pin')
  return (
    <button
      type="button"
      className={`pc-pin${pinned ? ' pc-pin-active' : ''}`}
      onClick={(event) => {
        event.stopPropagation()
        onClick()
      }}
      aria-label={label}
      title={label}
    >
      ★
    </button>
  )
}

function DriverCard({ driver, pinned, onTogglePin, t }: {
  driver: PlanningDriver
  pinned: boolean
  onTogglePin: ResourcesPanelProps['onTogglePin']
  t: TranslateFn
}) {
  const busyDates = driver.assignments.length
  return (
    <article
      role="listitem"
      className="pc-resource-card"
      draggable
      onDragStart={(event) => {
        event.dataTransfer.setData(DRAG_MIME, encodeDragPayload({ kind: 'driver', id: driver.id, label: driver.name }))
        event.dataTransfer.effectAllowed = 'link'
      }}
    >
      <div className="pc-resource-head">
        <strong>{driver.name}</strong>
        <PinButton pinned={pinned} onClick={() => onTogglePin('Driver', driver.id, driver.name)} t={t} />
      </div>
      <p className="pc-resource-meta">
        <span>{driver.driverNumber}</span>
        {driver.fixedVehicleNumber && <span>{t('planningCenter.resources.fixed', { label: driver.fixedVehicleNumber })}</span>}
      </p>
      <div className="pc-resource-badges">
        {!driver.isActive && <Badge tone="danger">{t('planningCenter.resources.inactive')}</Badge>}
        {driver.isBlocked && <Badge tone="danger">{t('planningCenter.resources.blocked')}</Badge>}
        {driver.absences.length > 0 && <Badge tone="warning">{t('planningCenter.resources.absent', { count: driver.absences.length })}</Badge>}
        {driver.qualificationBlocks.length > 0 && (
          <Badge tone="danger">{t('planningCenter.resources.qualificationBlocks', { list: driver.qualificationBlocks.join(', ') })}</Badge>
        )}
        {driver.qualificationWarnings.length > 0 && (
          <Badge tone="warning">{t('planningCenter.resources.qualificationWarnings', { list: driver.qualificationWarnings.join(', ') })}</Badge>
        )}
        {busyDates > 0
          ? <Badge tone="info">{t('planningCenter.resources.trips', { count: busyDates })}</Badge>
          : <Badge tone="success">{t('planningCenter.resources.free')}</Badge>}
      </div>
    </article>
  )
}

function VehicleCard({ vehicle, pinned, onTogglePin, t }: {
  vehicle: PlanningVehicle
  pinned: boolean
  onTogglePin: ResourcesPanelProps['onTogglePin']
  t: TranslateFn
}) {
  return (
    <article
      role="listitem"
      className="pc-resource-card"
      draggable
      onDragStart={(event) => {
        event.dataTransfer.setData(DRAG_MIME, encodeDragPayload({ kind: 'vehicle', id: vehicle.id, label: vehicle.internalNumber }))
        event.dataTransfer.effectAllowed = 'link'
      }}
    >
      <div className="pc-resource-head">
        <strong>{vehicle.internalNumber}</strong>
        <PinButton pinned={pinned} onClick={() => onTogglePin('Vehicle', vehicle.id, vehicle.internalNumber)} t={t} />
      </div>
      <p className="pc-resource-meta">
        <span>{vehicle.licensePlate}</span>
        {vehicle.payloadKg !== null && <span>{formatInteger(vehicle.payloadKg)} kg</span>}
        {vehicle.fixedDriverName && <span>{t('planningCenter.resources.fixed', { label: vehicle.fixedDriverName })}</span>}
      </p>
      <div className="pc-resource-badges">
        {!vehicle.isActive && <Badge tone="danger">{t('planningCenter.resources.inactive')}</Badge>}
        {vehicle.operationalStatus !== 'Available' && <Badge tone="warning">{vehicle.operationalStatus}</Badge>}
        {vehicle.adrSuitable && <Badge tone="info">ADR</Badge>}
        {vehicle.hasCrane && <Badge tone="info">{t('planningCenter.resources.crane')}</Badge>}
        {vehicle.hasTailLift && <Badge tone="info">{t('planningCenter.resources.tailLift')}</Badge>}
        {vehicle.hasRefrigeration && <Badge tone="info">{t('planningCenter.resources.refrigeration')}</Badge>}
        {vehicle.overdueMaintenanceCount > 0 && <Badge tone="danger">{t('planningCenter.resources.maintenanceOverdue')}</Badge>}
        {vehicle.overdueInspectionCount > 0 && <Badge tone="danger">{t('planningCenter.resources.inspectionOverdue')}</Badge>}
        {vehicle.assignments.length > 0
          ? <Badge tone="info">{t('planningCenter.resources.trips', { count: vehicle.assignments.length })}</Badge>
          : <Badge tone="success">{t('planningCenter.resources.free')}</Badge>}
      </div>
    </article>
  )
}

function TrailerCard({ trailer, pinned, onTogglePin, t }: {
  trailer: PlanningTrailer
  pinned: boolean
  onTogglePin: ResourcesPanelProps['onTogglePin']
  t: TranslateFn
}) {
  return (
    <article
      role="listitem"
      className="pc-resource-card"
      draggable
      onDragStart={(event) => {
        event.dataTransfer.setData(DRAG_MIME, encodeDragPayload({ kind: 'trailer', id: trailer.id, label: trailer.internalNumber }))
        event.dataTransfer.effectAllowed = 'link'
      }}
    >
      <div className="pc-resource-head">
        <strong>{trailer.internalNumber}</strong>
        <PinButton pinned={pinned} onClick={() => onTogglePin('Trailer', trailer.id, trailer.internalNumber)} t={t} />
      </div>
      <p className="pc-resource-meta">
        <span>{trailer.licensePlate}</span>
        {trailer.capacityKg !== null && <span>{formatInteger(trailer.capacityKg)} kg</span>}
      </p>
      <div className="pc-resource-badges">
        {!trailer.isActive && <Badge tone="danger">{t('planningCenter.resources.inactive')}</Badge>}
        {trailer.operationalStatus !== 'Available' && <Badge tone="warning">{trailer.operationalStatus}</Badge>}
        {trailer.adrSuitable && <Badge tone="info">ADR</Badge>}
        {trailer.hasRefrigeration && <Badge tone="info">{t('planningCenter.resources.refrigeration')}</Badge>}
        {trailer.overdueMaintenanceCount > 0 && <Badge tone="danger">{t('planningCenter.resources.maintenanceOverdue')}</Badge>}
        {trailer.overdueInspectionCount > 0 && <Badge tone="danger">{t('planningCenter.resources.inspectionOverdue')}</Badge>}
        {trailer.assignments.length > 0
          ? <Badge tone="info">{t('planningCenter.resources.trips', { count: trailer.assignments.length })}</Badge>
          : <Badge tone="success">{t('planningCenter.resources.free')}</Badge>}
      </div>
    </article>
  )
}
