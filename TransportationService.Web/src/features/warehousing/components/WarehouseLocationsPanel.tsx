import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
      showSuccess(t('warehousing.locations.added'))
      setCode('')
      setName('')
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('warehousing.locations.addFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function remove(location: WarehouseLocation) {
    try {
      await deleteWarehouseLocation(warehouseId, location.id)
      showSuccess(t('warehousing.locations.removed'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('warehousing.locations.removeFailed')))
    }
  }

  const zones = (locations ?? []).filter((l) => l.parentId === null)
  const positionsOf = (zoneId: string) => (locations ?? []).filter((l) => l.parentId === zoneId)

  return (
    <div className="wh-locations">
      <button type="button" className="issued-items-link" onClick={() => setOpen((o) => !o)}>
        {open ? t('warehousing.locations.hide') : t('warehousing.locations.manage')}
      </button>
      {open && (
        <div className="wh-locations-body">
          {locations === null && <p className="wh-muted">{t('warehousing.locations.loading')}</p>}
          {locations !== null && zones.length === 0 && (
            <p className="wh-muted">{t('warehousing.locations.empty')}</p>
          )}
          {zones.map((zone) => (
            <div key={zone.id} className="wh-location-zone">
              <span>
                <strong>{zone.code}</strong> — {zone.name}
                {zone.packageCount > 0 && (
                  <span className="wh-muted"> · {t('warehousing.locations.colli', { count: zone.packageCount })}</span>
                )}
              </span>
              {canManage && (
                <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => void remove(zone)}>
                  {t('warehousing.locations.remove')}
                </button>
              )}
              {positionsOf(zone.id).map((position) => (
                <div key={position.id} className="wh-location-position">
                  <span>
                    ↳ {position.code} — {position.name}
                    {position.packageCount > 0 && (
                      <span className="wh-muted"> · {t('warehousing.locations.colli', { count: position.packageCount })}</span>
                    )}
                  </span>
                  {canManage && (
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => void remove(position)}
                    >
                      {t('warehousing.locations.remove')}
                    </button>
                  )}
                </div>
              ))}
            </div>
          ))}
          {canManage && (
            <form className="wh-location-add" onSubmit={(e) => void add(e)}>
              <select value={parentId} onChange={(e) => setParentId(e.target.value)} aria-label={t('warehousing.locations.parentZoneAria')} disabled={busy}>
                <option value="">{t('warehousing.locations.newZone')}</option>
                {zones.map((zone) => (
                  <option key={zone.id} value={zone.id}>
                    {t('warehousing.locations.positionIn', { zone: zone.code })}
                  </option>
                ))}
              </select>
              <input value={code} onChange={(e) => setCode(e.target.value)} placeholder={t('warehousing.locations.codePlaceholder')} maxLength={50} aria-label={t('warehousing.locations.codeAria')} disabled={busy} />
              <input value={name} onChange={(e) => setName(e.target.value)} placeholder={t('warehousing.locations.namePlaceholder')} maxLength={200} aria-label={t('warehousing.locations.nameAria')} disabled={busy} />
              <Button type="submit" variant="secondary" disabled={busy || !code.trim() || !name.trim()}>
                {t('warehousing.locations.add')}
              </Button>
            </form>
          )}
        </div>
      )}
    </div>
  )
}
