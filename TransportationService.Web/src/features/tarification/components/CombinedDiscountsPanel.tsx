import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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

  if (discounts === null) return <p className="placeholder-text">{t('tarification.discounts.loading')}</p>

  function unitName(unitTypeId: string): string {
    return units.find((u) => u.id === unitTypeId)?.name ?? '?'
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.name.trim()) {
      setError(t('tarification.common.nameRequired'))
      return
    }

    const unitInputs = draft.units.filter((u) => u.unitTypeId).map((u) => ({
      unitTypeId: u.unitTypeId,
      equivalentFactor: Number(u.equivalentFactor) || 0,
    }))
    if (unitInputs.length === 0) {
      setError(t('tarification.discounts.chooseUnit'))
      return
    }

    const tierInputs = draft.tiers
      .filter((tier) => tier.fromCount.trim() !== '' && tier.percent.trim() !== '')
      .map((tier) => ({
        fromCount: Number(tier.fromCount) || 0,
        toCount: tier.toCount.trim() === '' ? null : Number(tier.toCount),
        percent: Number(tier.percent) || 0,
      }))
    if (tierInputs.length === 0) {
      setError(t('tarification.discounts.addTierRequired'))
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
      showSuccess(draft.discount ? t('tarification.discounts.updated') : t('tarification.discounts.created'))
      setDraft(null)
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.discounts.saveError')))
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
      showSuccess(t('tarification.discounts.deleted'))
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.discounts.deleteError')))
    }
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('tarification.discounts.title')}</h3>
        {canManage && <Button onClick={() => setDraft(toDraft(null))}>{t('tarification.discounts.new')}</Button>}
      </div>
      <p className="customer-form-muted">{t('tarification.discounts.intro')}</p>

      {discounts.length === 0 && <EmptyState message={t('tarification.discounts.empty')} />}
      {discounts.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('tarification.common.name')}</th>
              <th>{t('tarification.discounts.colScope')}</th>
              <th>{t('tarification.discounts.colUnits')}</th>
              <th>{t('tarification.discounts.colTiers')}</th>
              <th>{t('tarification.common.validFrom')}</th>
              <th>{t('tarification.common.validUntil')}</th>
              <th>{t('tarification.common.active')}</th>
              {canManage && <th aria-label={t('tarification.common.actions')} />}
            </tr>
          </thead>
          <tbody>
            {discounts.map((d) => (
              <tr key={d.id}>
                <td>{d.name}</td>
                <td>{t(DEGRESSION_SCOPE_LABELS[d.scope])}</td>
                <td>{d.units.map((u) => u.unitTypeName ?? unitName(u.unitTypeId)).join(', ')}</td>
                <td>{d.tiers.length}</td>
                <td>{d.effectiveFrom}</td>
                <td>{d.effectiveUntil ?? '—'}</td>
                <td>{d.isActive ? t('tarification.common.yes') : t('tarification.common.no')}</td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button type="button" className="issued-items-link" onClick={() => setDraft(toDraft(d))}>
                      {t('ui.actions.edit')}
                    </button>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setDeleteTarget(d)}
                    >
                      {t('ui.actions.delete')}
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
          title={draft.discount ? t('tarification.discounts.editTitle') : t('tarification.discounts.addTitle')}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="combined-discount-form" disabled={busy}>
                {t('ui.actions.save')}
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
            <FormField label={t('tarification.common.name')} htmlFor="discount-name" required>
              <input
                id="discount-name"
                value={draft.name}
                onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))}
                maxLength={200}
              />
            </FormField>
            <FormField label={t('tarification.discounts.colScope')} htmlFor="discount-scope" hint={t('tarification.discounts.scopeHint')}>
              <select
                id="discount-scope"
                value={draft.scope}
                onChange={(e) => setDraft((d) => (d ? { ...d, scope: e.target.value as DegressionScope } : d))}
              >
                {Object.entries(DEGRESSION_SCOPE_LABELS).map(([value, labelKey]) => (
                  <option key={value} value={value}>
                    {t(labelKey)}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.common.validFrom')} htmlFor="discount-from">
                <input
                  id="discount-from"
                  type="date"
                  value={draft.effectiveFrom}
                  onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))}
                />
              </FormField>
              <FormField label={t('tarification.common.validUntil')} htmlFor="discount-until" hint={t('tarification.assignments.untilHint')}>
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
              {t('tarification.common.active')}
            </label>

            <h4>{t('tarification.discounts.unitsHeading')}</h4>
            {draft.units.map((row, index) => (
              <div key={index} className="issued-items-form-row customer-rule-bracket">
                <select
                  aria-label={t('tarification.discounts.ariaUnit', { index: index + 1 })}
                  value={row.unitTypeId}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, units: d.units.map((u, i) => (i === index ? { ...u, unitTypeId: e.target.value } : u)) } : d,
                    )
                  }
                >
                  <option value="">{t('tarification.discounts.chooseUnitOption')}</option>
                  {units.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.name}
                    </option>
                  ))}
                </select>
                <input
                  aria-label={t('tarification.discounts.ariaFactor', { index: index + 1 })}
                  type="number"
                  step="0.001"
                  min={0}
                  placeholder={t('tarification.discounts.factorPlaceholder')}
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
                  {t('ui.actions.delete')}
                </Button>
              </div>
            ))}
            <Button
              variant="secondary"
              onClick={() => setDraft((d) => (d ? { ...d, units: [...d.units, { unitTypeId: '', equivalentFactor: '1' }] } : d))}
            >
              {t('tarification.discounts.addUnit')}
            </Button>

            <h4>{t('tarification.discounts.tiersHeading')}</h4>
            {draft.tiers.map((row, index) => (
              <div key={index} className="issued-items-form-row customer-rule-bracket">
                <input
                  aria-label={t('tarification.discounts.ariaTierFrom', { index: index + 1 })}
                  type="number"
                  step="0.01"
                  min={0}
                  placeholder={t('tarification.discounts.tierFromPlaceholder')}
                  value={row.fromCount}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, tiers: d.tiers.map((x, i) => (i === index ? { ...x, fromCount: e.target.value } : x)) } : d,
                    )
                  }
                />
                <input
                  aria-label={t('tarification.discounts.ariaTierTo', { index: index + 1 })}
                  type="number"
                  step="0.01"
                  min={0}
                  placeholder={t('tarification.discounts.tierToPlaceholder')}
                  value={row.toCount}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, tiers: d.tiers.map((x, i) => (i === index ? { ...x, toCount: e.target.value } : x)) } : d,
                    )
                  }
                />
                <input
                  aria-label={t('tarification.discounts.ariaTierPercent', { index: index + 1 })}
                  type="number"
                  step="0.01"
                  min={0}
                  max={100}
                  placeholder={t('tarification.discounts.tierPercentPlaceholder')}
                  value={row.percent}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, tiers: d.tiers.map((x, i) => (i === index ? { ...x, percent: e.target.value } : x)) } : d,
                    )
                  }
                />
                <Button
                  variant="ghost"
                  onClick={() => setDraft((d) => (d ? { ...d, tiers: d.tiers.filter((_, i) => i !== index) } : d))}
                >
                  {t('ui.actions.delete')}
                </Button>
              </div>
            ))}
            <Button
              variant="secondary"
              onClick={() => setDraft((d) => (d ? { ...d, tiers: [...d.tiers, { fromCount: '', toCount: '', percent: '' }] } : d))}
            >
              {t('tarification.discounts.addTier')}
            </Button>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('tarification.discounts.deleteTitle')}
          message={t('tarification.discounts.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
