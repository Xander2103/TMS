import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { agreementToInput } from '../agreementInputHelpers'
import {
  listPricingAgreements,
  listPricingZones,
  updatePricingAgreement,
  type PricingAgreement,
  type PricingAgreementModifierInput,
  type PricingZone,
} from '../api/pricingApi'

interface AgreementDerivationPanelProps {
  agreement: PricingAgreement
  canManage: boolean
  onUpdated: (updated: PricingAgreement) => void
}

interface ModifierDraft {
  name: string
  countryCode: string
  zoneId: string
  mode: 'Percent' | 'Fixed'
  value: string
}

/** Swaps the item at `from` with its neighbour at `to`; out-of-range is a no-op (edge buttons). */
function moveItem<T>(list: T[], from: number, to: number): T[] {
  if (to < 0 || to >= list.length) return list
  const next = [...list]
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item)
  return next
}

const toModifierDrafts = (agreement: PricingAgreement): ModifierDraft[] =>
  agreement.modifiers.map((m) => ({
    name: m.name,
    countryCode: m.countryCode ?? '',
    zoneId: m.zoneId ?? '',
    mode: m.fixedAmount !== null ? 'Fixed' : 'Percent',
    value: String(m.fixedAmount !== null ? m.fixedAmount : (m.percent ?? '')),
  }))

/**
 * "Afleiding" tab: makes this table a DERIVED table (spec §9, "NL = BE +30%") — it reuses another
 * (shared/general) table's rules and stacks its own country/zone-conditioned modifiers on top.
 * Read-only summary when the caller cannot manage tariffs.
 */
