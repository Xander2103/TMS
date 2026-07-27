import { Fragment, useCallback, useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import {
  createPriceRule,
  deletePriceRule,
  listPriceRulesByAgreement,
  listPricingZones,
  listUnitTypeSettings,
  updatePriceRule,
  PRICE_RULE_BASIS_LABELS,
  type PriceRule,
  type PriceRuleBasis,
  type PriceRuleBracket,
  type PriceRuleBracketInput,
  type PriceRuleInput,
  type PricingZone,
  type UnitTypeSettings,
} from '../api/pricingApi'
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
  const { showError, showSuccess } = useToast()
  const [rules, setRules] = useState<PriceRule[] | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [rowErrors, setRowErrors] = useState<Record<string, FieldErrors>>({})
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [deleteTarget, setDeleteTarget] = useState<PriceRule | null>(null)
  const [bracketDeleteTarget, setBracketDeleteTarget] = useState<{ rule: PriceRule; index: number } | null>(null)

  const reload = useCallback(() => {
    Promise.all([
      listPriceRulesByAgreement(agreementId),
      listUnitTypeSettings().catch(() => [] as UnitTypeSettings[]),
      listPricingZones().catch(() => [] as PricingZone[]),
    ])
      .then(([ruleData, unitData, zoneData]) => {
        setRules(ruleData)
        setUnits(unitData)
        setZones(zoneData)
        setLoadError(null)
      })
      .catch(() => setLoadError('De prijsregels konden niet worden geladen.'))
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
      const described = describeApiError(err, 'De prijsregel kon niet worden opgeslagen.')
      showError(described.message)
      setRowErrors((e) => ({ ...e, [rule.id]: described.fieldErrors }))
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
      showError('Een staffelregel heeft minstens één staffel nodig.')
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

  async function addRule() {
    try {
      await createPriceRule({
        customerId: agreementCustomerId,
        unitTypeId: null,
        basis: 'Fixed',
        zoneId: null,
        name: 'Nieuwe regel',
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
      showError(describeApiError(err, 'De prijsregel kon niet worden toegevoegd.').message)
    }
  }

  async function duplicateRule(rule: PriceRule) {
    try {
      await createPriceRule({ ...ruleToInput(rule), name: `${rule.name} (kopie)` })
      showSuccess('Prijsregel gedupliceerd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De prijsregel kon niet worden gedupliceerd.').message)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await deletePriceRule(target.id)
      showSuccess('Prijsregel verwijderd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De prijsregel kon niet worden verwijderd.').message)
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

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (rules === null) return <p className="placeholder-text">Prijsregels laden…</p>

  const pricingUnits = units.filter((u) => u.isActive && u.allowForPricing)

  return (
    <div>
      {canManage && (
        <div className="tof-documents-toolbar">
          <Button onClick={() => void addRule()}>+ Regel</Button>
        </div>
      )}
      {rules.length === 0 && <EmptyState message="Nog geen prijsregels in deze tarieventabel." />}
      {rules.length > 0 && (
        <div className="rule-grid-scroll">
          <table className="rule-grid-table">
            <thead>
              <tr>
                <th>Naam</th>
                <th>Basis</th>
                <th>Eenheid</th>
                <th>Zone</th>
                <th>Prioriteit</th>
                <th>Prijs / staffelprijs</th>
                <th>Extra / eenheid</th>
                <th>Basisbedrag</th>
                <th>Min</th>
                <th>Max</th>
                <th>Van</th>
                <th>Tot</th>
                <th>Gewicht tot (kg)</th>
                <th>Volume tot (m³)</th>
                <th>Ldm tot</th>
                <th>Geldig van</th>
                <th>Geldig tot</th>
                {canManage && <th aria-label="Acties" />}
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
                            aria-label={expanded ? `${rule.name} inklappen` : `${rule.name} uitklappen`}
                            onClick={() => toggleCollapsed(rule.id)}
                          >
                            {expanded ? '▾' : '▸'}
                          </button>
                        )}
                        <input
                          aria-label={`Naam voor ${rule.name}`}
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
                          aria-label={`Basis voor ${rule.name}`}
                          value={rule.basis}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { basis: e.target.value as PriceRuleBasis })}
                        >
                          {Object.entries(PRICE_RULE_BASIS_LABELS).map(([value, label]) => (
                            <option key={value} value={value}>
                              {label}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        {isUnitBound ? (
                          <select
                            aria-label={`Eenheid voor ${rule.name}`}
                            value={rule.unitTypeId ?? ''}
                            disabled={!canManage}
                            className={fieldError(rule.id, 'unitTypeId') ? 'rule-grid-cell-invalid' : undefined}
                            onChange={(e) => void saveRule(rule, { unitTypeId: e.target.value || null })}
                          >
                            <option value="">— Kies eenheid —</option>
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
                          aria-label={`Zone voor ${rule.name}`}
                          value={rule.zoneId ?? ''}
                          disabled={!canManage}
                          onChange={(e) => void saveRule(rule, { zoneId: e.target.value || null })}
                        >
                          <option value="">— Alle —</option>
                          {zones.map((zone) => (
                            <option key={zone.id} value={zone.id}>
                              {zone.code}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <input
                          aria-label={`Prioriteit voor ${rule.name}`}
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
                            aria-label={`Prijs voor ${rule.name}`}
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
                          aria-label={`Basisbedrag voor ${rule.name}`}
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
                          aria-label={`Minimum voor ${rule.name}`}
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
                          aria-label={`Maximum voor ${rule.name}`}
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
                      <td colSpan={5}>—</td>
                      <td>
                        <input
                          aria-label={`Geldig van voor ${rule.name}`}
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
                          aria-label={`Geldig tot voor ${rule.name}`}
                          type="date"
                          defaultValue={rule.effectiveUntil ?? ''}
                          disabled={!canManage}
                          onBlur={(e) => {
                            const value = e.target.value || null
                            if (value !== rule.effectiveUntil) void saveRule(rule, { effectiveUntil: value })
                          }}
                        />
                      </td>
                      {canManage && (
                        <td className="issued-items-row-actions">
                          <button type="button" className="issued-items-link" onClick={() => void duplicateRule(rule)}>
                            Dupliceren
                          </button>
                          <button
                            type="button"
                            className="issued-items-link issued-items-link-danger"
                            onClick={() => setDeleteTarget(rule)}
                          >
                            Verwijderen
                          </button>
                        </td>
                      )}
                    </tr>
                    {expanded &&
                      rule.brackets.map((bracket, index) => (
                        // bracket.id is server-assigned and regenerated on every save; index keeps
                        // the key stable/unique even for a not-yet-persisted or id-less bracket.
                        <tr key={bracket.id ?? `${rule.id}-bracket-${index}`} className="rule-grid-bracket-row">
                          <td>↳ Staffel {bracketRangeLabel(bracket)}</td>
                          <td colSpan={4}>—</td>
                          <td>
                            <input
                              aria-label={`Staffel ${index + 1} van ${rule.name} prijs`}
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
                              aria-label={`Staffel ${index + 1} van ${rule.name} prijs per extra eenheid`}
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
                          <td colSpan={3}>—</td>
                          <td>
                            <input
                              aria-label={`Staffel ${index + 1} van ${rule.name} van`}
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
                              aria-label={`Staffel ${index + 1} van ${rule.name} tot`}
                              type="number"
                              step="0.01"
                              placeholder="open"
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
                              aria-label={`Staffel ${index + 1} van ${rule.name} gewicht tot (kg)`}
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
                              aria-label={`Staffel ${index + 1} van ${rule.name} volume tot (m³)`}
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
                              aria-label={`Staffel ${index + 1} van ${rule.name} ldm tot`}
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
                          <td colSpan={2}>—</td>
                          {canManage && (
                            <td className="issued-items-row-actions">
                              <button
                                type="button"
                                className="issued-items-link issued-items-link-danger"
                                onClick={() => setBracketDeleteTarget({ rule, index })}
                              >
                                Verwijderen
                              </button>
                            </td>
                          )}
                        </tr>
                      ))}
                    {expanded && canManage && (
                      <tr className="rule-grid-bracket-row">
                        <td colSpan={18}>
                          <button type="button" className="issued-items-link" onClick={() => addBracket(rule)}>
                            + Staffelrij
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
          title="Prijsregel verwijderen"
          message={`Weet je zeker dat je "${deleteTarget.name}" wilt verwijderen? Bestaande orders behouden hun prijssnapshot.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {bracketDeleteTarget && (
        <ConfirmDialog
          title="Staffelrij verwijderen"
          message={`Weet je zeker dat je staffel ${bracketDeleteTarget.index + 1} van "${bracketDeleteTarget.rule.name}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleConfirmRemoveBracket}
          onCancel={() => setBracketDeleteTarget(null)}
        />
      )}
    </div>
  )
}
