import { useMemo, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
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
      .filter((t) => !term || t.internalNumber.toLowerCase().includes(term) || t.licensePlate.toLowerCase().includes(term))
      .filter((t) => !onlyAvailable || (t.isActive && t.operationalStatus === 'Available' && t.assignments.length === 0))
    return sortPinnedFirst(list, pinnedIds, (t) => t.id)
  }, [resources, term, onlyAvailable, pinnedIds])

  return (
    <section className="pc-panel pc-resources" aria-label="Middelen">
      <header className="pc-panel-header">
        <h2>Middelen</h2>
      </header>
      <div className="pc-tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'drivers'} className={tab === 'drivers' ? 'pc-tab-active' : ''} onClick={() => setTab('drivers')}>
          Chauffeurs
        </button>
        <button role="tab" aria-selected={tab === 'vehicles'} className={tab === 'vehicles' ? 'pc-tab-active' : ''} onClick={() => setTab('vehicles')}>
          Voertuigen
        </button>
        <button role="tab" aria-selected={tab === 'trailers'} className={tab === 'trailers' ? 'pc-tab-active' : ''} onClick={() => setTab('trailers')}>
          Opleggers
        </button>
      </div>
      <div className="pc-filters">
        <input
          type="search"
          placeholder="Zoeken…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          aria-label="Zoeken in middelen"
        />
        <label className="pc-check">
          <input type="checkbox" checked={onlyAvailable} onChange={(event) => setOnlyAvailable(event.target.checked)} />
          Alleen beschikbaar
        </label>
      </div>
      <div className="pc-resource-list" role="list">
        {isLoading && <p className="pc-muted">Laden…</p>}
        {!isLoading && tab === 'drivers' && drivers.map((driver) => (
          <DriverCard key={driver.id} driver={driver} pinned={pinnedIds.has(driver.id)} onTogglePin={onTogglePin} />
        ))}
        {!isLoading && tab === 'vehicles' && vehicles.map((vehicle) => (
          <VehicleCard key={vehicle.id} vehicle={vehicle} pinned={pinnedIds.has(vehicle.id)} onTogglePin={onTogglePin} />
        ))}
        {!isLoading && tab === 'trailers' && trailers.map((trailer) => (
          <TrailerCard key={trailer.id} trailer={trailer} pinned={pinnedIds.has(trailer.id)} onTogglePin={onTogglePin} />
        ))}
      </div>
    </section>
  )
}

function sortPinnedFirst<T>(items: T[], pinned: ReadonlySet<string>, idOf: (item: T) => string): T[] {
  return [...items].sort((a, b) => Number(pinned.has(idOf(b))) - Number(pinned.has(idOf(a))))
}

function PinButton({ pinned, onClick }: { pinned: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      className={`pc-pin${pinned ? ' pc-pin-active' : ''}`}
      onClick={(event) => {
        event.stopPropagation()
        onClick()
      }}
      aria-label={pinned ? 'Losmaken' : 'Vastpinnen'}
      title={pinned ? 'Losmaken' : 'Vastpinnen'}
    >
      ★
    </button>
  )
}

function DriverCard({ driver, pinned, onTogglePin }: {
  driver: PlanningDriver
  pinned: boolean
  onTogglePin: ResourcesPanelProps['onTogglePin']
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
        <PinButton pinned={pinned} onClick={() => onTogglePin('Driver', driver.id, driver.name)} />
      </div>
      <p className="pc-resource-meta">
        <span>{driver.driverNumber}</span>
        {driver.fixedVehicleNumber && <span>Vast: {driver.fixedVehicleNumber}</span>}
      </p>
      <div className="pc-resource-badges">
        {!driver.isActive && <Badge tone="danger">Inactief</Badge>}
        {driver.isBlocked && <Badge tone="danger">Geblokkeerd</Badge>}
        {driver.absences.length > 0 && <Badge tone="warning">Afwezig ({driver.absences.length})</Badge>}
        {driver.qualificationBlocks.length > 0 && (
          <Badge tone="danger">Kwalificatie: {driver.qualificationBlocks.join(', ')}</Badge>
        )}
        {driver.qualificationWarnings.length > 0 && (
          <Badge tone="warning">Verloopt: {driver.qualificationWarnings.join(', ')}</Badge>
        )}
        {busyDates > 0 ? <Badge tone="info">{busyDates} rit(ten)</Badge> : <Badge tone="success">Vrij</Badge>}
      </div>
    </article>
  )
}

function VehicleCard({ vehicle, pinned, onTogglePin }: {
  vehicle: PlanningVehicle
  pinned: boolean
  onTogglePin: ResourcesPanelProps['onTogglePin']
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
        <PinButton pinned={pinned} onClick={() => onTogglePin('Vehicle', vehicle.id, vehicle.internalNumber)} />
      </div>
      <p className="pc-resource-meta">
        <span>{vehicle.licensePlate}</span>
        {vehicle.payloadKg !== null && <span>{vehicle.payloadKg.toLocaleString('nl-BE')} kg</span>}
        {vehicle.fixedDriverName && <span>Vast: {vehicle.fixedDriverName}</span>}
      </p>
      <div className="pc-resource-badges">
        {!vehicle.isActive && <Badge tone="danger">Inactief</Badge>}
        {vehicle.operationalStatus !== 'Available' && <Badge tone="warning">{vehicle.operationalStatus}</Badge>}
        {vehicle.adrSuitable && <Badge tone="info">ADR</Badge>}
        {vehicle.hasCrane && <Badge tone="info">Kraan</Badge>}
        {vehicle.hasTailLift && <Badge tone="info">Laadklep</Badge>}
        {vehicle.hasRefrigeration && <Badge tone="info">Koeling</Badge>}
        {vehicle.overdueMaintenanceCount > 0 && <Badge tone="danger">Onderhoud over tijd</Badge>}
        {vehicle.overdueInspectionCount > 0 && <Badge tone="danger">Keuring over tijd</Badge>}
        {vehicle.assignments.length > 0
          ? <Badge tone="info">{vehicle.assignments.length} rit(ten)</Badge>
          : <Badge tone="success">Vrij</Badge>}
      </div>
    </article>
  )
}

function TrailerCard({ trailer, pinned, onTogglePin }: {
  trailer: PlanningTrailer
  pinned: boolean
  onTogglePin: ResourcesPanelProps['onTogglePin']
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
        <PinButton pinned={pinned} onClick={() => onTogglePin('Trailer', trailer.id, trailer.internalNumber)} />
      </div>
      <p className="pc-resource-meta">
        <span>{trailer.licensePlate}</span>
        {trailer.capacityKg !== null && <span>{trailer.capacityKg.toLocaleString('nl-BE')} kg</span>}
      </p>
      <div className="pc-resource-badges">
        {!trailer.isActive && <Badge tone="danger">Inactief</Badge>}
        {trailer.operationalStatus !== 'Available' && <Badge tone="warning">{trailer.operationalStatus}</Badge>}
        {trailer.adrSuitable && <Badge tone="info">ADR</Badge>}
        {trailer.hasRefrigeration && <Badge tone="info">Koeling</Badge>}
        {trailer.overdueMaintenanceCount > 0 && <Badge tone="danger">Onderhoud over tijd</Badge>}
        {trailer.overdueInspectionCount > 0 && <Badge tone="danger">Keuring over tijd</Badge>}
        {trailer.assignments.length > 0
          ? <Badge tone="info">{trailer.assignments.length} rit(ten)</Badge>
          : <Badge tone="success">Vrij</Badge>}
      </div>
    </article>
  )
}
