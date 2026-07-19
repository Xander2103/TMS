import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
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
        if (mounted) setLoadError('Het vlootoverzicht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  function ownerLink(vehicleId: string | null, trailerId: string | null): string {
    return vehicleId ? `/vehicles/${vehicleId}` : `/trailers/${trailerId}`
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Vloot' }]} />
      <PageHeader
        title="Vloot"
        action={
          <span className="fd-quick-actions">
            {hasPermission('vehicles.create') && (
              <Button variant="secondary" onClick={() => navigate('/vehicles/new')}>
                Nieuw voertuig
              </Button>
            )}
            {hasPermission('trailers.create') && (
              <Button variant="secondary" onClick={() => navigate('/trailers/new')}>
                Nieuwe oplegger
              </Button>
            )}
            {hasPermission('tank_cards.view') && (
              <Button variant="secondary" onClick={() => navigate('/tank-cards')}>
                Tankkaarten
              </Button>
            )}
          </span>
        }
      />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && dashboard === null && <p className="placeholder-text">Vlootoverzicht laden…</p>}

      {!loadError && dashboard !== null && (
        <>
          <div className="fd-counts">
            <button type="button" className="fd-count-card" onClick={() => navigate('/vehicles')}>
              <h3>Voertuigen</h3>
              <div className="fd-count-total">{dashboard.vehicles.total}</div>
              <dl>
                <div>
                  <dt>Beschikbaar</dt>
                  <dd>{dashboard.vehicles.available}</dd>
                </div>
                <div>
                  <dt>In gebruik</dt>
                  <dd>{dashboard.vehicles.inUse}</dd>
                </div>
                <div>
                  <dt>In onderhoud</dt>
                  <dd>{dashboard.vehicles.inMaintenance}</dd>
                </div>
                <div>
                  <dt>Buiten dienst</dt>
                  <dd>{dashboard.vehicles.outOfService}</dd>
                </div>
                <div>
                  <dt>Inactief</dt>
                  <dd>{dashboard.vehicles.inactive}</dd>
                </div>
              </dl>
            </button>
            <button type="button" className="fd-count-card" onClick={() => navigate('/trailers')}>
              <h3>Opleggers</h3>
              <div className="fd-count-total">{dashboard.trailers.total}</div>
              <dl>
                <div>
                  <dt>Beschikbaar</dt>
                  <dd>{dashboard.trailers.available}</dd>
                </div>
                <div>
                  <dt>In gebruik</dt>
                  <dd>{dashboard.trailers.inUse}</dd>
                </div>
                <div>
                  <dt>In onderhoud</dt>
                  <dd>{dashboard.trailers.inMaintenance}</dd>
                </div>
                <div>
                  <dt>Buiten dienst</dt>
                  <dd>{dashboard.trailers.outOfService}</dd>
                </div>
                <div>
                  <dt>Inactief</dt>
                  <dd>{dashboard.trailers.inactive}</dd>
                </div>
              </dl>
            </button>
          </div>

          <div className="fd-grid">
            <section className="fd-panel">
              <h3>
                Onderhoud gepland <Badge tone={dashboard.maintenanceDueCount > 0 ? 'warning' : 'success'}>{dashboard.maintenanceDueCount}</Badge>
              </h3>
              {dashboard.maintenanceDue.length === 0 && <p className="fd-empty">Geen onderhoud binnen 30 dagen.</p>}
              <ul>
                {dashboard.maintenanceDue.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main">{maintenanceDisplayName(item)}</span>
                      <span className="fd-row-meta">
                        {item.scheduledDate ?? '—'}
                        {item.isOverdue && <Badge tone="danger">Te laat</Badge>}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel">
              <h3>
                Keuringen <Badge tone={dashboard.inspectionsDueCount > 0 ? 'warning' : 'success'}>{dashboard.inspectionsDueCount}</Badge>
              </h3>
              {dashboard.inspectionsDue.length === 0 && <p className="fd-empty">Geen keuringen binnen 30 dagen.</p>}
              <ul>
                {dashboard.inspectionsDue.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main">{inspectionDisplayName(item)}</span>
                      <span className="fd-row-meta">
                        {item.dueDate}
                        <Badge tone={URGENCY_TONE[item.urgency] ?? 'info'}>{INSPECTION_URGENCY_LABELS[item.urgency]}</Badge>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel">
              <h3>
                Documenten <Badge tone={dashboard.documentsExpiringCount > 0 ? 'warning' : 'success'}>{dashboard.documentsExpiringCount}</Badge>
              </h3>
              {dashboard.documentsExpiring.length === 0 && <p className="fd-empty">Geen documenten die binnen 60 dagen verlopen.</p>}
              <ul>
                {dashboard.documentsExpiring.map((item) => (
                  <li key={item.id}>
                    <button type="button" className="fd-row" onClick={() => navigate(ownerLink(item.vehicleId, item.trailerId))}>
                      <span className="fd-row-owner">{item.ownerNumber}</span>
                      <span className="fd-row-main">{fleetDocumentDisplayName(item)}</span>
                      <span className="fd-row-meta">
                        {item.expiryDate}
                        <Badge tone={DOCUMENT_TONE[item.status] ?? 'info'}>{FLEET_DOCUMENT_STATUS_LABELS[item.status]}</Badge>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel">
              <h3>
                Open schade <Badge tone={dashboard.openDamageCount > 0 ? 'warning' : 'success'}>{dashboard.openDamageCount}</Badge>
              </h3>
              {dashboard.recentDamage.length === 0 && <p className="fd-empty">Geen recente schademeldingen.</p>}
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
                        <Badge tone={SEVERITY_TONE[item.severity] ?? 'info'}>{DAMAGE_SEVERITY_LABELS[item.severity]}</Badge>
                        <Badge tone="neutral">{DAMAGE_STATUS_LABELS[item.status]}</Badge>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section className="fd-panel fd-panel-wide">
              <h3>
                Brandstofwaarschuwingen{' '}
                <Badge tone={dashboard.fuelWarnings.length > 0 ? 'warning' : 'success'}>{dashboard.fuelWarnings.length}</Badge>
              </h3>
              {dashboard.fuelWarnings.length === 0 && <p className="fd-empty">Geen afwijkende tankbeurten.</p>}
              <ul>
                {dashboard.fuelWarnings.map((item) => (
                  <li key={item.transactionId}>
                    <button type="button" className="fd-row" onClick={() => navigate(`/vehicles/${item.vehicleId}`)}>
                      <span className="fd-row-owner">{item.vehicleInternalNumber}</span>
                      <span className="fd-row-main">{item.warnings.map((w) => FUEL_WARNING_LABELS[w]).join(', ')}</span>
                      <span className="fd-row-meta">
                        {item.transactionDate}
                        {item.consumptionLPer100Km !== null && (
                          <Badge tone="warning">{item.consumptionLPer100Km.toLocaleString('nl-BE')} l/100km</Badge>
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
