import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { describeApiError } from '../../../api/problemDetails'
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
        if (!cancelled) setLoadError(describeApiError(err, 'De configuratie kon niet worden gecontroleerd.').message)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [agreementId, reloadToken])

  // Loading is flipped here (not inside the effect) so the effect only synchronises with the API.
  function reload() {
    setLoading(true)
    setReloadToken((token) => token + 1)
  }

  return (
    <div className="pricing-table-validation">
      <div className="pricing-table-validation-header">
        <h3>Controle</h3>
        <Button variant="secondary" onClick={reload} disabled={loading}>
          {loading ? 'Bezig…' : 'Controleer configuratie'}
        </Button>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}

      {!loadError && checks !== null && checks.length === 0 && (
        <div className="pricing-table-validation-ok" role="status">
          Geen problemen gevonden.
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
