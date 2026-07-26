import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { adjustmentSummary, formatEuro } from '../adjustmentFormat'
import {
  cancelAgreementAdjustment,
  createAgreementAdjustment,
  listAgreementAdjustments,
  listUnitTypeSettings,
  previewAgreementAdjustment,
  PRICE_RULE_BASIS_LABELS,
  type PriceAdjustmentInput,
  type PriceAdjustmentRulePreview,
  type PriceRuleBasis,
  type ScheduledPriceAdjustment,
  type UnitTypeSettings,
} from '../api/pricingApi'

interface AgreementAdjustmentsPanelProps {
  agreementId: string
  canManage: boolean
}

type RoundingChoice = '' | '0.01' | '0.05' | '0.10'

interface WizardState {
  effectiveDate: string
  type: 'percent' | 'amount'
  value: string
  roundingStep: RoundingChoice
  bases: Set<PriceRuleBasis>
  unitTypeId: string
  reason: string
  preview: PriceAdjustmentRulePreview[] | null
}

const statusTone = (status: ScheduledPriceAdjustment['status']) =>
  status === 'Gepland' ? 'info' : status === 'Actief' ? 'success' : 'neutral'

/**
 * "Prijsaanpassing" tab (bulk adjustment v2): plan a percentage OR fixed-amount change, optionally
 * narrowed to a basis/unit filter and rounded to a step, preview every affected value, then confirm
 * to materialize future effective versions of this agreement's rules. Mirrors the customer-scoped
 * flow (see CustomerPriceAdjustmentsPanel) with the v2 fields.
 */
