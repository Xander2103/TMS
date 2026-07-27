import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { listUnitTypeSettings, type UnitTypeSettings } from '../api/pricingApi'
import {
  DEGRESSION_SCOPE_LABELS,
  createCombinedDiscount,
  deleteCombinedDiscount,
  listCombinedDiscounts,
  updateCombinedDiscount,
  type CombinedUnitDiscount,
  type CombinedUnitDiscountInput,
  type DegressionScope,
} from '../api/pricingApi'

interface CombinedDiscountsPanelProps {
  /** Mount on a customer's "Tarieven & toeslagen" tab: scopes to (and defaults new discounts to) this customer. */
  customerId?: string
  /** Mount on a pricing table's "Kortingen" tab: scopes to (and defaults new discounts to) this agreement. */
  agreementId?: string
}

interface UnitRowDraft {
  unitTypeId: string
  equivalentFactor: string
}

interface TierRowDraft {
  fromCount: string
  toCount: string
  percent: string
}

interface DiscountDraft {
  discount: CombinedUnitDiscount | null
  name: string
  scope: DegressionScope
  effectiveFrom: string
  effectiveUntil: string
  isActive: boolean
  units: UnitRowDraft[]
  tiers: TierRowDraft[]
}

function toDraft(discount: CombinedUnitDiscount | null): DiscountDraft {
  return {
    discount,
    name: discount?.name ?? '',
    scope: discount?.scope ?? 'DeliveryAddress',
    effectiveFrom: discount?.effectiveFrom ?? new Date().toISOString().slice(0, 10),
    effectiveUntil: discount?.effectiveUntil ?? '',
    isActive: discount?.isActive ?? true,
    units: discount?.units.map((u) => ({ unitTypeId: u.unitTypeId, equivalentFactor: String(u.equivalentFactor) })) ?? [
      { unitTypeId: '', equivalentFactor: '1' },
    ],
    tiers: discount?.tiers.map((t) => ({
      fromCount: String(t.fromCount),
      toCount: t.toCount === null ? '' : String(t.toCount),
      percent: String(t.percent),
    })) ?? [{ fromCount: '', toCount: '', percent: '' }],
  }
}

/**
 * Combined-unit degression discounts (spec §29-31): "1 europallet + 1 blokpallet + 2 colli = 4
 * eenheden → -8%". Reusable panel — mount scoped to a customer (customer-specific discount) or to
 * a pricing table (agreement-linked discount, only fires when that agreement is engaged).
 */
