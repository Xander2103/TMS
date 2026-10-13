import { Fragment, useCallback, useEffect, useState } from 'react'
import { formatCurrency } from '../../../utils/numbers'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError, describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  createPriceRule,
  deleteBracketOverride,
  deletePriceRule,
  listBracketOverrides,
  listPriceRulesByAgreement,
  listPricingZones,
  listUnitTypeSettings,
  updatePriceRule,
  PRICE_RULE_BASIS_KEYS,
  type PriceRule,
  type PriceRuleBasis,
  type PriceRuleBracket,
  type PriceRuleBracketInput,
  type PriceRuleBracketOverride,
  type PriceRuleInput,
  type PricingZone,
  type UnitTypeSettings,
} from '../api/pricingApi'
import { listSalesCategories, type SalesCategory } from '../../accounting/api/accountingApi'
import { listActivityTypes, type ActivityType } from '../../dossiers/api/activityTypesApi'
import { BracketOverrideDialog } from './BracketOverrideDialog'
import './ruleGridEditor.css'

interface RuleGridEditorProps {
  agreementId: string
  /** The agreement's own CustomerId (null for shared/general tables) — new rules inherit it. */
  agreementCustomerId: string | null
  canManage: boolean
}

const today = () => new Date().toISOString().slice(0, 10)

/** Bases that price via brackets rather than a single unitPrice/UnitPrice. */
const BRACKET_BASES: PriceRuleBasis[] = ['QuantityBracket', 'WeightBracket']
/** Bases that need a unit (the rest are order-level measures or brackets without one). */
const UNIT_BOUND_BASES: PriceRuleBasis[] = ['PerUnit', 'QuantityBracket', 'Hourly']

function ruleToInput(rule: PriceRule): PriceRuleInput {
  return {
    customerId: rule.customerId,
    unitTypeId: rule.unitTypeId,
    basis: rule.basis,
    zoneId: rule.zoneId,
    name: rule.name,
    effectiveFrom: rule.effectiveFrom,
    effectiveUntil: rule.effectiveUntil,
    isActive: rule.isActive,
    unitPrice: rule.unitPrice,
    minimumAmount: rule.minimumAmount,
    brackets: rule.brackets.length > 0 ? rule.brackets.map(bracketToInput) : null,
    agreementId: rule.agreementId,
    priority: rule.priority,
    baseAmount: rule.baseAmount,
    oversizeLengthCm: rule.oversizeLengthCm,
    oversizeWidthCm: rule.oversizeWidthCm,
    oversizeBillableFactor: rule.oversizeBillableFactor,
    minimumQuantity: rule.minimumQuantity,
    quantityRoundingStep: rule.quantityRoundingStep,
    maximumAmount: rule.maximumAmount,
    bracketMode: rule.bracketMode,
    salesCategoryId: rule.salesCategoryId,
    originZoneId: rule.originZoneId,
    activityTypeId: rule.activityTypeId,
  }
}

function bracketToInput(b: PriceRuleBracket): PriceRuleBracketInput {
  return {
    fromQuantity: b.fromQuantity,
    toQuantity: b.toQuantity,
    price: b.price,
    pricePerExtraUnit: b.pricePerExtraUnit,
    weightToKg: b.weightToKg,
    volumeToM3: b.volumeToM3,
    loadingMetersTo: b.loadingMetersTo,
  }
}

function bracketRangeLabel(bracket: PriceRuleBracket): string {
  return bracket.toQuantity !== null ? `${bracket.fromQuantity}–${bracket.toQuantity}` : `${bracket.fromQuantity}+`
}

/**
 * The spreadsheet-feel grid for a rate table's price rules (house inline-onBlur pattern):
 * one row per rule, expandable bracket sub-rows for bracket bases, per-cell onBlur/onChange
 * saves via the existing rule PUT endpoint. Validation errors surface as a toast plus a red
 * outline on the offending cell; permission gating disables every input without tariffs.manage.
 */