export function AgreementAdjustmentsPanel({ agreementId, canManage }: AgreementAdjustmentsPanelProps) {
  const { showSuccess, showError } = useToast()
  const [adjustments, setAdjustments] = useState<ScheduledPriceAdjustment[] | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [wizard, setWizard] = useState<WizardState | null>(null)
  const [wizardError, setWizardError] = useState<string | null>(null)
  const [cancelTarget, setCancelTarget] = useState<ScheduledPriceAdjustment | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listAgreementAdjustments(agreementId)
      .then(setAdjustments)
      .catch(() => setAdjustments([]))
  }, [agreementId])

  useEffect(() => {
    reload()
  }, [reload])

  useEffect(() => {
    listUnitTypeSettings().then(setUnits).catch(() => setUnits([]))
  }, [])

  function openWizard() {
    setWizardError(null)
    setWizard({
      effectiveDate: '',
      type: 'percent',
      value: '',
      roundingStep: '',
      bases: new Set(),
      unitTypeId: '',
      reason: '',
      preview: null,
    })
  }

  function buildInput(state: WizardState): Omit<PriceAdjustmentInput, 'reason'> {
    return {
      effectiveDate: state.effectiveDate,
      percent: state.type === 'percent' ? Number(state.value) : null,
      amountDelta: state.type === 'amount' ? Number(state.value) : null,
      roundingStep: state.roundingStep === '' ? null : Number(state.roundingStep),
      ruleIds: null,
      basisFilter: state.bases.size > 0 ? [...state.bases].join(',') : null,
      unitTypeIdFilter: state.unitTypeId || null,
    }
  }

  async function loadPreview() {
    if (!wizard) return
    setWizardError(null)
    setBusy(true)
    try {
      const preview = await previewAgreementAdjustment(agreementId, buildInput(wizard))
      setWizard((w) => (w ? { ...w, preview } : w))
    } catch (err) {
      setWizardError(describeApiError(err, 'De preview kon niet worden berekend.').message)
    } finally {
      setBusy(false)
    }
  }

  async function confirm() {
    if (!wizard) return
    setBusy(true)
    try {
      await createAgreementAdjustment(agreementId, { ...buildInput(wizard), reason: wizard.reason.trim() || null })
      showSuccess('Prijsaanpassing ingepland.')
      setWizard(null)
      reload()
    } catch (err) {
      setWizardError(describeApiError(err, 'De prijsaanpassing kon niet worden ingepland.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleCancel() {
    if (!cancelTarget) return
    const target = cancelTarget
    setCancelTarget(null)
    try {
      await cancelAgreementAdjustment(agreementId, target.id)
      showSuccess('Prijsaanpassing geannuleerd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De prijsaanpassing kon niet worden geannuleerd.').message)
    }
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Geplande prijsaanpassingen</h3>
        {canManage && <Button onClick={openWizard}>+ Nieuwe prijsaanpassing</Button>}
      </div>

      {adjustments === null && <p className="placeholder-text">Prijsaanpassingen laden…</p>}
      {adjustments !== null && adjustments.length === 0 && (
        <EmptyState message="Geen geplande prijsaanpassingen voor deze tabel." />
      )}
      {adjustments !== null && adjustments.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Ingangsdatum</th>
              <th>Aanpassing</th>
              <th>Afronding</th>
              <th>Filter</th>
              <th>Regels</th>
              <th>Reden</th>
              <th>Status</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {adjustments.map((a) => (
              <tr key={a.id}>
                <td>{a.effectiveDate}</td>
                <td>{adjustmentSummary(a)}</td>
                <td>{a.roundingStep !== null ? formatEuro(a.roundingStep) : '—'}</td>
                <td>
                  {a.basisFilter ?? 'Alle bases'}
                  {a.unitTypeIdFilter ? ' · 1 eenheid' : ''}
                </td>
                <td>{a.ruleCount}</td>
                <td>{a.reason ?? '—'}</td>
                <td>
                  <Badge tone={statusTone(a.status)}>{a.status}</Badge>
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    {a.status === 'Gepland' && (
                      <button
                        type="button"
                        className="issued-items-link issued-items-link-danger"
                        onClick={() => setCancelTarget(a)}
                      >
                        Annuleren
                      </button>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {wizard && (
        <Modal
          title="Nieuwe prijsaanpassing"
          onClose={() => setWizard(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizard(null)} disabled={busy}>
                Annuleren
              </Button>
              {wizard.preview === null && (
                <Button onClick={() => void loadPreview()} disabled={busy || !wizard.effectiveDate || !wizard.value}>
                  Voorbeeld
                </Button>
              )}
              {wizard.preview !== null && (
                <Button onClick={() => void confirm()} disabled={busy || wizard.preview.length === 0}>
                  Bevestigen
                </Button>
              )}
            </>
          }
        >
          <div className="issued-items-form">
            {wizardError && (
              <div className="issued-items-form-error" role="alert">
                {wizardError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Type" htmlFor="agr-adj-type">
                <select
                  id="agr-adj-type"
                  value={wizard.type}
                  onChange={(e) => setWizard((w) => (w ? { ...w, type: e.target.value as 'percent' | 'amount', preview: null } : w))}
                >
                  <option value="percent">Percentage</option>
                  <option value="amount">Vast bedrag</option>
                </select>
              </FormField>
              <FormField
                label="Waarde"
                htmlFor="agr-adj-value"
                required
                hint={wizard.type === 'percent' ? 'Bv. 4 of -2,5.' : 'Bv. 5 of -2,50 (€).'}
              >
                <input
                  id="agr-adj-value"
                  type="number"
                  step="0.01"
                  value={wizard.value}
                  onChange={(e) => setWizard((w) => (w ? { ...w, value: e.target.value, preview: null } : w))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Ingangsdatum" htmlFor="agr-adj-date" required hint="Moet in de toekomst liggen.">
                <input
                  id="agr-adj-date"
                  type="date"
                  value={wizard.effectiveDate}
                  onChange={(e) => setWizard((w) => (w ? { ...w, effectiveDate: e.target.value, preview: null } : w))}
                />
              </FormField>
              <FormField label="Afronding" htmlFor="agr-adj-rounding">
                <select
                  id="agr-adj-rounding"
                  value={wizard.roundingStep}
                  onChange={(e) => setWizard((w) => (w ? { ...w, roundingStep: e.target.value as RoundingChoice, preview: null } : w))}
                >
                  <option value="">Geen</option>
                  <option value="0.01">€ 0,01</option>
                  <option value="0.05">€ 0,05</option>
                  <option value="0.10">€ 0,10</option>
                </select>
              </FormField>
            </div>
            <FormField label="Eenheid" htmlFor="agr-adj-unit" hint="Optioneel: alleen regels voor deze eenheid.">
              <select
                id="agr-adj-unit"
                value={wizard.unitTypeId}
                onChange={(e) => setWizard((w) => (w ? { ...w, unitTypeId: e.target.value, preview: null } : w))}
              >
                <option value="">— Alle eenheden —</option>
                {units.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.name}
                  </option>
                ))}
              </select>
            </FormField>
            <fieldset className="issued-items-generate-dimension">
              <legend>Basis (optioneel filter)</legend>
              <div className="customer-preferred-units">
                {Object.entries(PRICE_RULE_BASIS_LABELS).map(([value, label]) => (
                  <label key={value} className="tof-checkbox">
                    <input
                      type="checkbox"
                      checked={wizard.bases.has(value as PriceRuleBasis)}
                      onChange={(e) =>
                        setWizard((w) => {
                          if (!w) return w
                          const next = new Set(w.bases)
                          if (e.target.checked) next.add(value as PriceRuleBasis)
                          else next.delete(value as PriceRuleBasis)
                          return { ...w, bases: next, preview: null }
                        })
                      }
                    />
                    {label}
                  </label>
                ))}
              </div>
            </fieldset>
            <FormField label="Reden (intern)" htmlFor="agr-adj-reason">
              <input
                id="agr-adj-reason"
                value={wizard.reason}
                onChange={(e) => setWizard((w) => (w ? { ...w, reason: e.target.value } : w))}
                maxLength={1000}
              />
            </FormField>

            {wizard.preview !== null && (
              <>
                <h4>Voorbeeld</h4>
                {wizard.preview.length === 0 && (
                  <p className="placeholder-text">Geen aanpasbare tariefregels binnen deze selectie.</p>
                )}
                {wizard.preview.map((rule) => (
                  <div key={rule.priceRuleId}>
                    <strong>{rule.ruleName}</strong>
                    <table className="issued-items-table">
                      <thead>
                        <tr>
                          <th>Veld</th>
                          <th>Oud</th>
                          <th>Nieuw</th>
                          <th>Verschil</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rule.changes.map((change) => (
                          <tr key={change.field}>
                            <td>{change.field}</td>
                            <td>{formatEuro(change.oldValue)}</td>
                            <td>{formatEuro(change.newValue)}</td>
                            <td>{formatEuro(change.newValue - change.oldValue)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ))}
              </>
            )}
          </div>
        </Modal>
      )}

      {cancelTarget && (
        <ConfirmDialog
          title="Prijsaanpassing annuleren"
          message={`De geplande aanpassing per ${cancelTarget.effectiveDate} wordt geannuleerd; de huidige tarieven blijven gelden.`}
          confirmLabel="Annuleren bevestigen"
          destructive
          onConfirm={handleCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}
    </section>
  )
}