export function CombinedDiscountsPanel({ customerId, agreementId }: CombinedDiscountsPanelProps) {
  const { hasPermission } = useAuth()
  const canManage = hasPermission('tariffs.manage')
  const { showSuccess, showError } = useToast()

  const [discounts, setDiscounts] = useState<CombinedUnitDiscount[] | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [draft, setDraft] = useState<DiscountDraft | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<CombinedUnitDiscount | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    listCombinedDiscounts(customerId, agreementId)
      .then(setDiscounts)
      .catch(() => setDiscounts([]))
  }, [customerId, agreementId])

  useEffect(() => {
    reload()
  }, [reload])

  useEffect(() => {
    listUnitTypeSettings()
      .then((all) => setUnits(all.filter((u) => u.isActive)))
      .catch(() => setUnits([]))
  }, [])

  if (discounts === null) return <p className="placeholder-text">Combinatiekortingen laden…</p>

  function unitName(unitTypeId: string): string {
    return units.find((u) => u.id === unitTypeId)?.name ?? '?'
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.name.trim()) {
      setError('De naam is verplicht.')
      return
    }

    const unitInputs = draft.units.filter((u) => u.unitTypeId).map((u) => ({
      unitTypeId: u.unitTypeId,
      equivalentFactor: Number(u.equivalentFactor) || 0,
    }))
    if (unitInputs.length === 0) {
      setError('Kies minstens één eenheid.')
      return
    }

    const tierInputs = draft.tiers
      .filter((t) => t.fromCount.trim() !== '' && t.percent.trim() !== '')
      .map((t) => ({
        fromCount: Number(t.fromCount) || 0,
        toCount: t.toCount.trim() === '' ? null : Number(t.toCount),
        percent: Number(t.percent) || 0,
      }))
    if (tierInputs.length === 0) {
      setError('Voeg minstens één staffel toe.')
      return
    }

    const input: CombinedUnitDiscountInput = {
      customerId: customerId ?? draft.discount?.customerId ?? null,
      agreementId: agreementId ?? draft.discount?.agreementId ?? null,
      name: draft.name.trim(),
      scope: draft.scope,
      effectiveFrom: draft.effectiveFrom,
      effectiveUntil: draft.effectiveUntil.trim() === '' ? null : draft.effectiveUntil,
      isActive: draft.isActive,
      units: unitInputs,
      tiers: tierInputs,
    }

    setBusy(true)
    setError(null)
    try {
      const saved = draft.discount
        ? await updateCombinedDiscount(draft.discount.id, input)
        : await createCombinedDiscount(input)
      setDiscounts((prev) => {
        const others = (prev ?? []).filter((d) => d.id !== saved.id)
        return [...others, saved].sort((a, b) => a.name.localeCompare(b.name))
      })
      showSuccess(draft.discount ? 'Combinatiekorting bijgewerkt.' : 'Combinatiekorting aangemaakt.')
      setDraft(null)
    } catch (err) {
      setError(describeApiError(err, 'De combinatiekorting kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await deleteCombinedDiscount(target.id)
      setDiscounts((prev) => (prev ?? []).filter((d) => d.id !== target.id))
      showSuccess('Combinatiekorting verwijderd.')
    } catch (err) {
      showError(describeApiError(err, 'De combinatiekorting kon niet worden verwijderd.').message)
    }
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Combinatiekortingen</h3>
        {canManage && <Button onClick={() => setDraft(toDraft(null))}>+ Combinatiekorting</Button>}
      </div>
      <p className="customer-form-muted">
        Combineert verschillende eenheden tot één gewogen aantal (bv. 1 europallet + 1 blokpallet + 2 colli = 4
        eenheden) en past een staffelkorting toe — per leveradres, per stop, of over de hele order.
      </p>

      {discounts.length === 0 && <EmptyState message="Nog geen combinatiekortingen geconfigureerd." />}
      {discounts.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Naam</th>
              <th>Bereik</th>
              <th>Eenheden</th>
              <th>Staffels</th>
              <th>Geldig van</th>
              <th>Geldig tot</th>
              <th>Actief</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {discounts.map((d) => (
              <tr key={d.id}>
                <td>{d.name}</td>
                <td>{DEGRESSION_SCOPE_LABELS[d.scope]}</td>
                <td>{d.units.map((u) => u.unitTypeName ?? unitName(u.unitTypeId)).join(', ')}</td>
                <td>{d.tiers.length}</td>
                <td>{d.effectiveFrom}</td>
                <td>{d.effectiveUntil ?? '—'}</td>
                <td>{d.isActive ? 'Ja' : 'Nee'}</td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button type="button" className="issued-items-link" onClick={() => setDraft(toDraft(d))}>
                      Bewerken
                    </button>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setDeleteTarget(d)}
                    >
                      Verwijderen
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {draft && (
        <Modal
          title={draft.discount ? 'Combinatiekorting bewerken' : 'Combinatiekorting toevoegen'}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="combined-discount-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="combined-discount-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {error && (
              <div className="issued-items-form-error" role="alert">
                {error}
              </div>
            )}
            <FormField label="Naam" htmlFor="discount-name" required>
              <input
                id="discount-name"
                value={draft.name}
                onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))}
                maxLength={200}
              />
            </FormField>
            <FormField label="Bereik" htmlFor="discount-scope" hint="Hoe eenheden gecombineerd worden voordat de staffel wordt bepaald.">
              <select
                id="discount-scope"
                value={draft.scope}
                onChange={(e) => setDraft((d) => (d ? { ...d, scope: e.target.value as DegressionScope } : d))}
              >
                {Object.entries(DEGRESSION_SCOPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="issued-items-form-row">
              <FormField label="Geldig van" htmlFor="discount-from">
                <input
                  id="discount-from"
                  type="date"
                  value={draft.effectiveFrom}
                  onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))}
                />
              </FormField>
              <FormField label="Geldig tot" htmlFor="discount-until" hint="Leeg = onbeperkt.">
                <input
                  id="discount-until"
                  type="date"
                  value={draft.effectiveUntil}
                  onChange={(e) => setDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))}
                />
              </FormField>
            </div>
            <label className="tof-checkbox">
              <input
                type="checkbox"
                checked={draft.isActive}
                onChange={(e) => setDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))}
              />
              Actief
            </label>

            <h4>Eenheden</h4>
            {draft.units.map((row, index) => (
              <div key={index} className="issued-items-form-row customer-rule-bracket">
                <select
                  aria-label={`Eenheid ${index + 1}`}
                  value={row.unitTypeId}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, units: d.units.map((u, i) => (i === index ? { ...u, unitTypeId: e.target.value } : u)) } : d,
                    )
                  }
                >
                  <option value="">— Kies eenheid —</option>
                  {units.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.name}
                    </option>
                  ))}
                </select>
                <input
                  aria-label={`Eenheid ${index + 1} factor`}
                  type="number"
                  step="0.001"
                  min={0}
                  placeholder="factor"
                  value={row.equivalentFactor}
                  onChange={(e) =>
                    setDraft((d) =>
                      d
                        ? { ...d, units: d.units.map((u, i) => (i === index ? { ...u, equivalentFactor: e.target.value } : u)) }
                        : d,
                    )
                  }
                />
                <Button
                  variant="ghost"
                  onClick={() => setDraft((d) => (d ? { ...d, units: d.units.filter((_, i) => i !== index) } : d))}
                >
                  Verwijderen
                </Button>
              </div>
            ))}
            <Button
              variant="secondary"
              onClick={() => setDraft((d) => (d ? { ...d, units: [...d.units, { unitTypeId: '', equivalentFactor: '1' }] } : d))}
            >
              + Eenheid
            </Button>

            <h4>Staffels</h4>
            {draft.tiers.map((row, index) => (
              <div key={index} className="issued-items-form-row customer-rule-bracket">
                <input
                  aria-label={`Staffel ${index + 1} van`}
                  type="number"
                  step="0.01"
                  min={0}
                  placeholder="van"
                  value={row.fromCount}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, tiers: d.tiers.map((t, i) => (i === index ? { ...t, fromCount: e.target.value } : t)) } : d,
                    )
                  }
                />
                <input
                  aria-label={`Staffel ${index + 1} tot`}
                  type="number"
                  step="0.01"
                  min={0}
                  placeholder="tot (leeg = open)"
                  value={row.toCount}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, tiers: d.tiers.map((t, i) => (i === index ? { ...t, toCount: e.target.value } : t)) } : d,
                    )
                  }
                />
                <input
                  aria-label={`Staffel ${index + 1} korting %`}
                  type="number"
                  step="0.01"
                  min={0}
                  max={100}
                  placeholder="korting %"
                  value={row.percent}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, tiers: d.tiers.map((t, i) => (i === index ? { ...t, percent: e.target.value } : t)) } : d,
                    )
                  }
                />
                <Button
                  variant="ghost"
                  onClick={() => setDraft((d) => (d ? { ...d, tiers: d.tiers.filter((_, i) => i !== index) } : d))}
                >
                  Verwijderen
                </Button>
              </div>
            ))}
            <Button
              variant="secondary"
              onClick={() => setDraft((d) => (d ? { ...d, tiers: [...d.tiers, { fromCount: '', toCount: '', percent: '' }] } : d))}
            >
              + Staffel
            </Button>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Combinatiekorting verwijderen"
          message={`Combinatiekorting "${deleteTarget.name}" verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
