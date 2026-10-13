import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { formatInteger } from '../../../utils/numbers'
import { createMaintenancePolicy, deleteMaintenancePolicy, getEffectivePolicies } from '../api/maintenancePoliciesApi'
import { POLICY_KIND_LABELS, type EffectivePolicies, type EffectivePolicy, type FleetAssetKind, type MaintenancePolicyKind } from '../types'
import './maintenance-policy-summary.css'

interface MaintenancePolicySummaryProps {
  assetKind: FleetAssetKind
  assetId: string
}

interface OverrideDraft {
  kind: MaintenancePolicyKind
  intervalMonths: string
  intervalKm: string
  warningDays: string
  description: string
}

function intervalText(t: TranslateFn, policy: EffectivePolicy): string {
  const parts: string[] = []
  if (policy.intervalMonths !== null) parts.push(t('maintenance.policy.everyMonths', { months: policy.intervalMonths }))
  if (policy.intervalKm !== null) parts.push(t('maintenance.policy.everyKm', { km: formatInteger(policy.intervalKm) }))
  return parts.join(` ${t('maintenance.policy.or')} `) || '—'
}

/**
 * Shows where the asset's effective maintenance/inspection rules come from
 * (voertuig/oplegger-regel → categorie → bedrijfsstandaard) and lets authorized users set
 * or remove an asset-specific override. Removing re-inherits the category/company rule —
 * inherited rules are never mutated from here.
 */
export function MaintenancePolicySummary({ assetKind, assetId }: MaintenancePolicySummaryProps) {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('maintenance_policies.manage')

  const [effective, setEffective] = useState<EffectivePolicies | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<OverrideDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [resetTarget, setResetTarget] = useState<EffectivePolicy | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    getEffectivePolicies(assetKind, assetId)
      .then((data) => {
        setEffective(data)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('maintenance.policy.loadFailed')))
  }, [assetKind, assetId, t])

  useEffect(() => {
    reload()
  }, [reload])

  async function submitOverride(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    const months = draft.intervalMonths.trim() === '' ? null : Number(draft.intervalMonths)
    const km = draft.intervalKm.trim() === '' ? null : Number(draft.intervalKm)
    if (months === null && km === null) {
      setDraftError(t('maintenance.policy.intervalRequired'))
      return
    }
    setBusy(true)
    try {
      await createMaintenancePolicy({
        kind: draft.kind,
        assetKind,
        categoryId: null,
        vehicleId: assetKind === 'Vehicle' ? assetId : null,
        trailerId: assetKind === 'Trailer' ? assetId : null,
        intervalMonths: months,
        intervalKm: assetKind === 'Vehicle' ? km : null,
        warningDays: Number(draft.warningDays) || 30,
        description: draft.description.trim() || null,
        isActive: true,
      })
      showSuccess(t('maintenance.policy.overrideSet'))
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(localizeApiError(t, err, t('maintenance.policy.overrideSaveFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function resetToInherited() {
    if (!resetTarget) return
    const target = resetTarget
    setResetTarget(null)
    setBusy(true)
    try {
      await deleteMaintenancePolicy(target.policyId)
      showSuccess(t('maintenance.policy.inheritedAgain'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('maintenance.policy.deleteFailed')))
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!effective) return <p className="placeholder-text">{t('maintenance.policy.loading')}</p>

  const rows: { kind: MaintenancePolicyKind; policy: EffectivePolicy | null }[] = [
    { kind: 'Maintenance', policy: effective.maintenance },
    { kind: 'Inspection', policy: effective.inspection },
  ]

  return (
    <div className="policy-summary">
      {rows.map(({ kind, policy }) => (
        <div key={kind} className="policy-summary-row">
          <div className="policy-summary-main">
            <strong>{t(POLICY_KIND_LABELS[kind])}</strong>
            {policy ? (
              <>
                <span>{intervalText(t, policy)}</span>
                <Badge tone={policy.level === 'Asset' ? 'info' : 'neutral'}>{policy.sourceLabel}</Badge>
                {policy.description && <span className="customer-form-muted">{policy.description}</span>}
              </>
            ) : (
              <span className="customer-form-muted">{t('maintenance.policy.noRule')}</span>
            )}
          </div>
          {canManage && (
            <div className="policy-summary-actions">
              {policy?.level === 'Asset' ? (
                <Button variant="secondary" onClick={() => setResetTarget(policy)} disabled={busy}>
                  {t('maintenance.policy.useInherited')}
                </Button>
              ) : (
                <Button
                  variant="secondary"
                  onClick={() => {
                    setDraftError(null)
                    setDraft({ kind, intervalMonths: '', intervalKm: '', warningDays: '30', description: '' })
                  }}
                  disabled={busy}
                >
                  {t('maintenance.policy.setOverride')}
                </Button>
              )}
            </div>
          )}
        </div>
      ))}
      <p className="customer-form-muted">
        {t('maintenance.policy.precedence', { asset: t(`maintenance.policy.assetLower.${assetKind}`) })}
      </p>

      {draft && (
        <Modal
          title={t(`maintenance.policy.overrideTitle.${draft.kind}`)}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="policy-override-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="policy-override-form" className="issued-items-form" onSubmit={submitOverride} noValidate>
            {draftError && (
              <div className="vehicle-form-error" role="alert">
                {draftError}
              </div>
            )}
            <FormField label={t('maintenance.policy.intervalMonths')} htmlFor="ovr-months">
              <input id="ovr-months" type="number" min={1} value={draft.intervalMonths} onChange={(e) => setDraft((d) => (d ? { ...d, intervalMonths: e.target.value } : d))} />
            </FormField>
            {assetKind === 'Vehicle' && (
              <FormField label={t('maintenance.policy.intervalKm')} htmlFor="ovr-km" hint={t('maintenance.policy.intervalKmHint')}>
                <input id="ovr-km" type="number" min={1} value={draft.intervalKm} onChange={(e) => setDraft((d) => (d ? { ...d, intervalKm: e.target.value } : d))} />
              </FormField>
            )}
            <FormField label={t('maintenance.policy.warningDaysBefore')} htmlFor="ovr-warning">
              <input id="ovr-warning" type="number" min={0} max={365} value={draft.warningDays} onChange={(e) => setDraft((d) => (d ? { ...d, warningDays: e.target.value } : d))} />
            </FormField>
            <FormField label={t('maintenance.policy.description')} htmlFor="ovr-desc">
              <input id="ovr-desc" value={draft.description} onChange={(e) => setDraft((d) => (d ? { ...d, description: e.target.value } : d))} maxLength={300} />
            </FormField>
          </form>
        </Modal>
      )}

      {resetTarget && (
        <ConfirmDialog
          title={t('maintenance.policy.resetTitle')}
          message={t('maintenance.policy.resetMessage')}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={resetToInherited}
          onCancel={() => setResetTarget(null)}
        />
      )}
    </div>
  )
}
