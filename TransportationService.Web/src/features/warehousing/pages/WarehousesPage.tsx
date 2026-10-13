import { useEffect, useState, type FormEvent } from 'react'
import { localizeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getLocationOptions } from '../../locations/api/locationsApi'
import type { LocationOption } from '../../locations/types'
import { createDock, createWarehouse, deleteDock, listWarehouses, updateDock, updateWarehouse } from '../api/warehousingApi'
import { WarehouseLocationsPanel } from '../components/WarehouseLocationsPanel'
import { OPERATION_LABELS, type Dock, type DockInput, type Warehouse, type WarehouseInput } from '../types'
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
  const { t } = useLocale()
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
      .catch((error: unknown) => showError(localizeApiError(t, error, t('warehousing.warehouses.loadFailed'))))
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
      showSuccess(t('warehousing.warehouses.saved'))
      setEditing(null)
      setReloadToken((token) => token + 1)
    } catch (error) {
      showError(localizeApiError(t, error, t('warehousing.warehouses.saveFailed')))
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
      showSuccess(t('warehousing.docks.saved'))
      setDockEditing(null)
      setReloadToken((token) => token + 1)
    } catch (error) {
      showError(localizeApiError(t, error, t('warehousing.warehouses.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="wh-page">
      <header className="wh-header">
        <h1>{t('warehousing.warehouses.title')}</h1>
        {canManage && (
          <Button onClick={() => setEditing({ id: null, input: EMPTY_WAREHOUSE })}>{t('warehousing.warehouses.new')}</Button>
        )}
      </header>

      {warehouses.length === 0 && <p className="wh-muted">{t('warehousing.warehouses.none')}</p>}

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
              {!warehouse.isActive && <Badge tone="danger">{t('warehousing.warehouses.inactive')}</Badge>}
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
                    {t('warehousing.warehouses.edit')}
                  </Button>
                  <Button variant="secondary" onClick={() => setDockEditing({
                    warehouseId: warehouse.id, dockId: null, input: EMPTY_DOCK,
                  })}>
                    {t('warehousing.warehouses.addDock')}
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
                    .then(() => setReloadToken((token) => token + 1))
                    .catch((error: unknown) => showError(localizeApiError(t, error, t('warehousing.warehouses.deleteFailed'))))
                }}
              />
            ))}
            {warehouse.docks.length === 0 && <p className="wh-muted">{t('warehousing.warehouses.noDocks')}</p>}
          </div>
          <WarehouseLocationsPanel warehouseId={warehouse.id} canManage={canManage} />
        </section>
      ))}

      {editing && (
        <Modal
          title={editing.id ? t('warehousing.warehouses.editTitle') : t('warehousing.warehouses.new')}
          onClose={() => setEditing(null)}
          busy={busy}
        >
          <form className="wh-form" onSubmit={(event) => void submitWarehouse(event)}>
            <label>
              {t('warehousing.warehouses.name')}
              <input value={editing.input.name} required maxLength={200}
                     onChange={(event) => setEditing({ ...editing, input: { ...editing.input, name: event.target.value } })} />
            </label>
            <label>
              {t('warehousing.warehouses.locationAddress')}
              <select value={editing.input.locationId} required
                      onChange={(event) => setEditing({ ...editing, input: { ...editing.input, locationId: event.target.value } })}>
                <option value="">{t('warehousing.warehouses.chooseLocation')}</option>
                {locations.map((location) => (
                  <option key={location.id} value={location.id}>
                    {location.name}{location.city ? ` (${location.city})` : ''}
                  </option>
                ))}
              </select>
            </label>
            <div className="wh-form-row">
              <label>
                {t('warehousing.warehouses.opensAt')}
                <input type="time" value={editing.input.opensAt ?? ''}
                       onChange={(event) => setEditing({ ...editing, input: { ...editing.input, opensAt: event.target.value || null } })} />
              </label>
              <label>
                {t('warehousing.warehouses.closesAt')}
                <input type="time" value={editing.input.closesAt ?? ''}
                       onChange={(event) => setEditing({ ...editing, input: { ...editing.input, closesAt: event.target.value || null } })} />
              </label>
            </div>
            <label>
              {t('warehousing.warehouses.contactName')}
              <input value={editing.input.contactName ?? ''}
                     onChange={(event) => setEditing({ ...editing, input: { ...editing.input, contactName: event.target.value || null } })} />
            </label>
            <label className="wh-check">
              <input type="checkbox" checked={editing.input.isActive}
                     onChange={(event) => setEditing({ ...editing, input: { ...editing.input, isActive: event.target.checked } })} />
              {t('warehousing.warehouses.active')}
            </label>
            <div className="wh-form-actions">
              <Button variant="secondary" type="button" onClick={() => setEditing(null)} disabled={busy}>
                {t('warehousing.warehouses.cancel')}
              </Button>
              <Button type="submit" disabled={busy}>{t('warehousing.warehouses.save')}</Button>
            </div>
          </form>
        </Modal>
      )}

      {dockEditing && (
        <Modal
          title={dockEditing.dockId ? t('warehousing.docks.editTitle') : t('warehousing.docks.newTitle')}
          onClose={() => setDockEditing(null)}
          busy={busy}
        >
          <form className="wh-form" onSubmit={(event) => void submitDock(event)}>
            <div className="wh-form-row">
              <label>
                {t('warehousing.docks.code')}
                <input value={dockEditing.input.code} required maxLength={30}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, code: event.target.value } })} />
              </label>
              <label>
                {t('warehousing.docks.name')}
                <input value={dockEditing.input.name ?? ''}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, name: event.target.value || null } })} />
              </label>
            </div>
            <div className="wh-form-row">
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.allowsLoading}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, allowsLoading: event.target.checked } })} />
                {t(OPERATION_LABELS.Loading)}
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.allowsUnloading}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, allowsUnloading: event.target.checked } })} />
                {t(OPERATION_LABELS.Unloading)}
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.allowsAdr}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, allowsAdr: event.target.checked } })} />
                {t('warehousing.docks.adr')}
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.refrigerated}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, refrigerated: event.target.checked } })} />
                {t('warehousing.docks.refrigerated')}
              </label>
              <label className="wh-check">
                <input type="checkbox" checked={dockEditing.input.isActive}
                       onChange={(event) => setDockEditing({ ...dockEditing, input: { ...dockEditing.input, isActive: event.target.checked } })} />
                {t('warehousing.docks.active')}
              </label>
            </div>
            <div className="wh-form-actions">
              <Button variant="secondary" type="button" onClick={() => setDockEditing(null)} disabled={busy}>
                {t('warehousing.warehouses.cancel')}
              </Button>
              <Button type="submit" disabled={busy}>{t('warehousing.warehouses.save')}</Button>
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
  const { t } = useLocale()
  return (
    <div className="wh-dock">
      <div className="wh-dock-head">
        <strong>{dock.code}</strong>
        {dock.name && <span className="wh-muted">{dock.name}</span>}
      </div>
      <div className="wh-dock-badges">
        {dock.allowsLoading && <Badge tone="info">{t(OPERATION_LABELS.Loading)}</Badge>}
        {dock.allowsUnloading && <Badge tone="info">{t(OPERATION_LABELS.Unloading)}</Badge>}
        {dock.allowsAdr && <Badge tone="warning">{t('warehousing.docks.adr')}</Badge>}
        {dock.refrigerated && <Badge tone="info">{t('warehousing.docks.refrigerated')}</Badge>}
        {!dock.isActive && <Badge tone="danger">{t('warehousing.warehouses.inactive')}</Badge>}
      </div>
      {canManage && (
        <div className="wh-dock-actions">
          <button type="button" className="wh-link" onClick={onEdit}>{t('warehousing.warehouses.edit')}</button>
          <button type="button" className="wh-link wh-link-danger" onClick={onDelete}>{t('warehousing.warehouses.delete')}</button>
        </div>
      )}
    </div>
  )
}
