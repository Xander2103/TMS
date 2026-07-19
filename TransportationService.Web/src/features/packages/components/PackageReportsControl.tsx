import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

const REPORTS: Array<{ key: string; label: string }> = [
  { key: 'order-packages', label: 'Colli per opdracht' },
  { key: 'trip-packages', label: 'Colli per rit' },
  { key: 'scan-activity', label: 'Scanactiviteit' },
  { key: 'package-exceptions', label: 'Colli-meldingen' },
  { key: 'missing-packages', label: 'Vermiste colli' },
  { key: 'damaged-packages', label: 'Beschadigde colli' },
  { key: 'returns', label: 'Retouren' },
  { key: 'delivery-performance', label: 'Leverprestatie per dag' },
]

function isoDaysAgo(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() - days)
  return date.toISOString().slice(0, 10)
}

/** XLSX download for the eight package reports (package_reports.export). */
export function PackageReportsControl() {
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
      showError('De export is mislukt.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className="pk-reports-control">
      <select value={report} onChange={(e) => setReport(e.target.value)} disabled={busy} aria-label="Colli-rapport">
        {REPORTS.map((item) => (
          <option key={item.key} value={item.key}>
            {item.label}
          </option>
        ))}
      </select>
      <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} disabled={busy} aria-label="Van" />
      <input type="date" value={to} onChange={(e) => setTo(e.target.value)} disabled={busy} aria-label="Tot" />
      <Button variant="secondary" onClick={() => void download()} disabled={busy}>
        {busy ? 'Exporteren…' : 'Colli-rapport (Excel)'}
      </Button>
    </span>
  )
}
