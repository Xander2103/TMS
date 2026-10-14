import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AlertTriangle } from 'lucide-react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { useListDensity } from '../../../components/ui/listDensity'
import { useUrlListState } from '../../../hooks/useUrlListState'
import { useLocale } from '../../../i18n/localeContext'
import { getExpiredQualifications, getExpiringQualifications } from '../../employees/api/qualificationsApi'
import {
  QUALIFICATION_STATUS_LABELS,
  QUALIFICATION_STATUS_TONES,
  type ExpiringQualification,
} from '../../employees/types/qualification'
import { toIsoDate } from '../../../components/calendar/dateUtils'
import '../../../components/ui/DensityToggle.css'
import './qualifications-overview.css'
import { formatDate } from '../../../utils/dates'

const WINDOW_OPTIONS = ['30', '60', '90'] as const

/** B14: the chosen window lives in the URL (refresh and shared links keep it). */
const LIST_STATE = {
  defaults: { window: '30' },
  allowed: { window: WINDOW_OPTIONS },
}

const DAY_MS = 86_400_000

/** Whole days from today to the expiry date (negative = already expired). */
function daysUntil(expiryDate: string): number {
  const today = new Date(`${toIsoDate(new Date())}T00:00:00`).getTime()
  return Math.round((new Date(`${expiryDate}T00:00:00`).getTime() - today) / DAY_MS)
}

/**
 * Tenant-wide expiry radar: which qualifications expire within the chosen window and
 * which are already expired, with a direct link to the employee's qualification tab.
 */
export function QualificationsOverviewPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  const { state, apply } = useUrlListState(LIST_STATE)
  const windowDays = Number(state.window)
  const [density, setDensity] = useListDensity('qualifications')
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

  function relativeExpiry(expiryDate: string): string {
    const days = daysUntil(expiryDate)
    if (days === 0) return t('qualifications.page.today')
    return days > 0 ? t('qualifications.page.inDays', { count: days }) : t('qualifications.page.daysAgo', { count: -days })
  }

  const columns: Column<ExpiringQualification>[] = [
    {
      key: 'employee',
      header: t('qualifications.page.colEmployee'),
      render: (row) => {
        const suffix = row.employeeIsDriver ? ` ${t('qualifications.page.driverSuffix')}` : ''
        return (
          <TruncatedText tooltip={`${row.employeeName} (${row.employeeNumber})${suffix}`}>
            <span className="qual-overview-name">{row.employeeName}</span>{' '}
            <span className="qual-overview-number">
              {row.employeeNumber}
              {suffix}
            </span>
          </TruncatedText>
        )
      },
    },
    {
      key: 'type',
      header: t('qualifications.page.colQualification'),
      render: (row) => <TruncatedText tooltip={row.qualificationTypeName}>{row.qualificationTypeName}</TruncatedText>,
    },
    {
      key: 'expiry',
      header: t('qualifications.page.colExpiry'),
      width: '250px',
      render: (row) =>
        row.expiryDate ? (
          <span className="ui-nowrap">
            {formatDate(row.expiryDate)} <span className="qual-overview-relative">· {relativeExpiry(row.expiryDate)}</span>
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'status',
      header: t('qualifications.page.colStatus'),
      width: '190px',
      render: (row) => (
        <Badge tone={QUALIFICATION_STATUS_TONES[row.effectiveStatus]}>
          {/* Expired is marked by an icon as well as colour and label. */}
          {row.effectiveStatus === 'Expired' && <AlertTriangle size={12} aria-hidden="true" className="qual-overview-icon" />}
          {/* t() vertaalt sleutels en laat reeds-Nederlandse labels ongemoeid (fallback = key). */}
          {t(QUALIFICATION_STATUS_LABELS[row.effectiveStatus])}
        </Badge>
      ),
    },
  ]

  const openEmployee = (row: ExpiringQualification) => navigate(`/employees/${row.employeeId}?tab=kwalificaties`)

  return (
    <div>
      <PageHeader
        title={t('qualifications.page.title')}
        subtitle={t('qualifications.page.subtitle')}
      />

      <section className="qual-overview-section">
        <div className="qual-overview-header">
          <h2>{t('qualifications.page.expiringTitle')}</h2>
          <div className="qual-overview-tools">
            <div role="group" aria-label={t('qualifications.page.windowAria')} className="ui-segmented">
              {WINDOW_OPTIONS.map((days) => (
                <button
                  key={days}
                  type="button"
                  aria-pressed={state.window === days}
                  onClick={() => apply({ window: days })}
                >
                  {t('qualifications.page.windowDays', { days })}
                </button>
              ))}
            </div>
            <DensityToggle value={density} onChange={setDensity} />
          </div>
        </div>
        <DataTable
          fixedLayout
          density={density}
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
          fixedLayout
          density={density}
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
