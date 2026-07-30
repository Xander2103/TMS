import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getPeppolOverview, type PeppolChecklistItem, type PeppolOverview } from '../api/peppolApi'

function missingFields(entity: PeppolChecklistItem): string[] {
  const missing: string[] = []
  if (!entity.hasPeppolIdentity) missing.push('Peppol-ID')
  if (!entity.hasVatNumber) missing.push('BTW-nummer')
  if (!entity.hasIban) missing.push('IBAN')
  return missing
}

/** "Validatieproblemen": onvolledige eigen bedrijven + klanten zonder Peppol-gegevens. */
export function ValidationTab() {
  const [overview, setOverview] = useState<PeppolOverview | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getPeppolOverview()
      .then((data) => {
        if (mounted) setOverview(data)
      })
      .catch(() => {
        if (mounted) setError('De validatieproblemen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (error) return <p className="placeholder-text">{error}</p>
  if (overview === null) return <p className="placeholder-text">Validatieproblemen laden…</p>

  const incomplete = overview.legalEntities.filter((entity) => !entity.isComplete)
  const hasCustomerIssues = overview.customersEnabledWithoutPeppolId > 0 || overview.activeCustomersMissingPeppolData > 0

  return (
    <div>
      <p className="peppol-explain">
        Voordat een factuur via Peppol verzonden kan worden, moeten zowel het versturende eigen bedrijf als de
        ontvangende klant volledige Peppol-gegevens hebben. Hieronder staan de openstaande problemen.
      </p>

      <section className="edi-section">
        <h3>Onvolledige eigen bedrijven</h3>
        {incomplete.length === 0 ? (
          <p className="placeholder-text">Alle eigen bedrijven zijn volledig geconfigureerd.</p>
        ) : (
          <ul className="peppol-issue-list">
            {incomplete.map((entity) => (
              <li key={entity.legalEntityId}>
                <strong>{entity.legalEntityName}</strong> — ontbreekt: {missingFields(entity).join(', ')}
              </li>
            ))}
          </ul>
        )}
        <p className="peppol-explain">
          Vul de ontbrekende gegevens aan via <Link to="/settings/legal-entities">Instellingen → Eigen bedrijven</Link>.
        </p>
      </section>

      <section className="edi-section">
        <h3>Klanten</h3>
        {!hasCustomerIssues ? (
          <p className="placeholder-text">Geen klantproblemen gevonden.</p>
        ) : (
          <ul className="peppol-issue-list">
            {overview.customersEnabledWithoutPeppolId > 0 && (
              <li>
                <strong>{overview.customersEnabledWithoutPeppolId}</strong>{' '}
                {overview.customersEnabledWithoutPeppolId === 1 ? 'klant' : 'klanten'} met Peppol aan maar zonder
                Peppol-ID — verzenden mislukt voor deze klanten tot het Peppol-ID is ingevuld.
              </li>
            )}
            {overview.activeCustomersMissingPeppolData > 0 && (
              <li>
                <strong>{overview.activeCustomersMissingPeppolData}</strong> actieve{' '}
                {overview.activeCustomersMissingPeppolData === 1 ? 'klant' : 'klanten'} zonder Peppol-gegevens
                (informatief — Peppol is voor deze klanten mogelijk niet gewenst).
              </li>
            )}
          </ul>
        )}
        <p className="peppol-explain">
          Peppol-gegevens van een klant beheer je op de klantfiche via <Link to="/customers">Klanten</Link>.
        </p>
      </section>
    </div>
  )
}
