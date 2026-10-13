import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { validateAgreementConfiguration, type PricingConfigCheck } from '../api/pricingApi'
import './pricingTableDetail.css'

interface AgreementValidationPanelProps {
  agreementId: string
}

/**
 * "Controle": the configuration-health checks for this rate table (overlapping rules, staffel
 * gaps, derivation-chain health, orphaned/mismatched assignments, inactive unit/zone references,
 * drifted min/max data, ...) — runs on load and on demand via the "Controleer configuratie" button.
 * Never blocks the rest of the page: a failed check load just shows an inline error, not a crash.
 */
export function AgreementValidationPanel({ agreementId }: AgreementValidationPanelProps) {
  const { t } = useLocale()
  const [checks, setChecks] = useState<PricingConfigCheck[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let cancelled = false
    validateAgreementConfiguration(agreementId)
      .then((result) => {
        if (cancelled) return
        setChecks(result)
        setLoadError(null)
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(localizeApiError(t, err, t('tarification.validation.loadError')))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // `t` bewust buiten de deps: een taalwissel hoeft geen nieuwe API-call uit te lokken.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [agreementId, reloadToken])

  // Loading is flipped here (not inside the effect) so the effect only synchronises with the API.
  function reload() {
    setLoading(true)
    setReloadToken((token) => token + 1)
  }

  return (
    <div className="pricing-table-validation">
      <div className="pricing-table-validation-header">
        <h3>{t('tarification.validation.title')}</h3>
        <Button variant="secondary" onClick={reload} disabled={loading}>
          {loading ? t('tarification.common.busyEllipsis') : t('tarification.validation.check')}
        </Button>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}

      {!loadError && checks !== null && checks.length === 0 && (
        <div className="pricing-table-validation-ok" role="status">
          {t('tarification.validation.ok')}
        </div>
      )}

      {!loadError && checks !== null && checks.length > 0 && (
        <ul className="pricing-table-validation-list">
          {checks.map((check, index) => (
            <li
              key={`${check.severity}-${index}`}
              className={`pricing-table-validation-item pricing-table-validation-${check.severity}`}
            >
              {check.message}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
