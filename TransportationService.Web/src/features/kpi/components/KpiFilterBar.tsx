import { useEffect, useState } from 'react'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { searchCustomers } from '../../customers/api/customersApi'
import { searchDrivers } from '../../drivers/api/driversApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import { presetRange, type KpiFilterState } from '../types'
import './kpi.css'

interface KpiFilterBarProps {
  filter: KpiFilterState
  onChange: (filter: KpiFilterState) => void
}

type Preset = 'today' | 'week' | 'month' | 'custom'

function activePreset(filter: KpiFilterState): Preset {
  for (const preset of ['today', 'week', 'month'] as const) {
    const range = presetRange(preset)
    if (filter.from === range.from && filter.to === range.to) return preset
  }
  return 'custom'
}

/** One filter row above the KPI views: period presets + custom range + dimension selects. */
export function KpiFilterBar({ filter, onChange }: KpiFilterBarProps) {
  const [customers, setCustomers] = useState<SearchableSelectOption[]>([])
  const [drivers, setDrivers] = useState<SearchableSelectOption[]>([])
  const [vehicles, setVehicles] = useState<SearchableSelectOption[]>([])
  const preset = activePreset(filter)

  useEffect(() => {
    let mounted = true
    void searchCustomers({ isActive: true, page: 1, pageSize: 200 }).then((page) => {
      if (mounted) setCustomers(page.items.map((c) => ({ value: c.id, label: c.name, description: c.customerNumber })))
    }).catch(() => {})
    void searchDrivers({ isActive: true, page: 1, pageSize: 200 }).then((page) => {
      if (mounted) setDrivers(page.items.map((d) => ({ value: d.id, label: d.fullName, description: d.driverNumber })))
    }).catch(() => {})
    void getVehicleOptions().then((options) => {
      if (mounted) setVehicles(options.map((v) => ({ value: v.id, label: v.internalNumber, description: v.licensePlate })))
    }).catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  return (
    <div className="kpi-filterbar">
      <div className="kpi-presets" role="group" aria-label="Periode">
        {(
          [
            ['today', 'Vandaag'],
            ['week', 'Deze week'],
            ['month', 'Deze maand'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={preset === key ? 'kpi-preset-active' : undefined}
            onClick={() => onChange({ ...filter, ...presetRange(key) })}
          >
            {label}
          </button>
        ))}
      </div>
      <label>
        Van
        <input type="date" value={filter.from} onChange={(e) => onChange({ ...filter, from: e.target.value })} />
      </label>
      <label>
        Tot
        <input type="date" value={filter.to} onChange={(e) => onChange({ ...filter, to: e.target.value })} />
      </label>
      <div className="kpi-filter-select">
        <SearchableSelect
          ariaLabel="Klant"
          placeholder="Alle klanten"
          value={filter.customerId}
          onChange={(value) => onChange({ ...filter, customerId: value })}
          options={customers}
        />
      </div>
      <div className="kpi-filter-select">
        <SearchableSelect
          ariaLabel="Chauffeur"
          placeholder="Alle chauffeurs"
          value={filter.driverId}
          onChange={(value) => onChange({ ...filter, driverId: value })}
          options={drivers}
        />
      </div>
      <div className="kpi-filter-select">
        <SearchableSelect
          ariaLabel="Voertuig"
          placeholder="Alle voertuigen"
          value={filter.vehicleId}
          onChange={(value) => onChange({ ...filter, vehicleId: value })}
          options={vehicles}
        />
      </div>
    </div>
  )
}
