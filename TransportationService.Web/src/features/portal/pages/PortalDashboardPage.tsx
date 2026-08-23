import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getMyDashboard } from '../api/portalApi'
import { WorkStatusCard } from '../../time-attendance/components/WorkStatusCard'
import { PORTAL_MODULES, visibleModules } from '../modules'
import type { MyDashboard } from '../types'
import './portal.css'

interface CardDef {
  to: string
  icon: string
  label: string
  value: string
  attention?: boolean
}

/** Landing page of the employee portal: own numbers, module launcher, big tap targets. */
export function PortalDashboardPage() {
  const { hasAnyPermission } = useAuth()
  const { t } = useLocale()
  const [dashboard, setDashboard] = useState<MyDashboard | null>(null)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    let mounted = true
    getMyDashboard()
      .then((data) => {
        if (!mounted) return
        setDashboard(data)
        setLoadError(false)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  if (loadError) return <ErrorState message={t('portalHome.dashboard.loadFailed')} />
  if (!dashboard) return <LoadingState message={t('portalHome.dashboard.loading')} />

  const cards: CardDef[] = [
    ...(dashboard.isDriver
      ? [
          {
            to: '/my-trips',
            icon: '🚚',
            label: t('portalHome.dashboard.cardTripsToday'),
            value: String(dashboard.tripsToday),
            attention: dashboard.tripsToday > 0,
          },
          { to: '/my-trips', icon: '🗓', label: t('portalHome.dashboard.cardTripsWeek'), value: String(dashboard.tripsThisWeek) },
        ]
      : []),
    {
      to: '/portal/absences',
      icon: '🏖',
      label: t('portalHome.dashboard.cardOpenRequests'),
      value: String(dashboard.openAbsenceRequests),
      attention: dashboard.openAbsenceRequests > 0,
    },
    { to: '/portal/absences', icon: '✅', label: t('portalHome.dashboard.cardApprovedUpcoming'), value: String(dashboard.upcomingApprovedAbsences) },
    {
      to: '/notifications',
      icon: '🔔',
      label: t('portalHome.dashboard.cardUnreadNotifications'),
      value: String(dashboard.unreadNotifications),
      attention: dashboard.unreadNotifications > 0,
    },
    {
      to: '/portal/qualifications',
      icon: '🪪',
      label: t('portalHome.dashboard.cardExpiringQualifications'),
      value: String(dashboard.expiringQualifications),
      attention: dashboard.expiringQualifications > 0,
    },
  ]

  return (
    <div>
      <PageHeader title={t('portalHome.dashboard.greeting', { firstName: dashboard.firstName })} subtitle={t('portalHome.dashboard.subtitle')} />
      {hasAnyPermission(['attendance.self']) && <WorkStatusCard />}
      <div className="portal-cards">
        {cards.map((card) => (
          <Link key={`${card.to}-${card.label}`} to={card.to} className={`portal-card ${card.attention ? 'portal-card-attention' : ''}`}>
            <span className="portal-card-icon" aria-hidden="true">
              {card.icon}
            </span>
            <span className="portal-card-value">{card.value}</span>
            <span className="portal-card-label">{card.label}</span>
          </Link>
        ))}
      </div>
      <div className="portal-quick-links">
        <Link to="/portal/absences" className="portal-quick-link">
          {t('portalHome.dashboard.quickRequestLeave')}
        </Link>
        {dashboard.isDriver && (
          <Link to="/my-trips" className="portal-quick-link">
            {t('portalHome.dashboard.quickMyTrips')}
          </Link>
        )}
      </div>

      <h2 className="portal-modules-title">{t('portalHome.modules.title')}</h2>
      <nav className="portal-modules" aria-label={t('portalHome.modules.title')}>
        {visibleModules(PORTAL_MODULES, hasAnyPermission).map((module) => (
          <Link key={`${module.to}-${module.label}`} to={module.to} className="portal-module">
            <span className="portal-module-icon" aria-hidden="true">
              {module.icon}
            </span>
            <span className="portal-module-text">
              <span className="portal-module-label">{t(module.label)}</span>
              <span className="portal-module-description">{t(module.description)}</span>
            </span>
          </Link>
        ))}
      </nav>
    </div>
  )
}
