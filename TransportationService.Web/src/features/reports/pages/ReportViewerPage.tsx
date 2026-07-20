import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { ORDER_STATUS_LABELS, ORDER_STATUSES } from '../../transport-orders/types'
import { getReportCatalog } from '../api/reportsApi'
import type { ReportCatalogEntry } from '../types'
import './reports.css'

function isoDaysAgo(days: number): string {
  return new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10)
}

/**
 * Generieke runner voor export-rapporten uit de catalogus: toont uitsluitend de filters
 * die het rapport declareert en downloadt via het BESTAANDE endpoint van het rapport.
 * Page-rapporten worden doorgestuurd naar hun eigen scherm.
 */
export function ReportViewerPage() {
  const { reportId } = useParams<{ reportId: string }>()
  const navigate = useNavigate()
  const toast = useToast()

  const [report, setReport] = useState<ReportCatalogEntry | null | undefined>(undefined)
  const [from, setFrom] = useState(isoDaysAgo(30))
  const [to, setTo] = useState(isoDaysAgo(0))
  const [search, setSearch] = useState('')
  const [orderStatus, setOrderStatus] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getReportCatalog()
      .then((data) => {
        if (mounted) setReport(data.find((entry) => entry.id === reportId) ?? null)
      })
      .catch(() => {
        if (mounted) setReport(null)
      })
    return () => {
      mounted = false
    }
  }, [reportId])

  useEffect(() => {
    if (report?.kind === 'Page' && report.route) {
      navigate(report.route, { replace: true })
    }
  }, [report, navigate])

  if (report === undefined) return <LoadingState message="Rapport laden..." />
  if (report === null || report.kind === 'ComingSoon' || !report.endpoint) {
    return <ErrorState message="Dit rapport bestaat niet of is nog niet beschikbaar." />
  }

  const has = (filter: string) => report.filters.includes(filter)

  async function download() {
    if (!report?.endpoint) return
    setBusy(true)
    try {
      const url = new URL(`${apiBaseUrl}${report.endpoint}`, window.location.origin)
      if (has('dateRange')) {
        url.searchParams.set('from', from)
        url.searchParams.set('to', to)
      }
      if (has('search') && search) url.searchParams.set('search', search)
      if (has('orderStatus') && orderStatus) url.searchParams.set('status', orderStatus)

      const response = await fetch(url.toString(), {
        headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
      })
      if (!response.ok) throw new Error()
      const blob = await response.blob()
      const objectUrl = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = objectUrl
      anchor.download = `${report.id}.${report.fileType ?? 'xlsx'}`
      anchor.click()
      URL.revokeObjectURL(objectUrl)
    } catch {
      toast.showError('Het rapport kon niet worden gedownload.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <BackButton to="/reports" label="Terug naar rapportcentrum" />
      <PageHeader title={report.title} subtitle={report.description} />

      <div className="reports-runner">
        {has('dateRange') && (
          <>
            <FormField label="Van" htmlFor="report-from">
              <input id="report-from" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
            </FormField>
            <FormField label="Tot en met" htmlFor="report-to">
              <input id="report-to" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
            </FormField>
          </>
        )}
        {has('search') && (
          <FormField label="Zoekterm" htmlFor="report-search">
            <input
              id="report-search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Optioneel"
            />
          </FormField>
        )}
        {has('orderStatus') && (
          <FormField label="Status" htmlFor="report-status">
            <select id="report-status" value={orderStatus} onChange={(event) => setOrderStatus(event.target.value)}>
              <option value="">Alle statussen</option>
              {ORDER_STATUSES.map((status) => (
                <option key={status} value={status}>
                  {ORDER_STATUS_LABELS[status]}
                </option>
              ))}
            </select>
          </FormField>
        )}
      </div>

      <Button onClick={() => void download()} disabled={busy}>
        {busy ? 'Bezig met genereren…' : `Download (${(report.fileType ?? 'xlsx').toUpperCase()})`}
      </Button>
    </div>
  )
}
