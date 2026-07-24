import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
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

function intervalText(policy: EffectivePolicy): string {
  const parts: string[] = []
  if (policy.intervalMonths !== null) parts.push(`elke ${policy.intervalMonths} maanden`)
  if (policy.intervalKm !== null) parts.push(`elke ${policy.intervalKm.toLocaleString('nl-BE')} km`)
  return parts.join(' of ') || '—'
}

/**
 * Shows where the asset's effective maintenance/inspection rules come from
 * (voertuig/oplegger-regel → categorie → bedrijfsstandaard) and lets authorized users set
 * or remove an asset-specific override. Removing re-inherits the category/company rule —
 * inherited rules are never mutated from here.
 */
export function MaintenancePolicySummary({ assetKind, assetId }: MaintenancePolicySummaryProps) {
  const { hasPermission } = useAuth()
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
      .catch(() => setLoadError('Onderhoudsbeleid kon niet worden geladen.'))
  }, [assetKind, assetId])

  useEffect(() => {
    reload()
  }, [reload])

  async function submitOverride(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    const months = draft.intervalMonths.trim() === '' ? null : Number(draft.intervalMonths)
    const km = draft.intervalKm.trim() === '' ? null : Number(draft.intervalKm)
    if (months === null && km === null) {
      setDraftError('Geef een interval in maanden en/of kilometers op.')
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
      showSuccess('Specifieke regel ingesteld.')
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De regel kon niet worden opgeslagen.').message)
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
      showSuccess('De categorie-/bedrijfsstandaard geldt opnieuw.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De regel kon niet worden verwijderd.').message)
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!effective) return <p className="placeholder-text">Onderhoudsbeleid laden…</p>

  const rows: { kind: MaintenancePolicyKind; policy: EffectivePolicy | null }[] = [
    { kind: 'Maintenance', policy: effective.maintenance },
    { kind: 'Inspection', policy: effective.inspection },
  ]

  return (
    <div className="policy-summary">
      {rows.map(({ kind, policy }) => (
        <div key={kind} className="policy-summary-row">
          <div className="policy-summary-main">
            <strong>{POLICY_KIND_LABELS[kind]}</strong>
            {policy ? (
              <>
                <span>{intervalText(policy)}</span>
                <Badge tone={policy.level === 'Asset' ? 'info' : 'neutral'}>{policy.sourceLabel}</Badge>
                {policy.description && <span className="customer-form-muted">{policy.description}</span>}
              </>
            ) : (
              <span className="customer-form-muted">Geen regel geconfigureerd.</span>
            )}
          </div>
          {canManage && (
            <div className="policy-summary-actions">
              {policy?.level === 'Asset' ? (
                <Button variant="secondary" onClick={() => setResetTarget(policy)} disabled={busy}>
                  Gebruik opnieuw categorie-/bedrijfsstandaard
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
                  Afwijkende regel instellen
                </Button>
              )}
            </div>
          )}
        </div>
      ))}
      <p className="customer-form-muted">
        Voorrang: specifieke regel voor {assetKind === 'Vehicle' ? 'voertuig' : 'oplegger'} → categorieregel →
        bedrijfsstandaard. Een categorie- of bedrijfsregel overschrijft nooit een specifieke regel.
      </p>

      {draft && (
        <Modal
          title={`Afwijkende ${POLICY_KIND_LABELS[draft.kind].toLowerCase()}sregel`}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="policy-override-form" disabled={busy}>
                Opslaan
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
            <FormField label="Interval (maanden)" htmlFor="ovr-months">
              <input id="ovr-months" type="number" min={1} value={draft.intervalMonths} onChange={(e) => setDraft((d) => (d ? { ...d, intervalMonths: e.target.value } : d))} />
            </FormField>
            {assetKind === 'Vehicle' && (
              <FormField label="Interval (km)" htmlFor="ovr-km" hint="Optioneel; wat eerst komt geldt.">
                <input id="ovr-km" type="number" min={1} value={draft.intervalKm} onChange={(e) => setDraft((d) => (d ? { ...d, intervalKm: e.target.value } : d))} />
              </FormField>
            )}
            <FormField label="Waarschuwing (dagen vooraf)" htmlFor="ovr-warning">
              <input id="ovr-warning" type="number" min={0} max={365} value={draft.warningDays} onChange={(e) => setDraft((d) => (d ? { ...d, warningDays: e.target.value } : d))} />
            </FormField>
            <FormField label="Omschrijving" htmlFor="ovr-desc">
              <input id="ovr-desc" value={draft.description} onChange={(e) => setDraft((d) => (d ? { ...d, description: e.target.value } : d))} maxLength={300} />
            </FormField>
          </form>
        </Modal>
      )}

      {resetTarget && (
        <ConfirmDialog
          title="Specifieke regel verwijderen"
          message="De specifieke regel wordt verwijderd; daarna geldt opnieuw de categorie- of bedrijfsstandaard."
          confirmLabel="Verwijderen"
          destructive
          onConfirm={resetToInherited}
          onCancel={() => setResetTarget(null)}
        />
      )}
    </div>
  )
}
