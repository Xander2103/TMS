import { useEffect, useState, type FormEvent } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { getLocationOptions } from '../../locations/api/locationsApi'
import type { LocationOption } from '../../locations/types'
import { createDock, createWarehouse, deleteDock, listWarehouses, updateDock, updateWarehouse } from '../api/warehousingApi'
import type { Dock, DockInput, Warehouse, WarehouseInput } from '../types'
import './warehousing.css'

const EMPTY_WAREHOUSE: WarehouseInput = {
  name: '', locationId: '', isActive: true, opensAt: '06:00', closesAt: '20:00',
  contactName: null, contactPhone: null, contactEmail: null, notes: null,
}

const EMPTY_DOCK: DockInput = {
  code: '', name: null, allowsLoading: true, allowsUnloading: true, allowsAdr: false,
  refrigerated: false, maxVehicleLengthM: null, maxVehicleHeightM: null, isActive: true, notes: null,
}

/** Warehouse & dock master data; the physical address stays on the linked master location. */
export function WarehousesPage() {
  const { showError, showSuccess } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('warehouse.manage')

  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [locations, setLocations] = useState<LocationOption[]>([])
  const [reloadToken, setReloadToken] = useState(0)
  const [busy, setBusy] = useState(false)

  const [editing, setEditing] = useState<{ id: string | null; input: WarehouseInput } | null>(null)
  const [dockEditing, setDockEditing] = useState<{ warehouseId: string; dockId: string | null; input: DockInput } | null>(null)

  useEffect(() => {
    let cancelled = false
    listWarehouses()
      .then((data) => {
        if (!cancelled) setWarehouses(data)
      })
      .catch((error: unknown) => showError(describeApiError(error, 'Magazijnen laden mislukt.').message))
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadToken])

  useEffect(() => {
    getLocationOptions()
      .then(setLocations)
      .catch(() => undefined)
  }, [])

  async function submitWarehouse(event: FormEvent) {
    event.preventDefault()
    if (!editing) return
    setBusy(true)
    try {
      if (editing.id) {
        await updateWarehouse(editing.id, editing.input)
      } else {
        await createWarehouse(editing.input)
      }
      showSuccess('Magazijn opgeslagen.')
      setEditing(null)
      setReloadToken((t) => t + 1)
    } catch (error) {
      showError(describeApiError(error, 'Opslaan mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitDock(event: FormEvent) {
    event.preventDefault()
    if (!dockEditing) return
    setBusy(true)
    try {
      if (dockEditing.dockId) {
        await updateDock(dockEditing.warehouseId, dockEditing.dockId, dockEditing.input)
      } else {
        await createDock(dockEditing.warehouseId, dockEditing.input)
      }
      showSuccess('Dock opgeslagen.')
      setDockEditing(null)
      setReloadToken((t) => t + 1)
    } catch (error) {
      showError(describeApiError(error, 'Opslaan mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="wh-page">
      <header className="wh-header">
        <h1>Magazijnen</h1>
        {canManage && (
          <Button onClick={() => setEditing({ id: null, input: EMPTY_WAREHOUSE })}>Nieuw magazijn</Button>
        )}
      </header>

      {warehouses.length === 0 && <p className="wh-muted">Nog geen magazijnen aangemaakt.</p>}

      {warehouses.map((warehouse) => (
        <section key={warehouse.id} className="wh-card">
          <div className="wh-card-head">
            <div>
              <h2>{warehouse.name}</h2>
              <p className="wh-muted">
                {warehouse.locationLabel}
                {warehouse.opensAt && warehouse.closesAt && ` · ${warehouse.opensAt.slice(0, 5)}–${warehouse.closesAt.slice(0, 5)}`}
                {warehouse.contactName && ` · ${warehouse.contactName}`}
              </p>
            </div>
            <div className="wh-card-actions">
              {!warehouse.isActive && <Badge tone="danger">Inactief</Badge>}
              {canManage && (
                <>
                  <Button variant="secondary" onClick={() => setEditing({
                    id: warehouse.id,
                    input: {
                      name: warehouse.name, locationId: warehouse.locationId, isActive: warehouse.isActive,
                      opensAt: warehouse.opensAt?.slice(0, 5) ?? null, closesAt: warehouse.closesAt?.slice(0, 5) ?? null,
                      contactName: warehouse.contactName, contactPhone: warehouse.contactPhone,
                      contactEmail: warehouse.contactEmail, notes: warehouse.notes,
                    },
                  })}>
                    Bewerken
                  </Button>
                  <Button variant="secondary" onClick={() => setDockEditing({
                    warehouseId: warehouse.id, dockId: null, input: EMPTY_DOCK,
                  })}>
                    Dock toevoegen
                  </Button>
                </>
              )}
            </div>
          </div>
          <div className="wh-docks">
            {warehouse.docks.map((dock) => (
              <DockCard
                key={dock.id}
                dock={dock}
                canManage={canManage}
                onEdit={() => setDockEditing({
                  warehouseId: warehouse.id, dockId: dock.id,
                  input: {
                    code: dock.code, name: dock.name, allowsLoading: dock.allowsLoading,
                    allowsUnloading: dock.allowsUnloading, allowsAdr: dock.allowsAdr,
                    refrigerated: dock.refrigerated, maxVehicleLengthM: dock.maxVehicleLengthM,
                    maxVehicleHeightM: dock.maxVehicleHeightM, isActive: dock.isActive, notes: dock.notes,
                  },
                })}
                onDelete={() => {
                  void deleteDock(warehouse.id, dock.id)
                    .then(() => setReloadToken((t) => t + 1))
                    .catch((error: unknown) => showError(describeApiError(error, 'Verwijderen mislukt.').message))
                }}
              />
            ))}
            {warehouse.docks.length === 0 && <p className="wh-muted">Nog geen docks.</p>}
          </div>
        </section>
      ))}

      {editing && (
        <Modal title={editing.id ? 'Magazijn bewerken' : 'Nieuw magazijn'} onClose={() => setEditing(null)} busy={busy}>
          <form className="wh-form" onSubmit={(event) => void submitWarehouse(event)}>
            <label>
              Naam
              <input value={editing.input.name} required maxLength={200}
                     onChange={(event) => setEditing({ ...editing, input: { ...editing.input, name: event.target.value } })} />
            </label>
            <label>
              Locatie (adres)
              <select value={editing.input.locationId} required
                      onChange={(event) => setEditing({ ...editing, input: { ...editing.input, locationId: event.target.value } })}>
                <option value="">— kies een locatie —</option>
                {locations.map((location) => (
                  <option key={location.id} value={location.id}>
                    {location.name}{location.city ? ` (${location.city})` : ''}
                  </option>
                ))}
              </select>
            </label>
            <div className="wh-form-row">
              <label>
                Opent
                <input type="time" value={editing.input.opensAt ?? ''}
                       onChange={(event) => setEditing({ ...editing, input: { ...editing.input, opensAt: event.target.value || null } })} />
              </label>
              <label>
                Sluit
                <input type="time" value={editing.input.closesAt ?? ''}
                       onChange={(event) => setEditing({ ...editing, input: { ...editing.input, closesAt: event.target.value || null } })} />
              </label>
            </div>
            <label>
              Contactpersoon
              <input value={editing.input.contactName ?? ''}
                     onChange={(event) => setEditing({ ...editing, input: { ...editing.input, contactName: event.target.value || null } })} />
            </label>
            <label className="wh-check">
              <input type="checkbox" checked={editing.input.isActive}
                     onChange={(event) => setEditing({ ...editing, input: { ...editing.input, isActive: event.target.checked } })} />
              Actief
            </label>
            <div className="wh-form-actions">
              <Button variant="secondary" type="button" onClick={() => setEditing(null)} disabled={busy}>Annuleren</Button>
              <Button type="submit" disabled={busy}>Opslaan</Button>
            </div>
          </form>
        </Modal>
      )}

      {dockEditing && (
        <Modal title={dockEditing.dockId ? 'Dock bewerken' : 'Nieuw dock'} onClose={() => setDockEditing(null)} busy={busy}>
          <form className="wh-form" onSubmit={(event) => void submitDock(event)}>
            <div className="wh-form-row">
              <label>
                Code
                <input value={dockEditing.input.code} required maxLength={30}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, code: event.target.value } })} />
              </label>
              <label>
                Naam
                <input value={dockEditing.input.name ?? ''}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, name: event.target.value || null } })} />
              </label>
            </div>
            <div className="wh-form-row">
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.allowsLoading}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, allowsLoading: event.target.checked } })} />
                Laden
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.allowsUnloading}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, allowsUnloading: event.target.checked } })} />
                Lossen
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.allowsAdr}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, allowsAdr: event.target.checked } })} />
                ADR
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.refrigerated}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, refrigerated: event.target.checked } })} />
                Koeling
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.isActive}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, isActive: event.target.checked } })} />
                Actief
              </label>
            </div>
            <div className="wh-form-actions">
              <Button variant="secondary" type="button" onClick={() => setDockEditing(null)} disabled={busy}>Annuleren</Button>
              <Button type="submit" disabled={busy}>Opslaan</Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}

function DockCard({ dock, canManage, onEdit, onDelete }: {
  dock: Dock
  canManage: boolean
  onEdit: () => void
  onDelete: () => void
}) {
  return (
    <div className="wh-dock">
      <div className="wh-dock-head">
        <strong>{dock.code}</strong>
        {dock.name && <span className="wh-muted">{dock.name}</span>}
      </div>
      <div className="wh-dock-badges">
        {dock.allowsLoading && <Badge tone="info">Laden</Badge>}
        {dock.allowsUnloading && <Badge tone="info">Lossen</Badge>}
        {dock.allowsAdr && <Badge tone="warning">ADR</Badge>}
        {dock.refrigerated && <Badge tone="info">Koeling</Badge>}
        {!dock.isActive && <Badge tone="danger">Inactief</Badge>}
      </div>
      {canManage && (
        <div className="wh-dock-actions">
          <button type="button" className="wh-link" onClick={onEdit}>Bewerken</button>
          <button type="button" className="wh-link wh-link-danger" onClick={onDelete}>Verwijderen</button>
        </div>
      )}
    </div>
  )
}
