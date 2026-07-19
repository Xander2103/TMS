import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { getExpiredQualifications, getExpiringQualifications } from '../../employees/api/qualificationsApi'
import {
  QUALIFICATION_STATUS_LABELS,
  QUALIFICATION_STATUS_TONES,
  type ExpiringQualification,
} from '../../employees/types/qualification'
import './qualifications-overview.css'

const WINDOW_OPTIONS = [30, 60, 90] as const

/**
 * Tenant-wide expiry radar: which qualifications expire within the chosen window and
 * which are already expired, with a direct link to the employee's qualification tab.
 */
export function QualificationsOverviewPage() {
  const navigate = useNavigate()
  const [windowDays, setWindowDays] = useState<(typeof WINDOW_OPTIONS)[number]>(30)
  const [expiring, setExpiring] = useState<ExpiringQualification[] | null>(null)
  const [expired, setExpired] = useState<ExpiringQualification[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getExpiringQualifications(windowDays), getExpiredQualifications()])
      .then(([expiringRows, expiredRows]) => {
        if (!mounted) return
        setExpiring(expiringRows)
        setExpired(expiredRows)
        setError(null)
      })
      .catch(() => {
        if (mounted) setError('Kwalificaties konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [windowDays])

  const columns: Column<ExpiringQualification>[] = [
    {
      key: 'employee',
      header: 'Medewerker',
      render: (row) => (
        <div>
          <div className="qual-overview-name">{row.employeeName}</div>
          <div className="qual-overview-number">
            {row.employeeNumber}
            {row.employeeIsDriver ? ' · chauffeur' : ''}
          </div>
        </div>
      ),
    },
    { key: 'type', header: 'Kwalificatie', render: (row) => row.qualificationTypeName },
    { key: 'expiry', header: 'Vervaldatum', width: '130px', render: (row) => row.expiryDate ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '170px',
      render: (row) => (
        <Badge tone={QUALIFICATION_STATUS_TONES[row.effectiveStatus]}>
          {QUALIFICATION_STATUS_LABELS[row.effectiveStatus]}
        </Badge>
      ),
    },
  ]

  const openEmployee = (row: ExpiringQualification) => navigate(`/employees/${row.employeeId}?tab=kwalificaties`)

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Kwalificaties' }]} />
      <PageHeader
        title="Kwalificaties"
        subtitle="Vervallende en verlopen kwalificaties, licenties en keuringen van alle medewerkers."
      />

      <section className="qual-overview-section">
        <div className="qual-overview-header">
          <h2>Vervalt binnenkort</h2>
          <div role="group" aria-label="Periode" className="qual-overview-window">
            {WINDOW_OPTIONS.map((days) => (
              <button
                key={days}
                type="button"
                className={windowDays === days ? 'qualification-filter is-active' : 'qualification-filter'}
                onClick={() => setWindowDays(days)}
              >
                {days} dagen
              </button>
            ))}
          </div>
        </div>
        <DataTable
          columns={columns}
          rows={expiring ?? []}
          rowKey={(row) => row.id}
          isLoading={expiring === null && !error}
          error={error}
          emptyMessage={`Geen kwalificaties die binnen ${windowDays} dagen vervallen.`}
          loadingMessage="Laden…"
          onRowClick={openEmployee}
        />
      </section>

      <section className="qual-overview-section">
        <h2>Verlopen</h2>
        <DataTable
          columns={columns}
          rows={expired ?? []}
          rowKey={(row) => row.id}
          isLoading={expired === null && !error}
          error={error}
          emptyMessage="Geen verlopen kwalificaties. 🎉"
          loadingMessage="Laden…"
          onRowClick={openEmployee}
        />
      </section>
    </div>
  )
}
