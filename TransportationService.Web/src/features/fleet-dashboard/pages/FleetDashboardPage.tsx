import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatDecimal } from '../../../utils/numbers'
import { useAuth } from '../../auth/authContextValue'
import { DAMAGE_SEVERITY_LABELS, DAMAGE_STATUS_LABELS } from '../../damage/types'
import { FLEET_DOCUMENT_STATUS_LABELS, fleetDocumentDisplayName } from '../../fleet-documents/types'
import { FUEL_WARNING_LABELS } from '../../fuel/types'
import { INSPECTION_URGENCY_LABELS, inspectionDisplayName } from '../../inspections/types'
import { maintenanceDisplayName } from '../../maintenance/types'
import { getFleetDashboard } from '../api/fleetDashboardApi'
import type { FleetDashboard } from '../types'
import './fleet-dashboard.css'

const URGENCY_TONE: Record<string, BadgeTone> = {
  Ok: 'info',
  DueSoon: 'warning',
  Overdue: 'danger',
  Completed: 'success',
}

const DOCUMENT_TONE: Record<string, BadgeTone> = {
  NoExpiry: 'neutral',
  Valid: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
}

const SEVERITY_TONE: Record<string, BadgeTone> = {
  Minor: 'info',
  Moderate: 'warning',
  Severe: 'danger',
  TotalLoss: 'danger',
}

