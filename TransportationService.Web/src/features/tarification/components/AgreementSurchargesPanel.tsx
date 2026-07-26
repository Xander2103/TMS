import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { EmptyState } from '../../../components/ui/EmptyState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { agreementToInput } from '../agreementInputHelpers'
import { updatePricingAgreement, type PricingAgreement } from '../api/pricingApi'
import type { SurchargeKind } from '../types'

interface AgreementSurchargesPanelProps {
  agreement: PricingAgreement
  canManage: boolean
  onUpdated: (updated: PricingAgreement) => void
}

interface SurchargeDraft {
  name: string
  kind: SurchargeKind
  value: string
}

const toDrafts = (agreement: PricingAgreement): SurchargeDraft[] =>
  agreement.surcharges.map((s) => ({ name: s.name, kind: s.kind, value: String(s.value) }))

/** "Toeslagen" tab: automatic surcharges (percent or fixed) applied on the agreement subtotal. */
export function AgreementSurchargesPanel({ agreement, canManage, onUpdated }: AgreementSurchargesPanelProps) {
  const { showSuccess } = useToast()
  const [surcharges, setSurcharges] = useState<SurchargeDraft[]>(() => toDrafts(agreement))
  // Re-derive the draft from a fresh `agreement` prop (e.g. after a save elsewhere) without an
  // effect: comparing during render and adjusting state is the recommended React pattern for
  // "state that resets when a prop changes" (avoids the extra render an effect would cause).
  const [syncedAgreement, setSyncedAgreement] = useState(agreement)
  if (agreement !== syncedAgreement) {
    setSyncedAgreement(agreement)
    setSurcharges(toDrafts(agreement))
  }
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save() {
    setBusy(true)
    setError(null)
    try {
      const updated = await updatePricingAgreement(agreement.id, {
        ...agreementToInput(agreement),
        surcharges: surcharges
          .filter((s) => s.name.trim() !== '')
          .map((s) => ({ name: s.name.trim(), kind: s.kind, value: Number(s.value) || 0 })),
      })
      onUpdated(updated)
      showSuccess('Toeslagen opgeslagen.')
    } catch (err) {
      setError(describeApiError(err, 'De toeslagen konden niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Automatische toeslagen</h3>
      </div>
      {error && (
        <div className="issued-items-form-error" role="alert">
          {error}
        </div>
      )}
      {surcharges.length === 0 && <EmptyState message="Nog geen automatische toeslagen op deze tabel." />}
      {surcharges.map((surcharge, index) => (
        <div key={index} className="issued-items-form-row customer-rule-bracket">
          <input
            aria-label={`Toeslag ${index + 1} naam`}
            placeholder="naam"
            value={surcharge.name}
            disabled={!canManage}
            onChange={(e) => setSurcharges((s) => s.map((x, i) => (i === index ? { ...x, name: e.target.value } : x)))}
          />
          <select
            aria-label={`Toeslag ${index + 1} soort`}
            value={surcharge.kind}
            disabled={!canManage}
            onChange={(e) => setSurcharges((s) => s.map((x, i) => (i === index ? { ...x, kind: e.target.value as SurchargeKind } : x)))}
          >
            <option value="Percent">Percentage</option>
            <option value="Fixed">Vast bedrag</option>
          </select>
          <input
            aria-label={`Toeslag ${index + 1} waarde`}
            type="number"
            step="0.01"
            value={surcharge.value}
            disabled={!canManage}
            onChange={(e) => setSurcharges((s) => s.map((x, i) => (i === index ? { ...x, value: e.target.value } : x)))}
          />
          {canManage && (
            <Button variant="ghost" onClick={() => setSurcharges((s) => s.filter((_, i) => i !== index))}>
              Verwijderen
            </Button>
          )}
        </div>
      ))}
      {canManage && (
        <>
          <Button
            variant="secondary"
            onClick={() => setSurcharges((s) => [...s, { name: '', kind: 'Percent', value: '' }])}
          >
            + Toeslag
          </Button>
          <div className="customer-panel-header">
            <Button onClick={() => void save()} disabled={busy}>
              {busy ? 'Bezig...' : 'Opslaan'}
            </Button>
          </div>
        </>
      )}
    </section>
  )
}
