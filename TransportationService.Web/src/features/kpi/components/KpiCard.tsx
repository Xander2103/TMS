import type { ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import './kpi.css'

interface KpiCardProps {
  label: string
  value: ReactNode
  hint?: string
  /** Deep link to the filtered detail view; omit for cards without a drill-down. */
  to?: string
  /** Reserved status accent — pair with the label, never colour alone. */
  tone?: 'warning' | 'danger'
}

/**
 * Stat tile per the dataviz spec: sentence-case label, semibold proportional-figure
 * value in text ink (status tones only accent the border, the label carries meaning).
 * Clickable tiles are buttons that deep-link to the filtered detail report.
 */
export function KpiCard({ label, value, hint, to, tone }: KpiCardProps) {
  const navigate = useNavigate()
  const className = `kpi-card${tone ? ` kpi-card-${tone}` : ''}`
  const body = (
    <>
      <span className="kpi-card-label">{label}</span>
      <span className="kpi-card-value">{value}</span>
      {hint && <span className="kpi-card-hint">{hint}</span>}
    </>
  )

  return to ? (
    <button type="button" className={`${className} kpi-card-link`} onClick={() => navigate(to)}>
      {body}
    </button>
  ) : (
    <div className={className}>{body}</div>
  )
}