export function FleetDashboardPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()

  const [dashboard, setDashboard] = useState<FleetDashboard | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getFleetDashboard()
      .then((data) => {
        if (!mounted) return
        setDashboard(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('fleet.dashboard.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [t])

  function ownerLink(vehicleId: string | null, trailerId: string | null): string {
    return vehicleId ? `/vehicles/${vehicleId}` : `/trailers/${trailerId}`
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.modules.vloot') }]} />
      <PageHeader
        title={t('navigation.menu.modules.vloot')}
        action={
          <span className="fd-quick-actions">
            {hasPermission('vehicles.create') && (
              <Button variant="secondary" onClick={() => navigate('/vehicles/new')}>
                {t('vehicles.list.new')}
              </Button>
            )}
            {hasPermission('trailers.create') && (
              <Button variant="secondary" onClick={() => navigate('/trailers/new')}>
                {t('trailers.list.new')}
              </Button>
            )}
            {hasPermission('tank_cards.view') && (
              <Button variant="secondary" onClick={() => navigate('/tank-cards')}>
                {t('navigation.menu.tankCards')}
              </Button>
            )}
          </span>
        }
      />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && dashboard === null && <p className="placeholder-text">{t('fleet.dashboard.loading')}</p>}

      {!loadError && dashboard !== null && (
        <>
          <div className="fd-counts">
            <button type="button" className="fd-count-card" onClick={() => navigate('/vehicles')}>
              <h3>{t('navigation.menu.vehicles')}</h3>
              <div className="fd-count-total">{dashboard.vehicles.total}</div>
              <dl>
                <div>
                  <dt>{t('vehicles.status.Available')}</dt>
                  <dd>{dashboard.vehicles.available}</dd>
                </div>
                <div>
                  <dt>{t('vehicles.status.InUse')}</dt>
                  <dd>{dashboard.vehicles.inUse}</dd>
                </div>
                <div>
                  <dt>{t('vehicles.status.InMaintenance')}</dt>
                  <dd>{dashboard.vehicles.inMaintenance}</dd>
                </div>
                <div>
                  <dt>{t('vehicles.status.OutOfService')}</dt>
                  <dd>{dashboard.vehicles.outOfService}</dd>
                </div>
                <div>
                  <dt>{t('fleet.dashboard.inactive')}</dt>
                  <dd>{dashboard.vehicles.inactive}</dd>
                </div>
              </dl>
            </button>
            <button type="button" className="fd-count-card" onClick={() => navigate('/trailers')}>
              <h3>{t('navigation.menu.trailers')}</h3>
              <div className="fd-count-total">{dashboard.trailers.total}</div>
              <dl>
                <div>
                  <dt>{t('trailers.status.Available')}</dt>
                  <dd>{dashboard.trailers.available}</dd>
                </div>
                <div>
                  <dt>{t('trailers.status.InUse')}</dt>
                  <dd>{dashboard.trailers.inUse}</dd>
                </div>
                <div>
                  <dt>{t('trailers.status.InMaintenance')}</dt>
                  <dd>{dashboard.trailers.inMaintenance}</dd>
                </div>
                <div>
                  <dt>{t('trailers.status.OutOfService')}</dt>
                  <dd>{dashboard.trailers.outOfService}</dd>
                </div>
                <div>
                  <dt>{t('fleet.dashboard.inactive')}</dt>
                  <dd>{dashboard.trailers.inactive}</dd>
                </div>
              </dl>
            </button>
          </div>

          <div className="fd-grid">
            <section className="fd-panel">
              <h3>
                {t('fleet.dashboard.maintenancePlanned')} <Badge tone={dashboard.maintenanceDueCount > 0 ? 'warning' : 'success'}>{dashboard.maintenanceDueCount}</Badge>
              </h3>
              {dashboard.maintenanceDue.length === 0 && <p className="fd-empty">{t('fleet.dashboard.emptyMaintenance')}</p>}
              <ul>
                {dashboard.maintenanceDue.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main">{t(maintenanceDisplayName(item))}</span>
                      <span className="fd-row-meta">
                        {item.scheduledDate ?? '—'}
                        {item.isOverdue && <Badge tone="danger">{t('maintenance.overdue')}</Badge>}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel">
              <h3>
                {t('fleet.tabs.inspections')} <Badge tone={dashboard.inspectionsDueCount > 0 ? 'warning' : 'success'}>{dashboard.inspectionsDueCount}</Badge>
              </h3>
              {dashboard.inspectionsDue.length === 0 && <p className="fd-empty">{t('fleet.dashboard.emptyInspections')}</p>}
              <ul>
                {dashboard.inspectionsDue.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main">{t(inspectionDisplayName(item))}</span>
                      <span className="fd-row-meta">
                        {item.dueDate}
                        <Badge tone={URGENCY_TONE[item.urgency] ?? 'info'}>{t(INSPECTION_URGENCY_LABELS[item.urgency])}</Badge>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel">
              <h3>
                {t('fleet.tabs.documents')} <Badge tone={dashboard.documentsExpiringCount > 0 ? 'warning' : 'success'}>{dashboard.documentsExpiringCount}</Badge>
              </h3>
              {dashboard.documentsExpiring.length === 0 && <p className="fd-empty">{t('fleet.dashboard.emptyDocuments')}</p>}
              <ul>
                {dashboard.documentsExpiring.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main">{t(fleetDocumentDisplayName(item))}</span>
                      <span className="fd-row-meta">
                        {item.expiryDate}
                        <Badge tone={DOCUMENT_TONE[item.status] ?? 'info'}>{t(FLEET_DOCUMENT_STATUS_LABELS[item.status])}</Badge>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel">
              <h3>
                {t('fleet.dashboard.openDamage')} <Badge tone={dashboard.openDamageCount > 0 ? 'warning' : 'success'}>{dashboard.openDamageCount}</Badge>
              </h3>
              {dashboard.recentDamage.length === 0 && <p className="fd-empty">{t('fleet.dashboard.emptyDamage')}</p>}
              <ul>
                {dashboard.recentDamage.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main" title={item.description}>
                        {item.description}
                      </span>
                      <span className="fd-row-meta">
                        {item.incidentDate}
                        <Badge tone={SEVERITY_TONE[item.severity] ?? 'info'}>{t(DAMAGE_SEVERITY_LABELS[item.severity])}</Badge>
                        <Badge tone="neutral">{t(DAMAGE_STATUS_LABELS[item.status])}</Badge>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel fd-panel-wide">
              <h3>
                {t('fleet.dashboard.fuelWarnings')}{' '}
                <Badge tone={dashboard.fuelWarnings.length > 0 ? 'warning' : 'success'}>{dashboard.fuelWarnings.length}</Badge>
              </h3>
              {dashboard.fuelWarnings.length === 0 && <p className="fd-empty">{t('fleet.dashboard.emptyFuel')}</p>}
              <ul>
                {dashboard.fuelWarnings.map((item) => (
                  <li key={item.transactionId}>
                    <button type="button" className="fd-row" onClick={() => navigate(`/vehicles/${item.vehicleId}`)}>
                      <span className="fd-row-owner">{item.vehicleInternalNumber}</span>
                      <span className="fd-row-main">{item.warnings.map((w) => t(FUEL_WARNING_LABELS[w])).join(', ')}</span>
                      <span className="fd-row-meta">
                        {item.transactionDate}
                        {item.consumptionLPer100Km !== null && (
                          <Badge tone="warning">{formatDecimal(item.consumptionLPer100Km, Number.isInteger(item.consumptionLPer100Km) ? 0 : 1)} l/100km</Badge>
                        )}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          </div>
        </>
      )}
    </div>
  )
}
