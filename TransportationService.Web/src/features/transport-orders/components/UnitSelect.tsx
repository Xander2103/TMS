import { useState } from 'react'
import type { CustomerPreferredUnit } from '../../tarification/api/pricingApi'

export interface UnitOptionItem {
  id: string
  code: string
  name: string
}

interface UnitSelectProps {
  id: string
  value: string | null
  onChange: (code: string | null) => void
  units: UnitOptionItem[]
  preferredUnits: CustomerPreferredUnit[]
  disabled?: boolean
}

/**
 * Grouped unit selector (spec §4): the customer's configured units on top (favourites first,
 * customer label shown), every other active unit below. The planner is never restricted to
 * the customer units — "Andere eenheden tonen" reveals the full list.
 */
export function UnitSelect({ id, value, onChange, units, preferredUnits, disabled }: UnitSelectProps) {
  const [showAll, setShowAll] = useState(false)
  const preferredCodes = new Set(preferredUnits.map((u) => u.code))
  const otherUnits = units.filter((u) => !preferredCodes.has(u.code))
  const showOthers =
    preferredUnits.length === 0 || showAll || (value !== null && value !== '' && !preferredCodes.has(value))

  return (
    <>
      <select id={id} value={value ?? ''} onChange={(e) => onChange(e.target.value || null)} disabled={disabled}>
        <option value="">— Eenheid —</option>
        {preferredUnits.length > 0 && (
          <optgroup label="Eenheden van deze klant">
            {preferredUnits.map((unit) => (
              <option key={unit.unitTypeId} value={unit.code}>
                {unit.isFavourite ? '★ ' : ''}
                {unit.customerLabel ?? unit.name}
              </option>
            ))}
          </optgroup>
        )}
        {showOthers &&
          (preferredUnits.length > 0 ? (
            <optgroup label="Andere eenheden">
              {otherUnits.map((unit) => (
                <option key={unit.id} value={unit.code}>
                  {unit.name}
                </option>
              ))}
            </optgroup>
          ) : (
            units.map((unit) => (
              <option key={unit.id} value={unit.code}>
                {unit.name}
              </option>
            ))
          ))}
      </select>
      {preferredUnits.length > 0 && !showAll && (
        <button type="button" className="tof-link" onClick={() => setShowAll(true)}>
          Andere eenheden tonen
        </button>
      )}
    </>
  )
}
