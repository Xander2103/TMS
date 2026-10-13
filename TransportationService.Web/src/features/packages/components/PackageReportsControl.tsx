import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

/** Vertaalsleutels per rapportcode — renderen als t(label). */
const REPORTS: Array<{ key: string; label: string }> = [
  { key: 'order-packages', label: 'packages.reports.orderPackages' },
  { key: 'trip-packages', label: 'packages.reports.tripPackages' },
  { key: 'scan-activity', label: 'packages.reports.scanActivity' },
  { key: 'package-exceptions', label: 'packages.reports.packageExceptions' },
  { key: 'missing-packages', label: 'packages.reports.missingPackages' },
  { key: 'damaged-packages', label: 'packages.reports.damagedPackages' },
  { key: 'returns', label: 'packages.reports.returns' },
  { key: 'delivery-performance', label: 'packages.reports.deliveryPerformance' },
]

function isoDaysAgo(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() - days)
  return date.toISOString().slice(0, 10)
}

/** XLSX download for the eight package reports (package_reports.export). */
export function PackageReportsControl() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showError } = useToast()
  const [report, setReport] = useState(REPORTS[0].key)
  const [from, setFrom] = useState(isoDaysAgo(30))
  const [to, setTo] = useState(isoDaysAgo(0))
  const [busy, setBusy] = useState(false)

  if (!hasPermission('package_reports.export')) return null

  async function download() {
    setBusy(true)
    try {
      const response = await fetch(
        `${apiBaseUrl}/api/reports/packages/${report}?from=${from}&to=${to}`,
        { headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` } },
      )
      if (!response.ok) throw new Error()
      const disposition = response.headers.get('Content-Disposition') ?? ''
      const match = /filename="?([^";]+)"?/.exec(disposition)
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = match?.[1] ?? `colli-${report}.xlsx`
      anchor.click()
      URL.revokeObjectURL(url)
    } catch {
      showError(t('packages.reports.exportFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className="pk-reports-control">
      <select value={report} onChange={(e) => setReport(e.target.value)} disabled={busy} aria-label={t('packages.reports.reportAria')}>
        {REPORTS.map((item) => (
          <option key={item.key} value={item.key}>
            {t(item.label)}
          </option>
        ))}
      </select>
      <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} disabled={busy} aria-label={t('packages.reports.fromAria')} />
      <input type="date" value={to} onChange={(e) => setTo(e.target.value)} disabled={busy} aria-label={t('packages.reports.toAria')} />
      <Button variant="secondary" onClick={() => void download()} disabled={busy}>
        {busy ? t('packages.reports.exportBusy') : t('packages.reports.exportButton')}
      </Button>
    </span>
  )
}