export function AgreementDerivationPanel({ agreement, canManage, onUpdated }: AgreementDerivationPanelProps) {
  const { showSuccess } = useToast()
  const [baseAgreementId, setBaseAgreementId] = useState(agreement.baseAgreementId ?? '')
  const [modifiers, setModifiers] = useState<ModifierDraft[]>(() => toModifierDrafts(agreement))
  // Re-derive the draft from a fresh `agreement` prop without an effect (see AgreementSurchargesPanel
  // for the same pattern): comparing during render and adjusting state is the React-recommended way
  // to reset local state when a prop changes, without the extra render an effect would cause.
  const [syncedAgreement, setSyncedAgreement] = useState(agreement)
  if (agreement !== syncedAgreement) {
    setSyncedAgreement(agreement)
    setBaseAgreementId(agreement.baseAgreementId ?? '')
    setModifiers(toModifierDrafts(agreement))
  }
  const [baseOptions, setBaseOptions] = useState<PricingAgreement[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!canManage) return
    // Only company-wide/shared tables (CustomerId null) are valid base tables.
    listPricingAgreements().then(setBaseOptions).catch(() => setBaseOptions([]))
    listPricingZones().then(setZones).catch(() => setZones([]))
  }, [canManage])

  async function save() {
    setBusy(true)
    setError(null)
    try {
      const updated = await updatePricingAgreement(agreement.id, {
        ...agreementToInput(agreement),
        baseAgreementId: baseAgreementId || null,
        modifiers: baseAgreementId
          ? modifiers
              .filter((m) => m.name.trim() !== '')
              .map((m, index): PricingAgreementModifierInput => ({
                sequence: index + 1,
                name: m.name.trim(),
                countryCode: m.countryCode.trim() || null,
                zoneId: m.zoneId || null,
                percent: m.mode === 'Percent' ? Number(m.value) || 0 : null,
                fixedAmount: m.mode === 'Fixed' ? Number(m.value) || 0 : null,
              }))
          : null,
      })
      onUpdated(updated)
      showSuccess('Afleiding opgeslagen.')
    } catch (err) {
      setError(describeApiError(err, 'De afleiding kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  if (!canManage) {
    if (!agreement.baseAgreementId) {
      return <EmptyState message="Deze tabel is niet afgeleid van een andere tabel." />
    }
    return (
      <section className="customer-panel">
        <h3>Afleiding</h3>
        <p>
          Deze tabel gebruikt de prijsregels van <strong>{agreement.baseAgreementName ?? '—'}</strong>.
        </p>
        <ul>
          {agreement.modifiers.map((m) => (
            <li key={m.id}>
              {m.name}: {m.percent !== null ? `${m.percent}%` : `€ ${m.fixedAmount?.toFixed(2)}`}
              {m.countryCode ? ` (${m.countryCode})` : ''}
            </li>
          ))}
        </ul>
      </section>
    )
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Afleiding</h3>
      </div>
      {error && (
        <div className="issued-items-form-error" role="alert">
          {error}
        </div>
      )}
      <FormField
        label="Basistabel"
        htmlFor="derivation-base"
        hint="Optioneel: hergebruik de prijsregels van een gedeelde of algemene tabel, met eigen aanpassingen (bv. Nederland +30%)."
      >
        <select id="derivation-base" value={baseAgreementId} onChange={(e) => setBaseAgreementId(e.target.value)}>
          <option value="">— Geen (eigen prijsregels) —</option>
          {baseOptions
            .filter((a) => a.id !== agreement.id)
            .map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
        </select>
      </FormField>
      {baseAgreementId && (
        <>
          {modifiers.map((modifier, index) => (
            <div key={index} className="issued-items-form-row customer-rule-bracket">
              <input
                aria-label={`Aanpassing ${index + 1} naam`}
                placeholder="naam (bv. Nederland +30%)"
                value={modifier.name}
                onChange={(e) => setModifiers((m) => m.map((x, i) => (i === index ? { ...x, name: e.target.value } : x)))}
              />
              <input
                aria-label={`Aanpassing ${index + 1} land`}
                placeholder="land (bv. NL)"
                maxLength={2}
                value={modifier.countryCode}
                onChange={(e) =>
                  setModifiers((m) => m.map((x, i) => (i === index ? { ...x, countryCode: e.target.value.toUpperCase() } : x)))
                }
              />
              <select
                aria-label={`Aanpassing ${index + 1} zone`}
                value={modifier.zoneId}
                onChange={(e) => setModifiers((m) => m.map((x, i) => (i === index ? { ...x, zoneId: e.target.value } : x)))}
              >
                <option value="">— Alle zones —</option>
                {zones.map((zone) => (
                  <option key={zone.id} value={zone.id}>
                    {zone.code} — {zone.name}
                  </option>
                ))}
              </select>
              <select
                aria-label={`Aanpassing ${index + 1} soort`}
                value={modifier.mode}
                onChange={(e) =>
                  setModifiers((m) => m.map((x, i) => (i === index ? { ...x, mode: e.target.value as 'Percent' | 'Fixed' } : x)))
                }
              >
                <option value="Percent">Percentage</option>
                <option value="Fixed">Vast bedrag</option>
              </select>
              <input
                aria-label={`Aanpassing ${index + 1} waarde`}
                type="number"
                step="0.01"
                placeholder="waarde"
                value={modifier.value}
                onChange={(e) => setModifiers((m) => m.map((x, i) => (i === index ? { ...x, value: e.target.value } : x)))}
              />
              <Button
                variant="ghost"
                aria-label={`Aanpassing ${index + 1} omhoog`}
                disabled={index === 0}
                onClick={() => setModifiers((m) => moveItem(m, index, index - 1))}
              >
                ↑
              </Button>
              <Button
                variant="ghost"
                aria-label={`Aanpassing ${index + 1} omlaag`}
                disabled={index === modifiers.length - 1}
                onClick={() => setModifiers((m) => moveItem(m, index, index + 1))}
              >
                ↓
              </Button>
              <Button variant="ghost" onClick={() => setModifiers((m) => m.filter((_, i) => i !== index))}>
                Verwijderen
              </Button>
            </div>
          ))}
          <Button
            variant="secondary"
            onClick={() => setModifiers((m) => [...m, { name: '', countryCode: '', zoneId: '', mode: 'Percent', value: '' }])}
          >
            + Aanpassing
          </Button>
        </>
      )}
      <div className="customer-panel-header">
        <Button onClick={() => void save()} disabled={busy}>
          {busy ? 'Bezig...' : 'Opslaan'}
        </Button>
      </div>
    </section>
  )
}
