import { useEffect, useState } from 'react'
import { FormField } from '../../components/ui/FormField'
import { useLocale } from '../../i18n/localeContext'
import type { TranslateFn } from '../../i18n/localeContext'
import { formatCurrency, formatDecimal, formatInteger } from '../../utils/numbers'
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

/** Whole numbers render without decimals ("42 ritten"); fractional values keep two. */
function quantity(value: number): string {
  return Number.isInteger(value) ? formatInteger(value) : formatDecimal(value, 2)
}

/** Formats a KPI value, showing "—" plus an explanation when the source is unavailable. */
function renderValue(t: TranslateFn, v: FleetKpiValue): { value: string; hint?: string; tone?: 'warning' } {
  if (v.quality === 'Unavailable' || v.value === null) {
    return { value: '—', hint: v.detail ?? t('fleet.kpi.unavailable') }
  }
  const formatted = v.unit === 'EUR' ? formatCurrency(v.value) : `${quantity(v.value)} ${v.unit}`.trim()
  const estimatedHint = v.quality === 'Estimated' ? `${t('fleet.kpi.estimated')}${v.detail ? ` — ${v.detail}` : ''}` : v.detail ?? undefined
  return { value: formatted, hint: estimatedHint }
}

/**
 * KPI tab for a vehicle or trailer detail page. Reuses the KpiCard stat tiles and reports the
 * data quality of every metric — no fabricated numbers: unavailable sources render "—" with an
 * explanation. Trailer KPIs exclude fuel by construction (backend).
 */
export function FleetKpiPanel({ ownerType, ownerId }: FleetKpiPanelProps) {
  const { t } = useLocale()
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
        if (mounted) setError(t('fleet.kpi.loadFailed'))
      })
      .finally(() => {
        if (mounted) setLoading(false)
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, from, to, t])

  return (
    <div className="fleet-kpi-panel">
      <div className="fleet-kpi-filter">
        <FormField label={t('fleet.kpi.from')} htmlFor="kpi-from">
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
        <FormField label={t('fleet.kpi.to')} htmlFor="kpi-to">
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
      {!error && loading && <p className="placeholder-text">{t('fleet.kpi.loading')}</p>}
      {!error && !loading && data && data.values.length === 0 && (
        <p className="placeholder-text">{t('fleet.kpi.empty')}</p>
      )}
      {!error && !loading && data && data.values.length > 0 && (
        <div className="kpi-grid">
          {data.values.map((v) => {
            const rendered = renderValue(t, v)
            return <KpiCard key={v.key} label={v.label} value={rendered.value} hint={rendered.hint} tone={rendered.tone} />
          })}
        </div>
      )}
    </div>
  )
}
