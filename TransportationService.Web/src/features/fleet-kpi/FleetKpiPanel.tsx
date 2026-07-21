import { useEffect, useState } from 'react'
import { FormField } from '../../components/ui/FormField'
import { KpiCard } from '../kpi/components/KpiCard'
import '../kpi/components/kpi.css'
import './fleet-kpi.css'
import { getFleetKpi, type FleetKpi, type FleetKpiValue, type FleetKpiOwnerType } from './fleetKpiApi'

interface FleetKpiPanelProps {
  ownerType: FleetKpiOwnerType
  ownerId: string
}

function isoDaysAgo(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

/** Formats a KPI value, showing "—" plus an explanation when the source is unavailable. */
function renderValue(v: FleetKpiValue): { value: string; hint?: string; tone?: 'warning' } {
  if (v.quality === 'Unavailable' || v.value === null) {
    return { value: '—', hint: v.detail ?? 'Niet beschikbaar' }
  }
  const formatted =
    v.unit === 'EUR'
      ? v.value.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
      : `${v.value.toLocaleString('nl-BE')} ${v.unit}`.trim()
  const estimatedHint = v.quality === 'Estimated' ? `Geschat${v.detail ? ` — ${v.detail}` : ''}` : v.detail ?? undefined
  return { value: formatted, hint: estimatedHint }
}

/**
 * KPI tab for a vehicle or trailer detail page. Reuses the KpiCard stat tiles and reports the
 * data quality of every metric — no fabricated numbers: unavailable sources render "—" with an
 * explanation. Trailer KPIs exclude fuel by construction (backend).
 */
export function FleetKpiPanel({ ownerType, ownerId }: FleetKpiPanelProps) {
  const [from, setFrom] = useState(() => isoDaysAgo(365))
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [data, setData] = useState<FleetKpi | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let mounted = true
    getFleetKpi(ownerType, ownerId, from, to)
      .then((result) => {
        if (!mounted) return
        setData(result)
        setError(null)
      })
      .catch(() => {
        if (mounted) setError('KPI-gegevens konden niet worden geladen.')
      })
      .finally(() => {
        if (mounted) setLoading(false)
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, from, to])

  return (
    <div className="fleet-kpi-panel">
      <div className="fleet-kpi-filter">
        <FormField label="Van" htmlFor="kpi-from">
          <input
            id="kpi-from"
            type="date"
            value={from}
            max={to}
            onChange={(e) => {
              setLoading(true)
              setFrom(e.target.value)
            }}
          />
        </FormField>
        <FormField label="Tot" htmlFor="kpi-to">
          <input
            id="kpi-to"
            type="date"
            value={to}
            min={from}
            onChange={(e) => {
              setLoading(true)
              setTo(e.target.value)
            }}
          />
        </FormField>
      </div>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loading && <p className="placeholder-text">KPI's laden…</p>}
      {!error && !loading && data && data.values.length === 0 && (
        <p className="placeholder-text">Geen KPI-gegevens voor deze periode.</p>
      )}
      {!error && !loading && data && data.values.length > 0 && (
        <div className="kpi-grid">
          {data.values.map((v) => {
            const rendered = renderValue(v)
            return <KpiCard key={v.key} label={v.label} value={rendered.value} hint={rendered.hint} tone={rendered.tone} />
          })}
        </div>
      )}
    </div>
  )
}
