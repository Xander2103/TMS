import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import { adjustmentSummary, formatEuro } from '../../tarification/adjustmentFormat'
import {
  cancelPriceAdjustment,
  createPriceAdjustment,
  listPriceAdjustments,
  listPriceRules,
  previewPriceAdjustment,
  type PriceAdjustmentRulePreview,
  type PriceRule,
  type ScheduledPriceAdjustment,
  ADJUSTMENT_STATUS_LABELS,
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

/**
 * Scheduled future price changes (spec §12/14): plan +/-% now, preview every affected value,
 * confirm to create future effective versions. Current prices stay untouched until the date;
 * a scheduled change can be cancelled while it has not activated yet.
 */
export function CustomerPriceAdjustmentsPanel({ customerId }: CustomerPriceAdjustmentsPanelProps) {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
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
      setWizardError(describeApiError(err, t('customers.priceAdjustments.previewFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function confirm() {
    if (!wizard) return
    setBusy(true)
    try {
      await createPriceAdjustment(customerId, { ...wizardInput(wizard), reason: wizard.reason.trim() || null })
      showSuccess(t('customers.priceAdjustments.scheduled'))
      setWizard(null)
      reload()
    } catch (err) {
      setWizardError(describeApiError(err, t('customers.priceAdjustments.scheduleFailed')).message)
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
      showSuccess(t('customers.priceAdjustments.cancelled'))
      reload()
    } catch (err) {
      showError(describeApiError(err, t('customers.priceAdjustments.cancelFailed')).message)
    }
  }

  const statusTone = (status: ScheduledPriceAdjustment['statusCode']) =>
    status === 'Planned' ? 'info' : status === 'Active' ? 'success' : 'neutral'

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('customers.priceAdjustments.title')}</h3>
        {canManage && <Button onClick={openWizard}>{t('customers.priceAdjustments.newAdjustment')}</Button>}
      </div>

      {adjustments === null && <p className="placeholder-text">{t('customers.priceAdjustments.loading')}</p>}
      {adjustments !== null && adjustments.length === 0 && (
        <p className="placeholder-text">{t('customers.priceAdjustments.empty')}</p>
      )}
      {adjustments !== null && adjustments.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('customers.priceAdjustments.columnEffectiveDate')}</th>
              <th>{t('customers.priceAdjustments.columnAdjustment')}</th>
              <th>{t('customers.priceAdjustments.columnRules')}</th>
              <th>{t('customers.priceAdjustments.columnReason')}</th>
              <th>{t('customers.priceAdjustments.columnStatus')}</th>
              {canManage && <th aria-label={t('customers.pricing.actionsAria')} />}
            </tr>
          </thead>
          <tbody>
            {adjustments.map((adjustment) => (
              <tr key={adjustment.id}>
                <td>{adjustment.effectiveDate}</td>
                <td>{adjustmentSummary(adjustment)}</td>
                <td>{adjustment.ruleCount}</td>
                <td>{adjustment.reason ?? '—'}</td>
                <td>
                  {/* statusCode is de stabiele bron; `status` blijft enkel legacy weergaveveld. */}
                  <Badge tone={statusTone(adjustment.statusCode)}>
                    {t(ADJUSTMENT_STATUS_LABELS[adjustment.statusCode])}
                  </Badge>
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    {adjustment.statusCode === 'Planned' && (
                      <button
                        type="button"
                        className="issued-items-link issued-items-link-danger"
                        onClick={() => setCancelTarget(adjustment)}
                      >
                        {t('ui.actions.cancel')}
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
          title={t('customers.priceAdjustments.wizardTitle')}
          onClose={() => setWizard(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizard(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              {wizard.preview === null && (
                <Button onClick={() => void loadPreview()} disabled={busy || !wizard.effectiveDate || !wizard.percent}>
                  {t('customers.priceAdjustments.previewAction')}
                </Button>
              )}
              {wizard.preview !== null && (
                <Button onClick={() => void confirm()} disabled={busy || wizard.preview.length === 0}>
                  {t('ui.actions.confirm')}
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
              <FormField label={t('customers.priceAdjustments.columnEffectiveDate')} htmlFor="adj-date" required hint={t('customers.priceAdjustments.effectiveDateHint')}>
                <input
                  id="adj-date"
                  type="date"
                  value={wizard.effectiveDate}
                  onChange={(e) => setWizard((w) => (w ? { ...w, effectiveDate: e.target.value, preview: null } : w))}
                />
              </FormField>
              <FormField label={t('customers.priceAdjustments.percentField')} htmlFor="adj-percent" required hint={t('customers.priceAdjustments.percentHint')}>
                <input
                  id="adj-percent"
                  type="number"
                  step="0.01"
                  value={wizard.percent}
                  onChange={(e) => setWizard((w) => (w ? { ...w, percent: e.target.value, preview: null } : w))}
                />
              </FormField>
            </div>
            <FormField label={t('customers.priceAdjustments.scopeField')} htmlFor="adj-scope">
              <select
                id="adj-scope"
                value={wizard.scope}
                onChange={(e) => setWizard((w) => (w ? { ...w, scope: e.target.value as 'all' | 'selection', preview: null } : w))}
              >
                <option value="all">{t('customers.priceAdjustments.scopeAll')}</option>
                <option value="selection">{t('customers.priceAdjustments.scopeSelection')}</option>
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
                {rules.length === 0 && <p className="placeholder-text">{t('customers.priceAdjustments.noRules')}</p>}
              </div>
            )}
            <FormField label={t('customers.priceAdjustments.reasonField')} htmlFor="adj-reason">
              <input
                id="adj-reason"
                value={wizard.reason}
                onChange={(e) => setWizard((w) => (w ? { ...w, reason: e.target.value } : w))}
                maxLength={1000}
              />
            </FormField>

            {wizard.preview !== null && (
              <>
                <h4>{t('customers.priceAdjustments.previewAction')}</h4>
                {wizard.preview.length === 0 && (
                  <p className="placeholder-text">{t('customers.priceAdjustments.previewEmpty')}</p>
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
                              {formatEuro(change.oldValue)} → {formatEuro(change.newValue)}
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
          title={t('customers.priceAdjustments.cancelTitle')}
          message={t('customers.priceAdjustments.cancelMessage', {
            summary: adjustmentSummary(cancelTarget),
            date: cancelTarget.effectiveDate,
          })}
          confirmLabel={t('customers.priceAdjustments.cancelConfirmLabel')}
          destructive
          onConfirm={handleCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}
    </section>
  )
}
