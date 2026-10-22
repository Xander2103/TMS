import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { apiClient, LONG_RUNNING_TIMEOUT_MS } from '../../../api/apiClient'
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
      // PERF-H7: through apiClient (bearer + refresh, server file name) with the long-running deadline.
      await apiClient.downloadFile(kpiExportUrl(report, filter), `kpi-${report}.xlsx`, { timeoutMs: LONG_RUNNING_TIMEOUT_MS })
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
