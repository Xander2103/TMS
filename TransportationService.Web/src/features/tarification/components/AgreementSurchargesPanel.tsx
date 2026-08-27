import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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

type IncludedTimeMode = 'none' | 'separate' | 'combined'

const toDrafts = (agreement: PricingAgreement): SurchargeDraft[] =>
  agreement.surcharges.map((s) => ({ name: s.name, kind: s.kind, value: String(s.value) }))

const includedTimeMode = (agreement: PricingAgreement): IncludedTimeMode =>
  agreement.includedCombinedMinutes !== null
    ? 'combined'
    : agreement.includedLoadingMinutes !== null || agreement.includedUnloadingMinutes !== null
      ? 'separate'
      : 'none'

/**
 * "Toeslagen" tab: automatic surcharges (percent or fixed) applied on the agreement subtotal,
 * plus the included loading/unloading time agreement (Phase 6) — extra time beyond the allowance
 * is charged at the hourly rate, but only ever as a PROPOSAL on the order until confirmed.
 */
export function AgreementSurchargesPanel({ agreement, canManage, onUpdated }: AgreementSurchargesPanelProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const [surcharges, setSurcharges] = useState<SurchargeDraft[]>(() => toDrafts(agreement))
  const [timeMode, setTimeMode] = useState<IncludedTimeMode>(() => includedTimeMode(agreement))
  const [includedLoadingMinutes, setIncludedLoadingMinutes] = useState(
    agreement.includedLoadingMinutes !== null ? String(agreement.includedLoadingMinutes) : '',
  )
  const [includedUnloadingMinutes, setIncludedUnloadingMinutes] = useState(
    agreement.includedUnloadingMinutes !== null ? String(agreement.includedUnloadingMinutes) : '',
  )
  const [includedCombinedMinutes, setIncludedCombinedMinutes] = useState(
    agreement.includedCombinedMinutes !== null ? String(agreement.includedCombinedMinutes) : '',
  )
  const [extraHourlyRate, setExtraHourlyRate] = useState(
    agreement.extraHourlyRate !== null ? String(agreement.extraHourlyRate) : '',
  )
  // Re-derive the draft from a fresh `agreement` prop (e.g. after a save elsewhere) without an
  // effect: comparing during render and adjusting state is the recommended React pattern for
  // "state that resets when a prop changes" (avoids the extra render an effect would cause).
  const [syncedAgreement, setSyncedAgreement] = useState(agreement)
  if (agreement !== syncedAgreement) {
    setSyncedAgreement(agreement)
    setSurcharges(toDrafts(agreement))
    setTimeMode(includedTimeMode(agreement))
    setIncludedLoadingMinutes(agreement.includedLoadingMinutes !== null ? String(agreement.includedLoadingMinutes) : '')
    setIncludedUnloadingMinutes(agreement.includedUnloadingMinutes !== null ? String(agreement.includedUnloadingMinutes) : '')
    setIncludedCombinedMinutes(agreement.includedCombinedMinutes !== null ? String(agreement.includedCombinedMinutes) : '')
    setExtraHourlyRate(agreement.extraHourlyRate !== null ? String(agreement.extraHourlyRate) : '')
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
        includedLoadingMinutes: timeMode === 'separate' && includedLoadingMinutes.trim() ? Number(includedLoadingMinutes) : null,
        includedUnloadingMinutes: timeMode === 'separate' && includedUnloadingMinutes.trim() ? Number(includedUnloadingMinutes) : null,
        includedCombinedMinutes: timeMode === 'combined' && includedCombinedMinutes.trim() ? Number(includedCombinedMinutes) : null,
        extraHourlyRate: timeMode === 'none' || !extraHourlyRate.trim() ? null : Number(extraHourlyRate),
      })
      onUpdated(updated)
      showSuccess(t('tarification.surcharges.saved'))
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.surcharges.saveError')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('tarification.surcharges.title')}</h3>
      </div>
      {error && (
        <div className="issued-items-form-error" role="alert">
          {error}
        </div>
      )}
      {surcharges.length === 0 && <EmptyState message={t('tarification.surcharges.empty')} />}
      {surcharges.map((surcharge, index) => (
        <div key={index} className="issued-items-form-row customer-rule-bracket">
          <input
            aria-label={t('tarification.surcharges.ariaName', { index: index + 1 })}
            placeholder={t('tarification.surcharges.namePlaceholder')}
            value={surcharge.name}
            disabled={!canManage}
            onChange={(e) => setSurcharges((s) => s.map((x, i) => (i === index ? { ...x, name: e.target.value } : x)))}
          />
          <select
            aria-label={t('tarification.surcharges.ariaKind', { index: index + 1 })}
            value={surcharge.kind}
            disabled={!canManage}
            onChange={(e) => setSurcharges((s) => s.map((x, i) => (i === index ? { ...x, kind: e.target.value as SurchargeKind } : x)))}
          >
            <option value="Percent">{t('tarification.common.percentage')}</option>
            <option value="Fixed">{t('tarification.common.fixedAmount')}</option>
          </select>
          <input
            aria-label={t('tarification.surcharges.ariaValue', { index: index + 1 })}
            type="number"
            step="0.01"
            value={surcharge.value}
            disabled={!canManage}
            onChange={(e) => setSurcharges((s) => s.map((x, i) => (i === index ? { ...x, value: e.target.value } : x)))}
          />
          {canManage && (
            <Button variant="ghost" onClick={() => setSurcharges((s) => s.filter((_, i) => i !== index))}>
              {t('ui.actions.delete')}
            </Button>
          )}
        </div>
      ))}
      {canManage && (
        <Button
          variant="secondary"
          onClick={() => setSurcharges((s) => [...s, { name: '', kind: 'Percent', value: '' }])}
        >
          {t('tarification.surcharges.addSurcharge')}
        </Button>
      )}

      <div className="customer-panel-header">
        <h3>{t('tarification.surcharges.includedTitle')}</h3>
      </div>
      <p className="customer-form-muted">{t('tarification.surcharges.includedHint')}</p>
      <div className="issued-items-form-row">
        <label className="tof-checkbox">
          <input
            type="radio"
            name={`agreement-time-mode-${agreement.id}`}
            checked={timeMode === 'none'}
            disabled={!canManage}
            onChange={() => setTimeMode('none')}
          />
          {t('tarification.common.none')}
        </label>
        <label className="tof-checkbox">
          <input
            type="radio"
            name={`agreement-time-mode-${agreement.id}`}
            checked={timeMode === 'separate'}
            disabled={!canManage}
            onChange={() => setTimeMode('separate')}
          />
          {t('tarification.surcharges.modeSeparate')}
        </label>
        <label className="tof-checkbox">
          <input
            type="radio"
            name={`agreement-time-mode-${agreement.id}`}
            checked={timeMode === 'combined'}
            disabled={!canManage}
            onChange={() => setTimeMode('combined')}
          />
          {t('tarification.surcharges.modeCombined')}
        </label>
      </div>
      {timeMode === 'separate' && (
        <div className="issued-items-form-row">
          <FormField label={t('tarification.surcharges.loadingMinutes')} htmlFor="agreement-included-loading">
            <input
              id="agreement-included-loading"
              type="number"
              min={0}
              value={includedLoadingMinutes}
              disabled={!canManage}
              onChange={(e) => setIncludedLoadingMinutes(e.target.value)}
            />
          </FormField>
          <FormField label={t('tarification.surcharges.unloadingMinutes')} htmlFor="agreement-included-unloading">
            <input
              id="agreement-included-unloading"
              type="number"
              min={0}
              value={includedUnloadingMinutes}
              disabled={!canManage}
              onChange={(e) => setIncludedUnloadingMinutes(e.target.value)}
            />
          </FormField>
        </div>
      )}
      {timeMode === 'combined' && (
        <div className="issued-items-form-row">
          <FormField label={t('tarification.surcharges.combinedMinutes')} htmlFor="agreement-included-combined">
            <input
              id="agreement-included-combined"
              type="number"
              min={0}
              value={includedCombinedMinutes}
              disabled={!canManage}
              onChange={(e) => setIncludedCombinedMinutes(e.target.value)}
            />
          </FormField>
        </div>
      )}
      {timeMode !== 'none' && (
        <div className="issued-items-form-row">
          <FormField label={t('tarification.surcharges.extraRate')} htmlFor="agreement-extra-hourly-rate">
            <input
              id="agreement-extra-hourly-rate"
              type="number"
              min={0}
              step="0.01"
              value={extraHourlyRate}
              disabled={!canManage}
              onChange={(e) => setExtraHourlyRate(e.target.value)}
            />
          </FormField>
        </div>
      )}

      {canManage && (
        <div className="customer-panel-header">
          <Button onClick={() => void save()} disabled={busy}>
            {busy ? t('ui.actions.busy') : t('ui.actions.save')}
          </Button>
        </div>
      )}
    </section>
  )
}
