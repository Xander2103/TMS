import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { getPeppolOverview, type PeppolChecklistItem, type PeppolOverview } from '../api/peppolApi'

/** Vertaalsleutels van de ontbrekende velden — renderen als t(key) en samenvoegen. */
function missingFieldKeys(entity: PeppolChecklistItem): string[] {
  const missing: string[] = []
  if (!entity.hasPeppolIdentity) missing.push('peppol.fields.peppolId')
  if (!entity.hasVatNumber) missing.push('peppol.fields.vatNumber')
  if (!entity.hasIban) missing.push('peppol.fields.iban')
  return missing
}

/** "Validatieproblemen": onvolledige eigen bedrijven + klanten zonder Peppol-gegevens. */
export function ValidationTab() {
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
        if (mounted) setErrorKey('peppol.validation.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (errorKey) return <p className="placeholder-text">{t(errorKey)}</p>
  if (overview === null) return <p className="placeholder-text">{t('peppol.validation.loading')}</p>

  const incomplete = overview.legalEntities.filter((entity) => !entity.isComplete)
  const hasCustomerIssues = overview.customersEnabledWithoutPeppolId > 0 || overview.activeCustomersMissingPeppolData > 0

  return (
    <div>
      <p className="peppol-explain">{t('peppol.validation.intro')}</p>

      <section className="edi-section">
        <h3>{t('peppol.validation.entitiesTitle')}</h3>
        {incomplete.length === 0 ? (
          <p className="placeholder-text">{t('peppol.validation.entitiesComplete')}</p>
        ) : (
          <ul className="peppol-issue-list">
            {incomplete.map((entity) => (
              <li key={entity.legalEntityId}>
                <strong>{entity.legalEntityName}</strong> —{' '}
                {t('peppol.validation.missing', {
                  fields: missingFieldKeys(entity)
                    .map((key) => t(key))
                    .join(', '),
                })}
              </li>
            ))}
          </ul>
        )}
        <p className="peppol-explain">
          {t('peppol.validation.entitiesHintPrefix')}{' '}
          <Link to="/settings/legal-entities">{t('peppol.validation.entitiesHintLink')}</Link>.
        </p>
      </section>

      <section className="edi-section">
        <h3>{t('peppol.validation.customersTitle')}</h3>
        {!hasCustomerIssues ? (
          <p className="placeholder-text">{t('peppol.validation.customersOk')}</p>
        ) : (
          <ul className="peppol-issue-list">
            {overview.customersEnabledWithoutPeppolId > 0 && (
              <li>
                <strong>{overview.customersEnabledWithoutPeppolId}</strong>{' '}
                {t('peppol.validation.enabledWithoutId', { count: overview.customersEnabledWithoutPeppolId })}
              </li>
            )}
            {overview.activeCustomersMissingPeppolData > 0 && (
              <li>
                <strong>{overview.activeCustomersMissingPeppolData}</strong>{' '}
                {t('peppol.validation.missingData', { count: overview.activeCustomersMissingPeppolData })}
              </li>
            )}
          </ul>
        )}
        <p className="peppol-explain">
          {t('peppol.validation.customersHintPrefix')}{' '}
          <Link to="/customers">{t('peppol.validation.customersHintLink')}</Link>.
        </p>
      </section>
    </div>
  )
}
