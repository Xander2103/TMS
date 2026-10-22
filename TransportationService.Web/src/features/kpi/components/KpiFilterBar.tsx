import { useEffect, useState } from 'react'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useLocale } from '../../../i18n/localeContext'
import { useResolvedLabel } from '../../../hooks/useResolvedLabel'
import { resolveCustomerLabel, searchActiveCustomerOptions } from '../../customers/api/customerOptions'
import { resolveDriverLabel, searchActiveDriverOptions } from '../../drivers/api/driverOptions'
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

/** Vertaalsleutels per preset — renderen als t(label). */
const PRESETS: ReadonlyArray<readonly ['today' | 'week' | 'month', string]> = [
  ['today', 'kpiReports.filter.today'],
  ['week', 'kpiReports.filter.thisWeek'],
  ['month', 'kpiReports.filter.thisMonth'],
]

/** One filter row above the KPI views: period presets + custom range + dimension selects. */
export function KpiFilterBar({ filter, onChange }: KpiFilterBarProps) {
  const { t } = useLocale()
  // PERF-H3: customers and drivers are searched server-side as the user types; a selection that
  // arrives with the filter (restored view) resolves its name once.
  const customerLabel = useResolvedLabel(filter.customerId, resolveCustomerLabel)
  const driverLabel = useResolvedLabel(filter.driverId, resolveDriverLabel)
  const [vehicles, setVehicles] = useState<SearchableSelectOption[]>([])
  const preset = activePreset(filter)

  useEffect(() => {
    let mounted = true
    void getVehicleOptions().then((options) => {
      if (mounted) setVehicles(options.map((v) => ({ value: v.id, label: v.internalNumber, description: v.licensePlate })))
    }).catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  return (
    <div className="kpi-filterbar">
      <div className="kpi-presets" role="group" aria-label={t('kpiReports.filter.periodAria')}>
        {PRESETS.map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={preset === key ? 'kpi-preset-active' : undefined}
            onClick={() => onChange({ ...filter, ...presetRange(key) })}
          >
            {t(label)}
          </button>
        ))}
      </div>
      <label>
        {t('kpiReports.filter.from')}
        <input type="date" value={filter.from} onChange={(e) => onChange({ ...filter, from: e.target.value })} />
      </label>
      <label>
        {t('kpiReports.filter.to')}
        <input type="date" value={filter.to} onChange={(e) => onChange({ ...filter, to: e.target.value })} />
      </label>
      <div className="kpi-filter-select">
        <SearchableSelect
          ariaLabel={t('kpiReports.filter.customerAria')}
          placeholder={t('kpiReports.filter.allCustomers')}
          value={filter.customerId}
          onChange={(value) => onChange({ ...filter, customerId: value })}
          options={[]}
          selectedLabel={customerLabel}
          onSearch={searchActiveCustomerOptions}
        />
      </div>
      <div className="kpi-filter-select">
        <SearchableSelect
          ariaLabel={t('kpiReports.filter.driverAria')}
          placeholder={t('kpiReports.filter.allDrivers')}
          value={filter.driverId}
          onChange={(value) => onChange({ ...filter, driverId: value })}
          options={[]}
          selectedLabel={driverLabel}
          onSearch={searchActiveDriverOptions}
        />
      </div>
      <div className="kpi-filter-select">
        <SearchableSelect
          ariaLabel={t('kpiReports.filter.vehicleAria')}
          placeholder={t('kpiReports.filter.allVehicles')}
          value={filter.vehicleId}
          onChange={(value) => onChange({ ...filter, vehicleId: value })}
          options={vehicles}
        />
      </div>
    </div>
  )
}
