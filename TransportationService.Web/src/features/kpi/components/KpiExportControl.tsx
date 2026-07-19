import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { kpiExportUrl } from '../api/kpiApi'
import type { KpiFilterState } from '../types'

const REPORTS: Array<{ key: string; label: string }> = [
  { key: 'trip-profitability', label: 'Ritrendement' },
  { key: 'customer-profitability', label: 'Klantrendement' },
  { key: 'vehicle-utilisation', label: 'Voertuigbezetting' },
  { key: 'driver-hours', label: 'Chauffeurs km/uren' },
  { key: 'empty-km', label: 'Lege kilometers' },
  { key: 'fuel', label: 'Brandstofverbruik' },
  { key: 'co2', label: 'CO₂' },
  { key: 'eta-performance', label: 'ETA-prestaties' },
  { key: 'delivery-reliability', label: 'Leverbetrouwbaarheid' },
]

/** XLSX download for the nine KPI reports (kpi.export); filename comes from the server. */
export function KpiExportControl({ filter }: { filter: KpiFilterState }) {
  const { hasPermission } = useAuth()
  const { showError } = useToast()
  const [report, setReport] = useState(REPORTS[0].key)
  const [busy, setBusy] = useState(false)

  if (!hasPermission('kpi.export')) return null

  async function download() {
    setBusy(true)
    try {
      const response = await fetch(`${apiBaseUrl}${kpiExportUrl(report, filter)}`, {
        headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
      })
      if (!response.ok) throw new Error()
      const disposition = response.headers.get('Content-Disposition') ?? ''
      const match = /filename="?([^";]+)"?/.exec(disposition)
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = match?.[1] ?? `kpi-${report}.xlsx`
      anchor.click()
      URL.revokeObjectURL(url)
    } catch {
      showError('De export is mislukt.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
      <select value={report} onChange={(e) => setReport(e.target.value)} disabled={busy} aria-label="Rapport">
        {REPORTS.map((item) => (
          <option key={item.key} value={item.key}>
            {item.label}
          </option>
        ))}
      </select>
      <Button variant="secondary" onClick={() => void download()} disabled={busy}>
        {busy ? 'Exporteren…' : 'Exporteren (Excel)'}
      </Button>
    </span>
  )
}