export function RuleGridEditor({ agreementId, agreementCustomerId, canManage }: RuleGridEditorProps) {
  const { t } = useLocale()
  const { showError, showSuccess } = useToast()
  const [rules, setRules] = useState<PriceRule[] | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [salesCategories, setSalesCategories] = useState<SalesCategory[]>([])
  const [activityTypes, setActivityTypes] = useState<ActivityType[]>([])
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [rowErrors, setRowErrors] = useState<Record<string, FieldErrors>>({})
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [deleteTarget, setDeleteTarget] = useState<PriceRule | null>(null)
  const [bracketDeleteTarget, setBracketDeleteTarget] = useState<{ rule: PriceRule; index: number } | null>(null)
  const [overridesByRule, setOverridesByRule] = useState<Record<string, PriceRuleBracketOverride[]>>({})
  const [overrideDialog, setOverrideDialog] = useState<{ rule: PriceRule; bracket: PriceRuleBracket } | null>(null)
  const [overrideDeleteTarget, setOverrideDeleteTarget] = useState<PriceRuleBracketOverride | null>(null)

  const reload = useCallback(() => {
    Promise.all([
      listPriceRulesByAgreement(agreementId),
      listUnitTypeSettings().catch(() => [] as UnitTypeSettings[]),
      listPricingZones().catch(() => [] as PricingZone[]),
    ])
      .then(async ([ruleData, unitData, zoneData]) => {
        setRules(ruleData)
        setUnits(unitData)
        setZones(zoneData)
        setLoadErrorKey(null)
        // Row-level customer overrides only exist on shared/company bracket rules.
        const bracketRules = ruleData.filter((r) => BRACKET_BASES.includes(r.basis) && r.customerId === null)
        const loaded = await Promise.all(
          bracketRules.map((r) => listBracketOverrides(r.id).catch(() => [] as PriceRuleBracketOverride[])),
        )
        setOverridesByRule(Object.fromEntries(bracketRules.map((r, i) => [r.id, loaded[i]])))
      })
      .catch(() => setLoadErrorKey('tarification.grid.loadError'))
    // Sales codes feed the optional verkoopcategorie column; unavailable is fine.
    listSalesCategories()
      .then(setSalesCategories)
      .catch(() => {})
    // Activity types feed the optional activiteitstype column (P6); unavailable is fine.
    listActivityTypes()
      .then(setActivityTypes)
      .catch(() => {})
  }, [agreementId])

  useEffect(() => {
    reload()
  }, [reload])

  async function saveRule(rule: PriceRule, patch: Partial<PriceRuleInput>) {
    try {
      const updated = await updatePriceRule(rule.id, { ...ruleToInput(rule), ...patch })
      setRules((rs) => (rs ? rs.map((r) => (r.id === rule.id ? updated : r)) : rs))
      setRowErrors((e) => ({ ...e, [rule.id]: {} }))
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.grid.saveError')))
      setRowErrors((e) => ({ ...e, [rule.id]: describeApiError(err, '').fieldErrors }))
    }
  }

  function fieldError(ruleId: string, field: string): string | undefined {
    return getFieldError(rowErrors[ruleId], field)
  }

  function saveBracketField(rule: PriceRule, index: number, patch: Partial<PriceRuleBracketInput>) {
    const brackets = rule.brackets.map(bracketToInput)
    brackets[index] = { ...brackets[index], ...patch }
    void saveRule(rule, { brackets })
  }

  function addBracket(rule: PriceRule) {
    const brackets = rule.brackets.map(bracketToInput)
    const last = brackets[brackets.length - 1]
    const from = last ? (last.toQuantity ?? last.fromQuantity) + 1 : 1
    void saveRule(rule, {
      brackets: [...brackets, { fromQuantity: from, toQuantity: null, price: 0, pricePerExtraUnit: null, weightToKg: null, volumeToM3: null, loadingMetersTo: null }],
    })
  }

  function removeBracket(rule: PriceRule, index: number) {
    const brackets = rule.brackets.map(bracketToInput).filter((_, i) => i !== index)
    if (brackets.length === 0) {
      showError(t('tarification.grid.minBracket'))
      return
    }
    void saveRule(rule, { brackets })
  }

  function handleConfirmRemoveBracket() {
    if (!bracketDeleteTarget) return
    const { rule, index } = bracketDeleteTarget
    setBracketDeleteTarget(null)
    removeBracket(rule, index)
  }

  async function handleConfirmRemoveOverride() {
    if (!overrideDeleteTarget) return
    const target = overrideDeleteTarget
    setOverrideDeleteTarget(null)
    try {
      await deleteBracketOverride(target.id)
      showSuccess(t('tarification.grid.overrideDeleted'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.grid.overrideDeleteError')))
    }
  }

  /** Active overrides belonging to exactly this bracket row (matched on the row's value identity). */
  function overridesForBracket(rule: PriceRule, bracket: PriceRuleBracket): PriceRuleBracketOverride[] {
    return (overridesByRule[rule.id] ?? []).filter(
      (o) =>
        !o.orphaned &&
        o.fromQuantity === bracket.fromQuantity &&
        o.toQuantity === bracket.toQuantity &&
        o.weightToKg === bracket.weightToKg &&
        o.volumeToM3 === bracket.volumeToM3 &&
        o.loadingMetersTo === bracket.loadingMetersTo,
    )
  }

  async function addRule() {
    try {
      await createPriceRule({
        customerId: agreementCustomerId,
        unitTypeId: null,
        basis: 'Fixed',
        zoneId: null,
        name: t('tarification.grid.newRuleName'),
        effectiveFrom: today(),
        effectiveUntil: null,
        isActive: true,
        unitPrice: 0,
        minimumAmount: null,
        brackets: null,
        agreementId,
        priority: 0,
      })
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.grid.addError')))
    }
  }

  async function duplicateRule(rule: PriceRule) {
    try {
      await createPriceRule({ ...ruleToInput(rule), name: t('tarification.grid.copyName', { name: rule.name }) })
      showSuccess(t('tarification.grid.duplicated'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.grid.duplicateError')))
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await deletePriceRule(target.id)
      showSuccess(t('tarification.grid.deleted'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.grid.deleteError')))
    }
  }

  function toggleCollapsed(ruleId: string) {
    setCollapsed((s) => {
      const next = new Set(s)
      if (next.has(ruleId)) next.delete(ruleId)
      else next.add(ruleId)
      return next
    })
  }

  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>
  if (rules === null) return <p className="placeholder-text">{t('tarification.grid.loading')}</p>

  const pricingUnits = units.filter((u) => u.isActive && u.allowForPricing)

  return (
    <div>
      {canManage && (
        <div className="tof-documents-toolbar">
          <Button onClick={() => void addRule()}>{t('tarification.grid.addRule')}</Button>
        </div>
      )}
      {rules.length === 0 && <EmptyState message={t('tarification.grid.empty')} />}
      {rules.length > 0 && (
        <div className="rule-grid-scroll">
          <table className="rule-grid-table">
            <thead>
              <tr>
                <th>{t('tarification.common.name')}</th>
                <th>{t('tarification.grid.colBasis')}</th>
                <th>{t('tarification.common.unit')}</th>
                <th>{t('tarification.grid.colZone')}</th>
                <th title={t('tarification.grid.fromZoneTitle')}>{t('tarification.grid.colFromZone')}</th>
                <th title={t('tarification.grid.activityTypeTitle')}>{t('tarification.grid.colActivityType')}</th>
                <th>{t('tarification.grid.colPriority')}</th>
                <th>{t('tarification.grid.colPrice')}</th>
                <th>{t('tarification.grid.colExtraPerUnit')}</th>
                <th>{t('tarification.grid.colBaseAmount')}</th>
                <th>{t('tarification.grid.colMin')}</th>
                <th>{t('tarification.grid.colMax')}</th>
                <th title={t('tarification.grid.minQuantityTitle')}>{t('tarification.grid.colMinQuantity')}</th>
                <th title={t('tarification.grid.roundingStepTitle')}>{t('tarification.grid.colRoundingStep')}</th>
                <th>{t('tarification.grid.colFrom')}</th>
                <th>{t('tarification.grid.colTo')}</th>
                <th>{t('tarification.grid.colWeightTo')}</th>
                <th>{t('tarification.grid.colVolumeTo')}</th>
                <th>{t('tarification.grid.colLdmTo')}</th>
                <th>{t('tarification.common.validFrom')}</th>
                <th>{t('tarification.common.validUntil')}</th>
                <th title={t('tarification.grid.salesCategoryTitle')}>{t('tarification.grid.colSalesCategory')}</th>
                {canManage && <th aria-label={t('tarification.common.actions')} />}
              </tr>
            </thead>
            <tbody>
              {rules.map((rule) => {
                const isBracketBasis = BRACKET_BASES.includes(rule.basis)
                const isUnitBound = UNIT_BOUND_BASES.includes(rule.basis)
                const expanded = isBracketBasis && !collapsed.has(rule.id)
                return (
                  <Fragment key={rule.id}>
                    <tr>
                      <td>
                        {isBracketBasis && (
                          <button
                            type="button"
                            className="rule-grid-toggle"
                            aria-label={expanded ? t('tarification.grid.collapse', { name: rule.name }) : t('tarification.grid.expand', { name: rule.name })}
                            onClick={() => toggleCollapsed(rule.id)}
                          >
                            {expanded ? '▾' : '▸'}
                          </button>
                        )}
                        <input
                          aria-label={t('tarification.grid.ariaNameFor', { name: rule.name })}
                          defaultValue={rule.name}
                          disabled={!canManage}
                          className={fieldError(rule.id, 'name') ? 'rule-grid-cell-invalid' : undefined}
                          onBlur={(e) => {
                            if (e.target.value.trim() !== rule.name) void saveRule(rule, { name: e.target.value.trim() })
                          }}
                        />
                      </td>
                      <td>
                        <select
                          aria-label={t('tarification.grid.ariaBasisFor', { name: rule.name })}
                          value={rule.basis}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { basis: e.target.value as PriceRuleBasis })}
                        >
                          {Object.entries(PRICE_RULE_BASIS_KEYS).map(([value, labelKey]) => (
                            <option key={value} value={value}>
                              {t(labelKey)}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        {isUnitBound ? (
                          <select
                            aria-label={t('tarification.grid.ariaUnitFor', { name: rule.name })}
                            value={rule.unitTypeId ?? ''}
                            disabled={!canManage}
                            className={fieldError(rule.id, 'unitTypeId') ? 'rule-grid-cell-invalid' : undefined}
                            onChange={(e) => void saveRule(rule, { unitTypeId: e.target.value || null })}
                          >
                            <option value="">{t('tarification.grid.chooseUnit')}</option>
                            {pricingUnits.map((unit) => (
                              <option key={unit.id} value={unit.id}>
                                {unit.name}
                              </option>
                            ))}
                          </select>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td>
                        <select
                          aria-label={t('tarification.grid.ariaZoneFor', { name: rule.name })}
                          value={rule.zoneId ?? ''}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { zoneId: e.target.value || null })}
                        >
                          <option value="">{t('tarification.grid.allOption')}</option>
                          {zones.map((zone) => (
                            <option key={zone.id} value={zone.id}>
                              {zone.code}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <select
                          aria-label={t('tarification.grid.ariaOriginZoneFor', { name: rule.name })}
                          value={rule.originZoneId ?? ''}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { originZoneId: e.target.value || null })}
                        >
                          <option value="">{t('tarification.grid.allOption')}</option>
                          {zones.map((zone) => (
                            <option key={zone.id} value={zone.id}>
                              {zone.code}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <select
                          aria-label={t('tarification.grid.ariaActivityFor', { name: rule.name })}
                          value={rule.activityTypeId ?? ''}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { activityTypeId: e.target.value || null })}
                        >
                          <option value="">{t('tarification.grid.allOption')}</option>
                          {activityTypes.map((type) => (
                            <option key={type.id} value={type.id}>
                              {type.name}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <input
                          aria-label={t('tarification.grid.ariaPriorityFor', { name: rule.name })}
                          type="number"
                          defaultValue={rule.priority}
                          disabled={!canManage}
                          className={fieldError(rule.id, 'priority') ? 'rule-grid-cell-invalid' : undefined}
                          onBlur={(e) => {
                            const value = Number(e.target.value) || 0
                            if (value !== rule.priority) void saveRule(rule, { priority: value })
                          }}
                        />
                      </td>
                      <td>
                        {isBracketBasis ? (
                          '—'
                        ) : (
                          <input
                            aria-label={t('tarification.grid.ariaPriceFor', { name: rule.name })}
                            type="number"
                            step="0.01"
                            defaultValue={rule.unitPrice ?? ''}
                            disabled={!canManage}
                            className={fieldError(rule.id, 'unitPrice') ? 'rule-grid-cell-invalid' : undefined}
                            onBlur={(e) => {
                              const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                              if (value !== rule.unitPrice) void saveRule(rule, { unitPrice: value })
                            }}
                          />
                        )}
                      </td>
                      <td>—</td>
                      <td>
                        <input
                          aria-label={t('tarification.grid.ariaBaseAmountFor', { name: rule.name })}
                          type="number"
                          step="0.01"
                          defaultValue={rule.baseAmount ?? ''}
                          disabled={!canManage}
                          className={fieldError(rule.id, 'baseAmount') ? 'rule-grid-cell-invalid' : undefined}
                          onBlur={(e) => {
                            const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                            if (value !== rule.baseAmount) void saveRule(rule, { baseAmount: value })
                          }}
                        />
                      </td>
                      <td>
                        <input
                          aria-label={t('tarification.grid.ariaMinFor', { name: rule.name })}
                          type="number"
                          step="0.01"
                          defaultValue={rule.minimumAmount ?? ''}
                          disabled={!canManage}
                          onBlur={(e) => {
                            const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                            if (value !== rule.minimumAmount) void saveRule(rule, { minimumAmount: value })
                          }}
                        />
                      </td>
                      <td>
                        <input
                          aria-label={t('tarification.grid.ariaMaxFor', { name: rule.name })}
                          type="number"
                          step="0.01"
                          defaultValue={rule.maximumAmount ?? ''}
                          disabled={!canManage}
                          className={fieldError(rule.id, 'maximumAmount') ? 'rule-grid-cell-invalid' : undefined}
                          onBlur={(e) => {
                            const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                            if (value !== rule.maximumAmount) void saveRule(rule, { maximumAmount: value })
                          }}
                        />
                      </td>
                      <td>
                        {rule.basis === 'Hourly' ? (
                          <input
                            aria-label={t('tarification.grid.ariaMinHoursFor', { name: rule.name })}
                            type="number"
                            step="0.01"
                            min="0"
                            defaultValue={rule.minimumQuantity ?? ''}
                            disabled={!canManage}
                            className={fieldError(rule.id, 'minimumQuantity') ? 'rule-grid-cell-invalid' : undefined}
                            onBlur={(e) => {
                              const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                              if (value !== null && (Number.isNaN(value) || value < 0)) {
                                showError(t('tarification.grid.minQtyError'))
                                return
                              }
                              if (value !== rule.minimumQuantity) void saveRule(rule, { minimumQuantity: value })
                            }}
                          />
                        ) : (
                          '—'
                        )}
                      </td>
                      <td>
                        {rule.basis === 'Hourly' ? (
                          <input
                            aria-label={t('tarification.grid.ariaRoundingFor', { name: rule.name })}
                            type="number"
                            step="0.01"
                            min="0"
                            placeholder={t('tarification.grid.roundingPlaceholder')}
                            defaultValue={rule.quantityRoundingStep ?? ''}
                            disabled={!canManage}
                            className={fieldError(rule.id, 'quantityRoundingStep') ? 'rule-grid-cell-invalid' : undefined}
                            onBlur={(e) => {
                              const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                              if (value !== null && (Number.isNaN(value) || value < 0)) {
                                showError(t('tarification.grid.roundingError'))
                                return
                              }
                              if (value !== rule.quantityRoundingStep) void saveRule(rule, { quantityRoundingStep: value })
                            }}
                          />
                        ) : (
                          '—'
                        )}
                      </td>
                      <td colSpan={5}>—</td>
                      <td>
                        <input
                          aria-label={t('tarification.grid.ariaValidFromFor', { name: rule.name })}
                          type="date"
                          defaultValue={rule.effectiveFrom}
                          disabled={!canManage}
                          onBlur={(e) => {
                            if (e.target.value !== rule.effectiveFrom) void saveRule(rule, { effectiveFrom: e.target.value })
                          }}
                        />
                      </td>
                      <td>
                        <input
                          aria-label={t('tarification.grid.ariaValidUntilFor', { name: rule.name })}
                          type="date"
                          defaultValue={rule.effectiveUntil ?? ''}
                          disabled={!canManage}
                          onBlur={(e) => {
                            const value = e.target.value || null
                            if (value !== rule.effectiveUntil) void saveRule(rule, { effectiveUntil: value })
                          }}
                        />
                      </td>
                      <td>
                        <select
                          aria-label={t('tarification.grid.ariaSalesCatFor', { name: rule.name })}
                          value={rule.salesCategoryId ?? ''}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { salesCategoryId: e.target.value || null })}
                        >
                          <option value="">{t('tarification.grid.fromTable')}</option>
                          {salesCategories.map((category) => (
                            <option key={category.id} value={category.id}>
                              {category.name}
                            </option>
                          ))}
                        </select>
                      </td>
                      {canManage && (
                        <td className="issued-items-row-actions">
                          <button type="button" className="issued-items-link" onClick={() => void duplicateRule(rule)}>
                            {t('tarification.grid.duplicateAction')}
                          </button>
                          <button
                            type="button"
                            className="issued-items-link issued-items-link-danger"
                            onClick={() => setDeleteTarget(rule)}
                          >
                            {t('ui.actions.delete')}
                          </button>
                        </td>
                      )}
                    </tr>
                    {expanded &&
                      rule.brackets.map((bracket, index) => (
                        // bracket.id is server-assigned and regenerated on every save; index keeps
                        // the key stable/unique even for a not-yet-persisted or id-less bracket.
                        <Fragment key={bracket.id ?? `${rule.id}-bracket-${index}`}>
                        <tr className="rule-grid-bracket-row">
                          <td>↳ {t('tarification.grid.bracketRowLabel', { range: bracketRangeLabel(bracket) })}</td>
                          <td colSpan={6}>—</td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketPrice', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              defaultValue={bracket.price}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = Number(e.target.value) || 0
                                if (value !== bracket.price) saveBracketField(rule, index, { price: value })
                              }}
                            />
                          </td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketExtra', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              defaultValue={bracket.pricePerExtraUnit ?? ''}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                                if (value !== bracket.pricePerExtraUnit) saveBracketField(rule, index, { pricePerExtraUnit: value })
                              }}
                            />
                          </td>
                          <td colSpan={5}>—</td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketFrom', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              defaultValue={bracket.fromQuantity}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = Number(e.target.value) || 0
                                if (value !== bracket.fromQuantity) saveBracketField(rule, index, { fromQuantity: value })
                              }}
                            />
                          </td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketTo', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              placeholder={t('tarification.grid.openPlaceholder')}
                              defaultValue={bracket.toQuantity ?? ''}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                                if (value !== bracket.toQuantity) saveBracketField(rule, index, { toQuantity: value })
                              }}
                            />
                          </td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketWeight', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              defaultValue={bracket.weightToKg ?? ''}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                                if (value !== bracket.weightToKg) saveBracketField(rule, index, { weightToKg: value })
                              }}
                            />
                          </td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketVolume', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              defaultValue={bracket.volumeToM3 ?? ''}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                                if (value !== bracket.volumeToM3) saveBracketField(rule, index, { volumeToM3: value })
                              }}
                            />
                          </td>
                          <td>
                            <input
                              aria-label={t('tarification.grid.ariaBracketLdm', { index: index + 1, name: rule.name })}
                              type="number"
                              step="0.01"
                              defaultValue={bracket.loadingMetersTo ?? ''}
                              disabled={!canManage}
                              onBlur={(e) => {
                                const value = e.target.value.trim() === '' ? null : Number(e.target.value)
                                if (value !== bracket.loadingMetersTo) saveBracketField(rule, index, { loadingMetersTo: value })
                              }}
                            />
                          </td>
                          <td colSpan={3}>—</td>
                          {canManage && (
                            <td className="issued-items-row-actions">
                              {rule.customerId === null && (
                                <button
                                  type="button"
                                  className="issued-items-link"
                                  onClick={() => setOverrideDialog({ rule, bracket })}
                                >
                                  {t('tarification.grid.overrideAction')}
                                </button>
                              )}
                              <button
                                type="button"
                                className="issued-items-link issued-items-link-danger"
                                onClick={() => setBracketDeleteTarget({ rule, index })}
                              >
                                {t('ui.actions.delete')}
                              </button>
                            </td>
                          )}
                        </tr>
                        {overridesForBracket(rule, bracket).map((override) => (
                          <tr key={override.id} className="rule-grid-bracket-row rule-grid-override-row">
                            <td>
                              ↳ <Badge tone="info">{t('tarification.grid.overrideBadge')}</Badge> <span>{override.customerName}</span>
                            </td>
                            <td colSpan={6}>—</td>
                            <td>{formatCurrency(override.price)}</td>
                            <td>{override.pricePerExtraUnit !== null ? formatCurrency(override.pricePerExtraUnit) : '—'}</td>
                            <td colSpan={5}>—</td>
                            <td colSpan={5}>—</td>
                            <td>{override.effectiveFrom ?? '—'}</td>
                            <td>{override.effectiveUntil ?? '—'}</td>
                            <td>—</td>
                            {canManage && (
                              <td className="issued-items-row-actions">
                                <button
                                  type="button"
                                  className="issued-items-link issued-items-link-danger"
                                  onClick={() => setOverrideDeleteTarget(override)}
                                >
                                  {t('ui.actions.delete')}
                                </button>
                              </td>
                            )}
                          </tr>
                        ))}
                        </Fragment>
                      ))}
                    {expanded &&
                      (overridesByRule[rule.id] ?? [])
                        .filter((o) => o.orphaned)
                        .map((override) => (
                          <tr key={override.id} className="rule-grid-bracket-row rule-grid-override-row">
                            <td colSpan={22}>
                              ⚠ {t('tarification.grid.orphanedOverride', {
                                name: override.customerName,
                                range: `${override.fromQuantity}–${override.toQuantity ?? t('tarification.grid.openPlaceholder')}`,
                              })}
                            </td>
                            {canManage && (
                              <td className="issued-items-row-actions">
                                <button
                                  type="button"
                                  className="issued-items-link issued-items-link-danger"
                                  onClick={() => setOverrideDeleteTarget(override)}
                                >
                                  {t('ui.actions.delete')}
                                </button>
                              </td>
                            )}
                          </tr>
                        ))}
                    {expanded && canManage && (
                      <tr className="rule-grid-bracket-row">
                        <td colSpan={23}>
                          <button type="button" className="issued-items-link" onClick={() => addBracket(rule)}>
                            {t('tarification.grid.addBracketRow')}
                          </button>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('tarification.grid.deleteRuleTitle')}
          message={t('tarification.grid.deleteRuleMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {bracketDeleteTarget && (
        <ConfirmDialog
          title={t('tarification.grid.deleteBracketTitle')}
          message={t('tarification.grid.deleteBracketMessage', {
            index: bracketDeleteTarget.index + 1,
            name: bracketDeleteTarget.rule.name,
          })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleConfirmRemoveBracket}
          onCancel={() => setBracketDeleteTarget(null)}
        />
      )}

      {overrideDeleteTarget && (
        <ConfirmDialog
          title={t('tarification.grid.deleteOverrideTitle')}
          message={t('tarification.grid.deleteOverrideMessage', { name: overrideDeleteTarget.customerName })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={() => void handleConfirmRemoveOverride()}
          onCancel={() => setOverrideDeleteTarget(null)}
        />
      )}

      {overrideDialog && (
        <BracketOverrideDialog
          rule={overrideDialog.rule}
          bracket={overrideDialog.bracket}
          onSaved={() => {
            setOverrideDialog(null)
            reload()
          }}
          onClose={() => setOverrideDialog(null)}
        />
      )}
    </div>
  )
}
