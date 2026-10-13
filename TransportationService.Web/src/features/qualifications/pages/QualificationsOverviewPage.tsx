import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
  const navigate = useNavigate()
  const [windowDays, setWindowDays] = useState<(typeof WINDOW_OPTIONS)[number]>(30)
  const [expiring, setExpiring] = useState<ExpiringQualification[] | null>(null)
  const [expired, setExpired] = useState<ExpiringQualification[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [errorKey, setErrorKey] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getExpiringQualifications(windowDays), getExpiredQualifications()])
      .then(([expiringRows, expiredRows]) => {
        if (!mounted) return
        setExpiring(expiringRows)
        setExpired(expiredRows)
        setErrorKey(null)
      })
      .catch(() => {
        if (mounted) setErrorKey('qualifications.page.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [windowDays])

  const columns: Column<ExpiringQualification>[] = [
    {
      key: 'employee',
      header: t('qualifications.page.colEmployee'),
      render: (row) => (
        <div>
          <div className="qual-overview-name">{row.employeeName}</div>
          <div className="qual-overview-number">
            {row.employeeNumber}
            {row.employeeIsDriver ? ` ${t('qualifications.page.driverSuffix')}` : ''}
          </div>
        </div>
      ),
    },
    { key: 'type', header: t('qualifications.page.colQualification'), render: (row) => row.qualificationTypeName },
    { key: 'expiry', header: t('qualifications.page.colExpiry'), width: '130px', render: (row) => row.expiryDate ?? '—' },
    {
      key: 'status',
      header: t('qualifications.page.colStatus'),
      width: '170px',
      render: (row) => (
        <Badge tone={QUALIFICATION_STATUS_TONES[row.effectiveStatus]}>
          {/* t() vertaalt sleutels en laat reeds-Nederlandse labels ongemoeid (fallback = key). */}
          {t(QUALIFICATION_STATUS_LABELS[row.effectiveStatus])}
        </Badge>
      ),
    },
  ]

  const openEmployee = (row: ExpiringQualification) => navigate(`/employees/${row.employeeId}?tab=kwalificaties`)

  return (
    <div>
      <Breadcrumbs items={[{ label: t('qualifications.page.breadcrumb') }]} />
      <PageHeader
        title={t('qualifications.page.title')}
        subtitle={t('qualifications.page.subtitle')}
      />

      <section className="qual-overview-section">
        <div className="qual-overview-header">
          <h2>{t('qualifications.page.expiringTitle')}</h2>
          <div role="group" aria-label={t('qualifications.page.windowAria')} className="qual-overview-window">
            {WINDOW_OPTIONS.map((days) => (
              <button
                key={days}
                type="button"
                className={windowDays === days ? 'qualification-filter is-active' : 'qualification-filter'}
                onClick={() => setWindowDays(days)}
              >
                {t('qualifications.page.windowDays', { days })}
              </button>
            ))}
          </div>
        </div>
        <DataTable
          columns={columns}
          rows={expiring ?? []}
          rowKey={(row) => row.id}
          isLoading={expiring === null && !errorKey}
          error={errorKey ? t(errorKey) : null}
          emptyMessage={t('qualifications.page.emptyExpiring', { days: windowDays })}
          loadingMessage={t('qualifications.page.loading')}
          onRowClick={openEmployee}
        />
      </section>

      <section className="qual-overview-section">
        <h2>{t('qualifications.page.expiredTitle')}</h2>
        <DataTable
          columns={columns}
          rows={expired ?? []}
          rowKey={(row) => row.id}
          isLoading={expired === null && !errorKey}
          error={errorKey ? t(errorKey) : null}
          emptyMessage={t('qualifications.page.emptyExpired')}
          loadingMessage={t('qualifications.page.loading')}
          onRowClick={openEmployee}
        />
      </section>
    </div>
  )
}
