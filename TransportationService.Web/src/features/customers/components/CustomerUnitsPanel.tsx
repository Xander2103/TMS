import { useCallback, useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  getCustomerPricingConfig,
  listUnitTypeSettings,
  saveCustomerPricingConfig,
  type CustomerPricingConfig,
  type CustomerUnitInput,
  type UnitTypeSettings,
} from '../../tarification/api/pricingApi'

interface CustomerUnitsPanelProps {
  customerId: string
}

/**
 * Customer units (spec §3): which global units this customer commonly uses, with a customer
 * label, external EDI/Excel codes, favourite flag and sort order. The global unit is never
 * duplicated — these rows only configure how the customer uses it.
 */
export function CustomerUnitsPanel({ customerId }: CustomerUnitsPanelProps) {
  const { hasPermission } = useAuth()
  const { showError, showSuccess } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [config, setConfig] = useState<CustomerPricingConfig | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [addUnitId, setAddUnitId] = useState('')

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([getCustomerPricingConfig(customerId), listUnitTypeSettings().catch(() => [] as UnitTypeSettings[])])
      .then(([configData, unitData]) => {
        setConfig(configData)
        setUnits(unitData)
        setLoadError(null)
      })
      .catch(() => setLoadError('De klant-eenheden konden niet worden geladen.'))
  }, [customerId, canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return null
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!config) return <p className="placeholder-text">Klant-eenheden laden…</p>

  const configured = config.preferredUnits
  const configuredIds = new Set(configured.map((u) => u.unitTypeId))
  const available = units.filter((u) => u.isActive && !configuredIds.has(u.id))

  async function save(nextUnits: CustomerUnitInput[], message?: string) {
    if (!config) return
    try {
      const saved = await saveCustomerPricingConfig(customerId, {
        units: nextUnits,
        optionPrices: config.serviceOptions.map((o) => ({ serviceOptionId: o.serviceOptionId, value: o.customerValue })),
      })
      setConfig(saved)
      if (message) showSuccess(message)
    } catch (err) {
      showError(describeApiError(err, 'De klant-eenheden konden niet worden opgeslagen.').message)
    }
  }

  const asInputs = (): CustomerUnitInput[] =>
    configured.map((u) => ({
      unitTypeId: u.unitTypeId,
      sortOrder: u.sortOrder,
      customerLabel: u.customerLabel,
      ediCode: u.ediCode,
      excelCode: u.excelCode,
      isFavourite: u.isFavourite,
    }))

  function updateUnit(unitTypeId: string, patch: Partial<CustomerUnitInput>, message?: string) {
    void save(asInputs().map((u) => (u.unitTypeId === unitTypeId ? { ...u, ...patch } : u)), message)
  }

  function move(unitTypeId: string, delta: -1 | 1) {
    const ordered = asInputs().sort((a, b) => a.sortOrder - b.sortOrder)
    const index = ordered.findIndex((u) => u.unitTypeId === unitTypeId)
    const target = index + delta
    if (index < 0 || target < 0 || target >= ordered.length) return
    const swapped = [...ordered]
    ;[swapped[index], swapped[target]] = [swapped[target], swapped[index]]
    void save(swapped.map((u, i) => ({ ...u, sortOrder: i })))
  }

  function addUnit() {
    if (!addUnitId) return
    void save(
      [...asInputs(), {
        unitTypeId: addUnitId,
        sortOrder: configured.length,
        customerLabel: null,
        ediCode: null,
        excelCode: null,
        isFavourite: true,
      }],
      'Eenheid toegevoegd.',
    )
    setAddUnitId('')
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Eenheden van deze klant</h3>
      </div>
      <p className="customer-form-muted">
        Deze eenheden verschijnen bovenaan bij orderinvoer (favorieten eerst); andere actieve eenheden blijven kiesbaar.
        Externe EDI-/Excel-codes worden bij import automatisch naar de juiste eenheid vertaald.
      </p>

      {configured.length === 0 && <p className="placeholder-text">Nog geen eenheden geconfigureerd voor deze klant.</p>}
      {configured.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Eenheid</th>
              <th>Klantbenaming</th>
              <th>EDI-code</th>
              <th>Excel-code</th>
              <th>Favoriet</th>
              {canManage && <th>Volgorde</th>}
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {configured.map((unit) => (
              <tr key={unit.unitTypeId}>
                <td>{unit.name}</td>
                <td>
                  <input
                    aria-label={`Klantbenaming voor ${unit.name}`}
                    defaultValue={unit.customerLabel ?? ''}
                    placeholder={unit.name}
                    maxLength={150}
                    disabled={!canManage}
                    onBlur={(e) => {
                      const value = e.target.value.trim() === '' ? null : e.target.value.trim()
                      if (value !== unit.customerLabel) updateUnit(unit.unitTypeId, { customerLabel: value })
                    }}
                  />
                </td>
                <td>
                  <input
                    aria-label={`EDI-code voor ${unit.name}`}
                    defaultValue={unit.ediCode ?? ''}
                    maxLength={50}
                    disabled={!canManage}
                    onBlur={(e) => {
                      const value = e.target.value.trim() === '' ? null : e.target.value.trim()
                      if (value !== unit.ediCode) updateUnit(unit.unitTypeId, { ediCode: value })
                    }}
                  />
                </td>
                <td>
                  <input
                    aria-label={`Excel-code voor ${unit.name}`}
                    defaultValue={unit.excelCode ?? ''}
                    maxLength={50}
                    disabled={!canManage}
                    onBlur={(e) => {
                      const value = e.target.value.trim() === '' ? null : e.target.value.trim()
                      if (value !== unit.excelCode) updateUnit(unit.unitTypeId, { excelCode: value })
                    }}
                  />
                </td>
                <td>
                  <button
                    type="button"
                    className="issued-items-link"
                    aria-label={`Favoriet ${unit.name}`}
                    aria-pressed={unit.isFavourite}
                    disabled={!canManage}
                    onClick={() => updateUnit(unit.unitTypeId, { isFavourite: !unit.isFavourite })}
                  >
                    {unit.isFavourite ? '★' : '☆'}
                  </button>
                </td>
                {canManage && (
                  <td>
                    <button type="button" className="issued-items-link" aria-label={`${unit.name} omhoog`} onClick={() => move(unit.unitTypeId, -1)}>
                      ↑
                    </button>
                    <button type="button" className="issued-items-link" aria-label={`${unit.name} omlaag`} onClick={() => move(unit.unitTypeId, 1)}>
                      ↓
                    </button>
                  </td>
                )}
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => void save(asInputs().filter((u) => u.unitTypeId !== unit.unitTypeId), 'Eenheid verwijderd.')}
                    >
                      Verwijderen
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {canManage && available.length > 0 && (
        <div className="customer-panel-header">
          <select aria-label="Eenheid toevoegen" value={addUnitId} onChange={(e) => setAddUnitId(e.target.value)}>
            <option value="">— Kies een eenheid —</option>
            {available.map((unit) => (
              <option key={unit.id} value={unit.id}>
                {unit.name}
              </option>
            ))}
          </select>
          <Button variant="secondary" onClick={addUnit} disabled={!addUnitId}>
            + Eenheid toevoegen
          </Button>
        </div>
      )}
    </section>
  )
}
