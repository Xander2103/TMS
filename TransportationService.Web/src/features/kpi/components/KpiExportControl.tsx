import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { kpiExportUrl } from '../api/kpiApi'
import type { KpiFilterState } from '../types'

/** Vertaalsleutels per rapportcode — renderen als t(label). */
const REPORTS: Array<{ key: string; label: string }> = [
  { key: 'trip-profitability', label: 'kpiReports.export.tripProfitability' },
  { key: 'customer-profitability', label: 'kpiReports.export.customerProfitability' },
  { key: 'vehicle-utilisation', label: 'kpiReports.export.vehicleUtilisation' },
  { key: 'driver-hours', label: 'kpiReports.export.driverHours' },
  { key: 'empty-km', label: 'kpiReports.export.emptyKm' },
  { key: 'fuel', label: 'kpiReports.export.fuel' },
  { key: 'co2', label: 'kpiReports.export.co2' },
  { key: 'eta-performance', label: 'kpiReports.export.etaPerformance' },
  { key: 'delivery-reliability', label: 'kpiReports.export.deliveryReliability' },
]

/** XLSX download for the nine KPI reports (kpi.export); filename comes from the server. */
export function KpiExportControl({ filter }: { filter: KpiFilterState }) {
  const { t } = useLocale()
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
      showError(t('kpiReports.export.failed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
      <select value={report} onChange={(e) => setReport(e.target.value)} disabled={busy} aria-label={t('kpiReports.export.reportAria')}>
        {REPORTS.map((item) => (
          <option key={item.key} value={item.key}>
            {t(item.label)}
          </option>
        ))}
      </select>
      <Button variant="secondary" onClick={() => void download()} disabled={busy}>
        {busy ? t('kpiReports.export.busy') : t('kpiReports.export.button')}
      </Button>
    </span>
  )
}
