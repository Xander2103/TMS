import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  cancelPriceAdjustment,
  createPriceAdjustment,
  listPriceAdjustments,
  listPriceRules,
  previewPriceAdjustment,
  type PriceAdjustmentRulePreview,
  type PriceRule,
  type ScheduledPriceAdjustment,
} from '../../tarification/api/pricingApi'

interface CustomerPriceAdjustmentsPanelProps {
  customerId: string
}

interface WizardState {
  effectiveDate: string
  percent: string
  scope: 'all' | 'selection'
  selectedRuleIds: Set<string>
  reason: string
  preview: PriceAdjustmentRulePreview[] | null
}

const euro = (value: number) => `€ ${value.toFixed(2)}`

/**
 * Scheduled future price changes (spec §12/14): plan +/-% now, preview every affected value,
 * confirm to create future effective versions. Current prices stay untouched until the date;
 * a scheduled change can be cancelled while it has not activated yet.
 */
export function CustomerPriceAdjustmentsPanel({ customerId }: CustomerPriceAdjustmentsPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [adjustments, setAdjustments] = useState<ScheduledPriceAdjustment[] | null>(null)
  const [rules, setRules] = useState<PriceRule[]>([])
  const [wizard, setWizard] = useState<WizardState | null>(null)
  const [wizardError, setWizardError] = useState<string | null>(null)
  const [cancelTarget, setCancelTarget] = useState<ScheduledPriceAdjustment | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([listPriceAdjustments(customerId), listPriceRules(customerId).catch(() => [] as PriceRule[])])
      .then(([adjustmentData, ruleData]) => {
        setAdjustments(adjustmentData)
        setRules(ruleData)
      })
      .catch(() => setAdjustments([]))
  }, [customerId, canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return null

  function openWizard() {
    setWizardError(null)
    setWizard({
      effectiveDate: '',
      percent: '',
      scope: 'all',
      selectedRuleIds: new Set(),
      reason: '',
      preview: null,
    })
  }

  function wizardInput(state: WizardState) {
    return {
      effectiveDate: state.effectiveDate,
      percent: Number(state.percent),
      ruleIds: state.scope === 'all' ? null : [...state.selectedRuleIds],
    }
  }

  async function loadPreview() {
    if (!wizard) return
    setWizardError(null)
    setBusy(true)
    try {
      const preview = await previewPriceAdjustment(customerId, wizardInput(wizard))
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
      await createPriceAdjustment(customerId, { ...wizardInput(wizard), reason: wizard.reason.trim() || null })
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
      await cancelPriceAdjustment(customerId, target.id)
      showSuccess('Prijsaanpassing geannuleerd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De prijsaanpassing kon niet worden geannuleerd.').message)
    }
  }

  const statusTone = (status: ScheduledPriceAdjustment['status']) =>
    status === 'Gepland' ? 'info' : status === 'Actief' ? 'success' : 'neutral'

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Geplande prijswijzigingen</h3>
        {canManage && <Button onClick={openWizard}>+ Nieuwe prijsaanpassing</Button>}
      </div>

      {adjustments === null && <p className="placeholder-text">Prijswijzigingen laden…</p>}
      {adjustments !== null && adjustments.length === 0 && (
        <p className="placeholder-text">Geen geplande prijswijzigingen voor deze klant.</p>
      )}
      {adjustments !== null && adjustments.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Ingangsdatum</th>
              <th>Aanpassing</th>
              <th>Regels</th>
              <th>Reden</th>
              <th>Status</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {adjustments.map((adjustment) => (
              <tr key={adjustment.id}>
                <td>{adjustment.effectiveDate}</td>
                <td>
                  {adjustment.percent > 0 ? '+' : ''}
                  {adjustment.percent}%
                </td>
                <td>{adjustment.ruleCount}</td>
                <td>{adjustment.reason ?? '—'}</td>
                <td>
                  <Badge tone={statusTone(adjustment.status)}>{adjustment.status}</Badge>
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    {adjustment.status === 'Gepland' && (
                      <button
                        type="button"
                        className="issued-items-link issued-items-link-danger"
                        onClick={() => setCancelTarget(adjustment)}
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
                <Button onClick={() => void loadPreview()} disabled={busy || !wizard.effectiveDate || !wizard.percent}>
                  Preview
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
              <FormField label="Ingangsdatum" htmlFor="adj-date" required hint="Moet in de toekomst liggen.">
                <input
                  id="adj-date"
                  type="date"
                  value={wizard.effectiveDate}
                  onChange={(e) => setWizard((w) => (w ? { ...w, effectiveDate: e.target.value, preview: null } : w))}
                />
              </FormField>
              <FormField label="Aanpassing (%)" htmlFor="adj-percent" required hint="Bv. 4 of -2,5.">
                <input
                  id="adj-percent"
                  type="number"
                  step="0.01"
                  value={wizard.percent}
                  onChange={(e) => setWizard((w) => (w ? { ...w, percent: e.target.value, preview: null } : w))}
                />
              </FormField>
            </div>
            <FormField label="Toepassen op" htmlFor="adj-scope">
              <select
                id="adj-scope"
                value={wizard.scope}
                onChange={(e) => setWizard((w) => (w ? { ...w, scope: e.target.value as 'all' | 'selection', preview: null } : w))}
              >
                <option value="all">Alle actieve klanttarieven</option>
                <option value="selection">Geselecteerde tariefregels</option>
              </select>
            </FormField>
            {wizard.scope === 'selection' && (
              <div className="customer-preferred-units">
                {rules.map((rule) => (
                  <label key={rule.id} className="tof-checkbox">
                    <input
                      type="checkbox"
                      checked={wizard.selectedRuleIds.has(rule.id)}
                      onChange={(e) =>
                        setWizard((w) => {
                          if (!w) return w
                          const next = new Set(w.selectedRuleIds)
                          if (e.target.checked) next.add(rule.id)
                          else next.delete(rule.id)
                          return { ...w, selectedRuleIds: next, preview: null }
                        })
                      }
                    />
                    {rule.name}
                  </label>
                ))}
                {rules.length === 0 && <p className="placeholder-text">Geen tariefregels voor deze klant.</p>}
              </div>
            )}
            <FormField label="Reden (intern)" htmlFor="adj-reason">
              <input
                id="adj-reason"
                value={wizard.reason}
                onChange={(e) => setWizard((w) => (w ? { ...w, reason: e.target.value } : w))}
                maxLength={1000}
              />
            </FormField>

            {wizard.preview !== null && (
              <>
                <h4>Preview</h4>
                {wizard.preview.length === 0 && (
                  <p className="placeholder-text">Geen aanpasbare tariefregels binnen deze selectie.</p>
                )}
                {wizard.preview.map((rule) => (
                  <div key={rule.priceRuleId}>
                    <strong>{rule.ruleName}</strong>
                    <table className="issued-items-table">
                      <tbody>
                        {rule.changes.map((change) => (
                          <tr key={change.field}>
                            <td>{change.field}</td>
                            <td>
                              {euro(change.oldValue)} → {euro(change.newValue)}
                            </td>
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
          message={`De geplande aanpassing van ${cancelTarget.percent > 0 ? '+' : ''}${cancelTarget.percent}% per ${cancelTarget.effectiveDate} wordt geannuleerd; de huidige tarieven blijven gelden.`}
          confirmLabel="Annuleren bevestigen"
          destructive
          onConfirm={handleCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}
    </section>
  )
}
