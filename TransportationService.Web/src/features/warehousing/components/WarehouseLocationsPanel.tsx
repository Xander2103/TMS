import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  createWarehouseLocation,
  deleteWarehouseLocation,
  listWarehouseLocations,
  type WarehouseLocation,
} from '../api/warehousingApi'

interface WarehouseLocationsPanelProps {
  warehouseId: string
  canManage: boolean
}

/**
 * Wave 4 §1: opslaglocaties van één magazijn (zone → positie, max. twee niveaus). Compact
 * beheer in de magazijnkaart: per zone een regel met haar posities, toevoegen via één
 * invoerregel, verwijderen alleen als de locatie leeg is (server bewaakt).
 */
export function WarehouseLocationsPanel({ warehouseId, canManage }: WarehouseLocationsPanelProps) {
  const { showSuccess, showError } = useToast()
  const [locations, setLocations] = useState<WarehouseLocation[] | null>(null)
  const [open, setOpen] = useState(false)
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [parentId, setParentId] = useState('')
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listWarehouseLocations(warehouseId)
      .then(setLocations)
      .catch(() => setLocations([]))
  }, [warehouseId])

  useEffect(() => {
    if (open) reload()
  }, [open, reload])

  async function add(event: FormEvent) {
    event.preventDefault()
    if (!code.trim() || !name.trim()) return
    setBusy(true)
    try {
      await createWarehouseLocation(warehouseId, {
        code: code.trim(),
        name: name.trim(),
        parentId: parentId || null,
      })
      showSuccess('Locatie toegevoegd.')
      setCode('')
      setName('')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De locatie kon niet worden toegevoegd.').message)
    } finally {
      setBusy(false)
    }
  }

  async function remove(location: WarehouseLocation) {
    try {
      await deleteWarehouseLocation(warehouseId, location.id)
      showSuccess('Locatie verwijderd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De locatie kon niet worden verwijderd.').message)
    }
  }

  const zones = (locations ?? []).filter((l) => l.parentId === null)
  const positionsOf = (zoneId: string) => (locations ?? []).filter((l) => l.parentId === zoneId)

  return (
    <div className="wh-locations">
      <button type="button" className="issued-items-link" onClick={() => setOpen((o) => !o)}>
        {open ? 'Locaties verbergen' : 'Locaties beheren'}
      </button>
      {open && (
        <div className="wh-locations-body">
          {locations === null && <p className="wh-muted">Locaties laden…</p>}
          {locations !== null && zones.length === 0 && (
            <p className="wh-muted">Nog geen opslaglocaties. Voeg eerst een zone toe (bv. A — Bulkzone).</p>
          )}
          {zones.map((zone) => (
            <div key={zone.id} className="wh-location-zone">
              <span>
                <strong>{zone.code}</strong> — {zone.name}
                {zone.packageCount > 0 && <span className="wh-muted"> · {zone.packageCount} colli</span>}
              </span>
              {canManage && (
                <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => void remove(zone)}>
                  Verwijderen
                </button>
              )}
              {positionsOf(zone.id).map((position) => (
                <div key={position.id} className="wh-location-position">
                  <span>
                    ↳ {position.code} — {position.name}
                    {position.packageCount > 0 && <span className="wh-muted"> · {position.packageCount} colli</span>}
                  </span>
                  {canManage && (
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => void remove(position)}
                    >
                      Verwijderen
                    </button>
                  )}
                </div>
              ))}
            </div>
          ))}
          {canManage && (
            <form className="wh-location-add" onSubmit={(e) => void add(e)}>
              <select value={parentId} onChange={(e) => setParentId(e.target.value)} aria-label="Bovenliggende zone" disabled={busy}>
                <option value="">Nieuwe zone</option>
                {zones.map((zone) => (
                  <option key={zone.id} value={zone.id}>
                    Positie in {zone.code}
                  </option>
                ))}
              </select>
              <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="Code (bv. A of A-01)" maxLength={50} aria-label="Locatiecode" disabled={busy} />
              <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Naam" maxLength={200} aria-label="Locatienaam" disabled={busy} />
              <Button type="submit" variant="secondary" disabled={busy || !code.trim() || !name.trim()}>
                + Locatie
              </Button>
            </form>
          )}
        </div>
      )}
    </div>
  )
}
