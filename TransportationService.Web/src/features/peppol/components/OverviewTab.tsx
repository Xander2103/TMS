import { useEffect, useState } from 'react'
import { KpiCard } from '../../kpi/components/KpiCard'
import { Badge } from '../../../components/ui/Badge'
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
  const [overview, setOverview] = useState<PeppolOverview | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getPeppolOverview()
      .then((data) => {
        if (mounted) setOverview(data)
      })
      .catch(() => {
        if (mounted) setError('Het Peppol-overzicht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (error) return <p className="placeholder-text">{error}</p>
  if (overview === null) return <p className="placeholder-text">Overzicht laden…</p>

  return (
    <div>
      <div className="kpi-grid">
        <KpiCard label="In wachtrij" value={overview.counts.queued} to="/peppol?tab=uitgaand" />
        <KpiCard label="Afgeleverd" value={overview.counts.delivered} to="/peppol?tab=uitgaand" />
        <KpiCard
          label="Mislukt"
          value={overview.counts.failed}
          tone={overview.counts.failed > 0 ? 'danger' : undefined}
          to="/peppol?tab=uitgaand"
        />
        <KpiCard label="Inkomend ontvangen" value={overview.counts.receivedIncoming} to="/peppol?tab=inkomend" />
      </div>

      {overview.customersEnabledWithoutPeppolId > 0 && (
        <p className="peppol-warning" role="alert">
          {overview.customersEnabledWithoutPeppolId}{' '}
          {overview.customersEnabledWithoutPeppolId === 1 ? 'klant' : 'klanten'} met Peppol aan maar zonder Peppol-ID
        </p>
      )}
      {overview.activeCustomersMissingPeppolData > 0 && (
        <p className="peppol-info-note">
          {overview.activeCustomersMissingPeppolData} actieve{' '}
          {overview.activeCustomersMissingPeppolData === 1 ? 'klant' : 'klanten'} zonder Peppol-gegevens
        </p>
      )}

      <section className="edi-section">
        <h3>Configuratie per eigen bedrijf</h3>
        <ul className="peppol-checklist">
          {overview.legalEntities.map((entity) => (
            <li key={entity.legalEntityId} className="peppol-checklist-row">
              <span className="peppol-checklist-name">{entity.legalEntityName}</span>
              <CheckMark ok={entity.hasPeppolIdentity} label="Peppol-ID" />
              <CheckMark ok={entity.hasVatNumber} label="BTW-nummer" />
              <CheckMark ok={entity.hasIban} label="IBAN" />
              <Badge tone={entity.enabled ? 'success' : 'neutral'}>{entity.enabled ? 'Actief' : 'Inactief'}</Badge>
              <Badge tone={entity.environment === 'Live' ? 'success' : 'info'}>
                {entity.environment === 'Live' ? 'Live' : 'Sandbox'}
              </Badge>
            </li>
          ))}
          {overview.legalEntities.length === 0 && <li>Nog geen eigen bedrijven geconfigureerd.</li>}
        </ul>
      </section>
    </div>
  )
}
