import { useNavigate } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import {
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_SEVERITY_TONE,
  INCIDENT_STATUS_LABELS,
  INCIDENT_STATUS_TONE,
  INCIDENT_TYPE_LABELS,
  type IncidentSeverity,
  type IncidentStatus,
  type IncidentType,
} from '../../incidents/types'
import { DOSSIER_RELATION_LABELS, type DossierDetail } from '../types'

interface DossierMoreSectionProps {
  dossier: DossierDetail
  canManage: boolean
  busy: boolean
  onRemoveRelation: (relationId: string) => void
}

/** Ingeklapte compat-sectie: financieel overzicht, gerelateerde dossiers en incidenten. */
export function DossierMoreSection({ dossier, canManage, busy, onRemoveRelation }: DossierMoreSectionProps) {
  const { t } = useLocale()
  const navigate = useNavigate()
  return (
    <details className="dossier-collapsed" id="sectie-meer">
      <summary>
        {dossier.incidents.length > 0
          ? t('dossiers.more.summaryWithCount', { count: dossier.incidents.length })
          : t('dossiers.more.summary')}
      </summary>

      <h3>{t('dossiers.more.financialTitle')}</h3>
      <div className="db-kpis">
        <div className="db-kpi">
          <span className="db-kpi-label">{t('dossiers.more.agreedRevenue')}</span>
          <span className="db-kpi-value">{euro(dossier.financials.agreedOrderTotal)}</span>
        </div>
        <div className="db-kpi">
          <span className="db-kpi-label">{t('dossiers.more.invoiced')}</span>
          <span className="db-kpi-value">{euro(dossier.financials.invoicedTotal)}</span>
        </div>
        <div className="db-kpi">
          <span className="db-kpi-label">{t('dossiers.more.estimatedIncidentCost')}</span>
          <span className="db-kpi-value">{euro(dossier.financials.estimatedIncidentCost)}</span>
        </div>
        <div className="db-kpi">
          <span className="db-kpi-label">{t('dossiers.more.actualIncidentCost')}</span>
          <span className="db-kpi-value">{euro(dossier.financials.actualIncidentCost)}</span>
        </div>
      </div>

      <h3>{t('dossiers.more.relatedTitle', { count: dossier.relations.length })}</h3>
      {dossier.relations.length === 0 && <p className="placeholder-text">{t('dossiers.more.noRelated')}</p>}
      {dossier.relations.length > 0 && (
        <ul className="db-list">
          {dossier.relations.map((relation) => (
            <li key={relation.id}>
              <span className="db-row">
                <Badge tone="info">
                  {t(DOSSIER_RELATION_LABELS[relation.relationType])}
                  {relation.isOutgoing ? ' →' : ' ←'}
                </Badge>
                <button type="button" className="link-button" onClick={() => navigate(`/dossiers/${relation.otherDossierId}`)}>
                  <code>{relation.otherDossierNumber}</code> {relation.otherDossierTitle}
                </button>
                <span className="db-row-main">{relation.notes ?? ''}</span>
                {canManage && (
                  <Button variant="secondary" onClick={() => onRemoveRelation(relation.id)} disabled={busy}>
                    {t('ui.actions.delete')}
                  </Button>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}

      <h3>{t('dossiers.more.incidentsTitle', { count: dossier.incidents.length })}</h3>
      {dossier.incidents.length === 0 && <p className="placeholder-text">{t('dossiers.more.noIncidents')}</p>}
      {dossier.incidents.length > 0 && (
        <ul className="db-list">
          {dossier.incidents.map((incident) => (
            <li key={incident.id}>
              <button type="button" className="db-row" onClick={() => navigate(`/incidents/${incident.id}`)}>
                <span className="db-row-main">{incident.title}</span>
                <span className="db-row-meta">
                  {INCIDENT_TYPE_LABELS[incident.incidentType as IncidentType] ? t(INCIDENT_TYPE_LABELS[incident.incidentType as IncidentType]) : incident.incidentType}
                  <Badge tone={INCIDENT_SEVERITY_TONE[incident.severity as IncidentSeverity] ?? 'neutral'}>
                    {INCIDENT_SEVERITY_LABELS[incident.severity as IncidentSeverity] ? t(INCIDENT_SEVERITY_LABELS[incident.severity as IncidentSeverity]) : incident.severity}
                  </Badge>
                  <Badge tone={INCIDENT_STATUS_TONE[incident.status as IncidentStatus] ?? 'neutral'}>
                    {INCIDENT_STATUS_LABELS[incident.status as IncidentStatus] ? t(INCIDENT_STATUS_LABELS[incident.status as IncidentStatus]) : incident.status}
                  </Badge>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </details>
  )
}
