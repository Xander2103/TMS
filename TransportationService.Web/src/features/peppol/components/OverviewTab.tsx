import { useEffect, useState } from 'react'
import { KpiCard } from '../../kpi/components/KpiCard'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { getPeppolOverview, type PeppolOverview } from '../api/peppolApi'

function CheckMark({ ok, label }: { ok: boolean; label: string }) {
  return (
    <span className={ok ? 'peppol-check peppol-check-ok' : 'peppol-check peppol-check-missing'}>
      <span aria-hidden="true">{ok ? '✓' : '⚠'}</span> {label}
    </span>
  )
}

/** "Overzicht": configuratiechecklist per eigen bedrijf + stat-tegels + klantwaarschuwingen. */
export function OverviewTab() {
  const { t } = useLocale()
  const [overview, setOverview] = useState<PeppolOverview | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [errorKey, setErrorKey] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getPeppolOverview()
      .then((data) => {
        if (mounted) setOverview(data)
      })
      .catch(() => {
        if (mounted) setErrorKey('peppol.overview.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (errorKey) return <p className="placeholder-text">{t(errorKey)}</p>
  if (overview === null) return <p className="placeholder-text">{t('peppol.overview.loading')}</p>

  return (
    <div>
      <div className="kpi-grid">
        <KpiCard label={t('invoices.peppolStatus.Queued')} value={overview.counts.queued} to="/peppol?tab=uitgaand" />
        <KpiCard
          label={t('invoices.peppolStatus.Delivered')}
          value={overview.counts.delivered}
          to="/peppol?tab=uitgaand"
        />
        <KpiCard
          label={t('invoices.peppolStatus.Failed')}
          value={overview.counts.failed}
          tone={overview.counts.failed > 0 ? 'danger' : undefined}
          to="/peppol?tab=uitgaand"
        />
        <KpiCard
          label={t('peppol.overview.kpiReceivedIncoming')}
          value={overview.counts.receivedIncoming}
          to="/peppol?tab=inkomend"
        />
      </div>

      {overview.customersEnabledWithoutPeppolId > 0 && (
        <p className="peppol-warning" role="alert">
          {t('peppol.overview.customersEnabledWithoutId', { count: overview.customersEnabledWithoutPeppolId })}
        </p>
      )}
      {overview.activeCustomersMissingPeppolData > 0 && (
        <p className="peppol-info-note">
          {t('peppol.overview.customersMissingData', { count: overview.activeCustomersMissingPeppolData })}
        </p>
      )}

      <section className="edi-section">
        <h3>{t('peppol.overview.checklistTitle')}</h3>
        <ul className="peppol-checklist">
          {overview.legalEntities.map((entity) => (
            <li key={entity.legalEntityId} className="peppol-checklist-row">
              <span className="peppol-checklist-name">{entity.legalEntityName}</span>
              <CheckMark ok={entity.hasPeppolIdentity} label={t('peppol.fields.peppolId')} />
              <CheckMark ok={entity.hasVatNumber} label={t('peppol.fields.vatNumber')} />
              <CheckMark ok={entity.hasIban} label={t('peppol.fields.iban')} />
              <Badge tone={entity.enabled ? 'success' : 'neutral'}>
                {entity.enabled ? t('ui.statusBadges.active') : t('ui.statusBadges.inactive')}
              </Badge>
              <Badge tone={entity.environment === 'Live' ? 'success' : 'info'}>
                {entity.environment === 'Live' ? t('peppol.environment.Live') : t('peppol.environment.Sandbox')}
              </Badge>
            </li>
          ))}
          {overview.legalEntities.length === 0 && <li>{t('peppol.overview.noEntities')}</li>}
        </ul>
      </section>
    </div>
  )
}
