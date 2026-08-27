import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import { formatServiceValue } from '../../tarification/serviceValueFormat'
import type { SurchargeKind } from '../../tarification/types'
import {
  BRACKET_SELECTION_MODE_LABELS,
  PRICE_RULE_BASIS_LABELS,
  PRIMARY_BASIS_LABELS,
  createPriceRule,
  createPricingAgreement,
  deletePriceRule,
  deletePricingAgreement,
  getAgreementAssignments,
  getCustomerPricingConfig,
  listPriceRules,
  listPricingAgreements,
  listPricingZones,
  listUnitTypeSettings,
  saveCustomerPricingConfig,
  updatePriceRule,
  updatePricingAgreement,
  type BracketSelectionMode,
  type CustomerPricingConfig,
  type PriceRule,
  type PriceRuleBasis,
  type PriceRuleBracketInput,
  type PricingAgreement,
  type PricingAgreementModifierInput,
  type PricingAssignment,
  type PricingZone,
  type UnitTypeSettings,
} from '../../tarification/api/pricingApi'

interface CustomerUnitPricingPanelProps {
  customerId: string
}

interface RuleDraft {
  rule: PriceRule | null
  name: string
  unitTypeId: string
  basis: PriceRuleBasis
  zoneId: string
  agreementId: string
  effectiveFrom: string
  effectiveUntil: string
  unitPrice: string
  minimumAmount: string
  maximumAmount: string
  baseAmount: string
  priority: string
  minimumQuantity: string
  quantityRoundingStep: string
  oversizeLengthCm: string
  oversizeWidthCm: string
  oversizeBillableFactor: string
  /** QuantityBracket only: how the brackets combine into an amount. */
  bracketMode: BracketSelectionMode
  /** User-toggled visibility of the weight/volume/ldm columns (also shown when a row has a value). */
  showBracketDimensions: boolean
  brackets: {
    from: string
    to: string
    price: string
    extra: string
    weightToKg: string
    volumeToM3: string
    loadingMetersTo: string
  }[]
}

/** Maps a stored basis onto the editor's primary "Prijsbasis" choice (spec §10). */
const toPrimarySelectValue = (basis: PriceRuleBasis): string =>
  basis === 'PerUnit' || basis === 'QuantityBracket' ? 'unit' : basis

interface ModifierDraft {
  name: string
  countryCode: string
  zoneId: string
  mode: 'Percent' | 'Fixed'
  value: string
}

interface AgreementDraft {
  agreement: PricingAgreement | null
  name: string
  effectiveFrom: string
  effectiveUntil: string
  minimumAmount: string
  maximumAmount: string
  notes: string
  isShared: boolean
  surcharges: { name: string; kind: SurchargeKind; value: string }[]
  /** Set => this table is derived from another (shared/general) table; see spec §9. */
  baseAgreementId: string
  modifiers: ModifierDraft[]
}

/** A shared (reusable) rate table this customer is assigned to, with its adjustment. */
interface AssignedSharedAgreement {
  agreement: PricingAgreement
  assignment: PricingAssignment
}

function assignmentAdjustmentLabel(t: TranslateFn, assignment: PricingAssignment): string {
  const parts: string[] = []
  if (assignment.percentAdjustment !== null) {
    parts.push(`${assignment.percentAdjustment > 0 ? '+' : ''}${assignment.percentAdjustment}%`)
  }
  if (assignment.fixedAdjustment !== null) {
    parts.push(`${assignment.fixedAdjustment > 0 ? '+' : ''}€ ${assignment.fixedAdjustment.toFixed(2)}`)
  }
  return parts.length > 0 ? parts.join(', ') : t('customers.pricing.noAdjustment')
}

const today = () => new Date().toISOString().slice(0, 10)

/** Swaps the item at `from` with its neighbour at `to`; out-of-range is a no-op (edge buttons). */
function moveItem<T>(list: T[], from: number, to: number): T[] {
  if (to < 0 || to >= list.length) return list
  const next = [...list]
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item)
  return next
}

function ruleValueSummary(t: TranslateFn, rule: PriceRule): string {
  if (rule.brackets.length > 0) return t('customers.pricing.bracketCount', { count: rule.brackets.length })
  const parts: string[] = []
  if (rule.baseAmount !== null) parts.push(t('customers.pricing.baseAmountSummary', { amount: rule.baseAmount.toFixed(2) }))
  if (rule.unitPrice !== null) parts.push(`€ ${rule.unitPrice.toFixed(2)}`)
  if (rule.minimumAmount !== null) parts.push(t('customers.pricing.minAmountSummary', { amount: rule.minimumAmount.toFixed(2) }))
  return parts.join(', ') || '—'
}

/**
 * The customer's commercial tariff overview (spec §13): pricing agreements (tarievenkaarten),
 * current prices, scheduled future versions and price history, plus service-option prices.
 * Versioning happens via effective windows — old versions are never overwritten.
 */
export function CustomerUnitPricingPanel({ customerId }: CustomerUnitPricingPanelProps) {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [config, setConfig] = useState<CustomerPricingConfig | null>(null)
  const [rules, setRules] = useState<PriceRule[]>([])
  const [agreements, setAgreements] = useState<PricingAgreement[]>([])
  const [sharedAssigned, setSharedAssigned] = useState<AssignedSharedAgreement[]>([])
  // Company-wide/shared tables (CustomerId null) — the only valid "Afgeleid van" (base table) picks.
  const [baseTableOptions, setBaseTableOptions] = useState<PricingAgreement[]>([])
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<RuleDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [agreementDraft, setAgreementDraft] = useState<AgreementDraft | null>(null)
  const [agreementError, setAgreementError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<PriceRule | null>(null)
  const [deleteAgreementTarget, setDeleteAgreementTarget] = useState<PricingAgreement | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([
      getCustomerPricingConfig(customerId),
      listPriceRules(customerId),
      listPricingAgreements(customerId).catch(() => [] as PricingAgreement[]),
      // Company-wide + shared tables (CustomerId null); shared ones need an assignment check below.
      listPricingAgreements().catch(() => [] as PricingAgreement[]),
      listUnitTypeSettings().catch(() => [] as UnitTypeSettings[]),
      listPricingZones().catch(() => [] as PricingZone[]),
    ])
      .then(async ([configData, ruleData, agreementData, companyWideData, unitData, zoneData]) => {
        setConfig(configData)
        setRules(ruleData)
        setAgreements(agreementData)
        setBaseTableOptions(companyWideData)
        setUnits(unitData)
        setZones(zoneData)
        setLoadError(null)

        const sharedTables = companyWideData.filter((a) => a.isShared)
        const assignmentLists = await Promise.all(
          sharedTables.map((a) => getAgreementAssignments(a.id).catch(() => [] as PricingAssignment[])),
        )
        setSharedAssigned(
          sharedTables
            .map((agreement, index) => {
              const assignment = assignmentLists[index].find((a) => a.customerId === customerId)
              return assignment ? { agreement, assignment } : null
            })
            .filter((x): x is AssignedSharedAgreement => x !== null),
        )
      })
      .catch(() => setLoadError(t('customers.pricing.loadFailed')))
  }, [customerId, canView, t])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return null
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!config) return <p className="placeholder-text">{t('customers.pricing.loading')}</p>

  const now = today()
  const currentRules = rules.filter(
    (r) => r.isActive && r.effectiveFrom <= now && (r.effectiveUntil === null || r.effectiveUntil >= now),
  )
  const futureRules = rules.filter((r) => r.isActive && r.effectiveFrom > now)
  const historyRules = rules.filter((r) => !r.isActive || (r.effectiveUntil !== null && r.effectiveUntil < now))

  async function saveServiceOverride(
    serviceOptionId: string,
    patch: Partial<{
      value: number | null
      disabled: boolean
      effectiveFrom: string | null
      effectiveUntil: string | null
      autoApplyOverride: boolean | null
    }>,
    reset = false,
  ) {
    if (!config) return
    try {
      const saved = await saveCustomerPricingConfig(customerId, {
        units: config.preferredUnits.map((u) => ({
          unitTypeId: u.unitTypeId,
          sortOrder: u.sortOrder,
          customerLabel: u.customerLabel,
          ediCode: u.ediCode,
          excelCode: u.excelCode,
          isFavourite: u.isFavourite,
        })),
        optionPrices: config.serviceOptions.map((o) => {
          if (o.serviceOptionId !== serviceOptionId) {
            return {
              serviceOptionId: o.serviceOptionId,
              value: o.customerValue,
              disabled: o.disabled,
              minimumAmount: o.minimumAmount,
              invoiceDescription: o.invoiceDescription,
              effectiveFrom: o.effectiveFrom,
              effectiveUntil: o.effectiveUntil,
              autoApplyOverride: o.autoApplyOverride,
            }
          }

          if (reset) {
            // "Algemene waarde opnieuw gebruiken": drop the whole override row.
            return { serviceOptionId: o.serviceOptionId, value: null, disabled: false, autoApplyOverride: null }
          }

          return {
            serviceOptionId: o.serviceOptionId,
            value: patch.value !== undefined ? patch.value : o.customerValue,
            disabled: patch.disabled !== undefined ? patch.disabled : o.disabled,
            minimumAmount: o.minimumAmount,
            invoiceDescription: o.invoiceDescription,
            effectiveFrom: patch.effectiveFrom !== undefined ? patch.effectiveFrom : o.effectiveFrom,
            effectiveUntil: patch.effectiveUntil !== undefined ? patch.effectiveUntil : o.effectiveUntil,
            autoApplyOverride: patch.autoApplyOverride !== undefined ? patch.autoApplyOverride : o.autoApplyOverride,
          }
        }),
      })
      setConfig(saved)
      showSuccess(reset ? t('customers.pricing.overrideReset') : t('customers.pricing.overrideSaved'))
    } catch (err) {
      showError(describeApiError(err, t('customers.pricing.overrideSaveFailed')).message)
    }
  }

  function openDraft(rule: PriceRule | null) {
    setDraftError(null)
    setDraft(
      rule
        ? {
            rule,
            name: rule.name,
            unitTypeId: rule.unitTypeId ?? '',
            basis: rule.basis,
            zoneId: rule.zoneId ?? '',
            agreementId: rule.agreementId ?? '',
            effectiveFrom: rule.effectiveFrom,
            effectiveUntil: rule.effectiveUntil ?? '',
            unitPrice: rule.unitPrice !== null ? String(rule.unitPrice) : '',
            minimumAmount: rule.minimumAmount !== null ? String(rule.minimumAmount) : '',
            maximumAmount: rule.maximumAmount !== null ? String(rule.maximumAmount) : '',
            baseAmount: rule.baseAmount !== null ? String(rule.baseAmount) : '',
            priority: String(rule.priority),
            minimumQuantity: rule.minimumQuantity !== null ? String(rule.minimumQuantity) : '',
            quantityRoundingStep: rule.quantityRoundingStep !== null ? String(rule.quantityRoundingStep) : '',
            oversizeLengthCm: rule.oversizeLengthCm !== null ? String(rule.oversizeLengthCm) : '',
            oversizeWidthCm: rule.oversizeWidthCm !== null ? String(rule.oversizeWidthCm) : '',
            oversizeBillableFactor: rule.oversizeBillableFactor !== null ? String(rule.oversizeBillableFactor) : '',
            bracketMode: rule.bracketMode,
            showBracketDimensions: rule.brackets.some(
              (b) => b.weightToKg !== null || b.volumeToM3 !== null || b.loadingMetersTo !== null,
            ),
            brackets: rule.brackets.map((b) => ({
              from: String(b.fromQuantity),
              to: b.toQuantity !== null ? String(b.toQuantity) : '',
              price: String(b.price),
              extra: b.pricePerExtraUnit !== null ? String(b.pricePerExtraUnit) : '',
              weightToKg: b.weightToKg !== null ? String(b.weightToKg) : '',
              volumeToM3: b.volumeToM3 !== null ? String(b.volumeToM3) : '',
              loadingMetersTo: b.loadingMetersTo !== null ? String(b.loadingMetersTo) : '',
            })),
          }
        : {
            rule: null,
            name: '',
            unitTypeId: '',
            basis: 'QuantityBracket',
            zoneId: '',
            agreementId: '',
            effectiveFrom: today(),
            effectiveUntil: '',
            unitPrice: '',
            minimumAmount: '',
            maximumAmount: '',
            baseAmount: '',
            priority: '0',
            minimumQuantity: '',
            quantityRoundingStep: '',
            oversizeLengthCm: '',
            oversizeWidthCm: '',
            oversizeBillableFactor: '',
            bracketMode: 'Absolute',
            showBracketDimensions: false,
            brackets: [{ from: '1', to: '', price: '', extra: '', weightToKg: '', volumeToM3: '', loadingMetersTo: '' }],
          },
    )
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    const usesBrackets = draft.basis === 'QuantityBracket' || draft.basis === 'WeightBracket' || draft.basis === 'PerStop'
    const brackets: PriceRuleBracketInput[] = draft.brackets
      .filter((b) => b.from.trim() !== '')
      .map((b) => ({
        fromQuantity: Number(b.from),
        toQuantity: b.to.trim() === '' ? null : Number(b.to),
        price: Number(b.price) || 0,
        pricePerExtraUnit: b.extra.trim() === '' ? null : Number(b.extra),
        weightToKg: b.weightToKg.trim() === '' ? null : Number(b.weightToKg),
        volumeToM3: b.volumeToM3.trim() === '' ? null : Number(b.volumeToM3),
        loadingMetersTo: b.loadingMetersTo.trim() === '' ? null : Number(b.loadingMetersTo),
      }))
    const unitBound = draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket' || draft.basis === 'Hourly'
    setBusy(true)
    try {
      const input = {
        customerId,
        unitTypeId: unitBound ? draft.unitTypeId || null : draft.rule?.unitTypeId ?? null,
        basis: draft.basis,
        zoneId: draft.zoneId || null,
        agreementId: draft.agreementId || null,
        name: draft.name.trim(),
        effectiveFrom: draft.effectiveFrom,
        effectiveUntil: draft.effectiveUntil || null,
        isActive: true,
        unitPrice: draft.unitPrice.trim() === '' ? null : Number(draft.unitPrice),
        minimumAmount: draft.minimumAmount.trim() === '' ? null : Number(draft.minimumAmount),
        maximumAmount: draft.maximumAmount.trim() === '' ? null : Number(draft.maximumAmount),
        bracketMode: draft.basis === 'QuantityBracket' ? draft.bracketMode : 'Absolute',
        baseAmount: draft.baseAmount.trim() === '' ? null : Number(draft.baseAmount),
        priority: Number(draft.priority) || 0,
        minimumQuantity: draft.basis === 'Hourly' && draft.minimumQuantity.trim() !== '' ? Number(draft.minimumQuantity) : null,
        quantityRoundingStep: draft.basis === 'Hourly' && draft.quantityRoundingStep.trim() !== '' ? Number(draft.quantityRoundingStep) : null,
        oversizeLengthCm: draft.oversizeLengthCm.trim() === '' ? null : Number(draft.oversizeLengthCm),
        oversizeWidthCm: draft.oversizeWidthCm.trim() === '' ? null : Number(draft.oversizeWidthCm),
        oversizeBillableFactor: draft.oversizeBillableFactor.trim() === '' ? null : Number(draft.oversizeBillableFactor),
        brackets: usesBrackets && brackets.length > 0 ? brackets : null,
      }
      if (draft.rule) {
        await updatePriceRule(draft.rule.id, input)
        showSuccess(t('customers.pricing.ruleUpdated'))
      } else {
        await createPriceRule(input)
        showSuccess(t('customers.pricing.ruleAdded'))
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, t('customers.pricing.ruleSaveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await deletePriceRule(target.id)
      showSuccess(t('customers.pricing.ruleRemoved'))
      reload()
    } catch (err) {
      showError(describeApiError(err, t('customers.pricing.ruleRemoveFailed')).message)
    }
  }

  function openAgreementDraft(agreement: PricingAgreement | null) {
    setAgreementError(null)
    setAgreementDraft(
      agreement
        ? {
            agreement,
            name: agreement.name,
            effectiveFrom: agreement.effectiveFrom,
            effectiveUntil: agreement.effectiveUntil ?? '',
            minimumAmount: agreement.minimumAmount !== null ? String(agreement.minimumAmount) : '',
            maximumAmount: agreement.maximumAmount !== null ? String(agreement.maximumAmount) : '',
            notes: agreement.notes ?? '',
            isShared: agreement.isShared,
            surcharges: agreement.surcharges.map((s) => ({ name: s.name, kind: s.kind, value: String(s.value) })),
            baseAgreementId: agreement.baseAgreementId ?? '',
            modifiers: agreement.modifiers.map((m) => ({
              name: m.name,
              countryCode: m.countryCode ?? '',
              zoneId: m.zoneId ?? '',
              mode: m.fixedAmount !== null ? 'Fixed' : 'Percent',
              value: String(m.fixedAmount !== null ? m.fixedAmount : (m.percent ?? '')),
            })),
          }
        : {
            agreement: null,
            name: '',
            effectiveFrom: today(),
            effectiveUntil: '',
            minimumAmount: '',
            maximumAmount: '',
            notes: '',
            isShared: false,
            surcharges: [],
            baseAgreementId: '',
            modifiers: [],
          },
    )
  }

  async function submitAgreement(event: FormEvent) {
    event.preventDefault()
    if (!agreementDraft) return
    setBusy(true)
    try {
      const input = {
        // A reusable table is never tied to one customer — checking the box detaches it.
        customerId: agreementDraft.isShared ? null : customerId,
        name: agreementDraft.name.trim(),
        effectiveFrom: agreementDraft.effectiveFrom,
        effectiveUntil: agreementDraft.effectiveUntil || null,
        isActive: true,
        minimumAmount: agreementDraft.minimumAmount.trim() === '' ? null : Number(agreementDraft.minimumAmount),
        maximumAmount: agreementDraft.maximumAmount.trim() === '' ? null : Number(agreementDraft.maximumAmount),
        isShared: agreementDraft.isShared,
        notes: agreementDraft.notes.trim() || null,
        surcharges: agreementDraft.surcharges
          .filter((s) => s.name.trim() !== '')
          .map((s) => ({ name: s.name.trim(), kind: s.kind, value: Number(s.value) || 0 })),
        baseAgreementId: agreementDraft.baseAgreementId || null,
        modifiers: agreementDraft.baseAgreementId
          ? agreementDraft.modifiers
              .filter((m) => m.name.trim() !== '')
              .map((m, index): PricingAgreementModifierInput => ({
                sequence: index + 1,
                name: m.name.trim(),
                countryCode: m.countryCode.trim() || null,
                zoneId: m.zoneId || null,
                percent: m.mode === 'Percent' ? Number(m.value) || 0 : null,
                fixedAmount: m.mode === 'Fixed' ? Number(m.value) || 0 : null,
              }))
          : null,
      }
      if (agreementDraft.agreement) {
        await updatePricingAgreement(agreementDraft.agreement.id, input)
        showSuccess(t('customers.pricing.agreementUpdated'))
      } else {
        await createPricingAgreement(input)
        showSuccess(t('customers.pricing.agreementAdded'))
      }
      setAgreementDraft(null)
      reload()
    } catch (err) {
      setAgreementError(describeApiError(err, t('customers.pricing.agreementSaveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDeleteAgreement() {
    if (!deleteAgreementTarget) return
    const target = deleteAgreementTarget
    setDeleteAgreementTarget(null)
    try {
      await deletePricingAgreement(target.id)
      showSuccess(t('customers.pricing.agreementRemoved'))
      reload()
    } catch (err) {
      showError(describeApiError(err, t('customers.pricing.agreementRemoveFailed')).message)
    }
  }

  const usesBrackets = draft?.basis === 'QuantityBracket' || draft?.basis === 'WeightBracket' || draft?.basis === 'PerStop'
  // Keep the simple tables clean: the dimension columns only appear once toggled on, or already
  // hold a value (e.g. reopening a carrier-table rule that uses them).
  const showBracketDimensions =
    !!draft?.showBracketDimensions ||
    (draft?.brackets.some((b) => b.weightToKg.trim() !== '' || b.volumeToM3.trim() !== '' || b.loadingMetersTo.trim() !== '') ?? false)
  const pricingUnits = units.filter((u) => u.isActive && u.allowForPricing)
  const priceLabelByBasis: Partial<Record<PriceRuleBasis, string>> = {
    PerUnit: t('customers.pricing.priceLabel.PerUnit'),
    Hourly: t('customers.pricing.priceLabel.Hourly'),
    Fixed: t('customers.pricing.priceLabel.Fixed'),
    PerKm: t('customers.pricing.priceLabel.PerKm'),
    PerLoadingMeter: t('customers.pricing.priceLabel.PerLoadingMeter'),
    PerVolume: t('customers.pricing.priceLabel.PerVolume'),
    PerStop: t('customers.pricing.priceLabel.PerStop'),
    PerPallet: t('customers.pricing.priceLabel.PerPallet'),
    PerTon: t('customers.pricing.priceLabel.PerTon'),
  }

  const rulesTable = (list: PriceRule[]) => (
    <table className="issued-items-table">
      <thead>
        <tr>
          <th>{t('customers.pricing.columnName')}</th>
          <th>{t('customers.pricing.columnUnit')}</th>
          <th>{t('customers.pricing.columnBasis')}</th>
          <th>{t('customers.pricing.columnZone')}</th>
          <th>{t('customers.pricing.columnValue')}</th>
          <th>{t('customers.pricing.columnAgreement')}</th>
          <th>{t('customers.pricing.columnValidity')}</th>
          {canManage && <th aria-label={t('customers.pricing.actionsAria')} />}
        </tr>
      </thead>
      <tbody>
        {list.map((rule) => (
          <tr key={rule.id}>
            <td>{rule.name}</td>
            <td>{rule.unitTypeName ?? '—'}</td>
            <td>{PRICE_RULE_BASIS_LABELS[rule.basis]}</td>
            <td>{rule.zoneName ?? t('customers.pricing.allZones')}</td>
            <td>{ruleValueSummary(t, rule)}</td>
            <td>{rule.agreementName ?? '—'}</td>
            <td>
              {rule.effectiveFrom}
              {rule.effectiveUntil ? ` – ${rule.effectiveUntil}` : ' →'}
              {!rule.isActive && <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>}
            </td>
            {canManage && (
              <td className="issued-items-row-actions">
                <button type="button" className="issued-items-link" onClick={() => openDraft(rule)}>
                  {t('ui.actions.edit')}
                </button>
                <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(rule)}>
                  {t('ui.actions.delete')}
                </button>
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  )

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('customers.pricing.agreementsTitle')}</h3>
        {canManage && <Button variant="secondary" onClick={() => openAgreementDraft(null)}>{t('customers.pricing.addAgreement')}</Button>}
      </div>
      {agreements.length === 0 && sharedAssigned.length === 0 && (
        <p className="placeholder-text">{t('customers.pricing.agreementsEmpty')}</p>
      )}
      {(agreements.length > 0 || sharedAssigned.length > 0) && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('customers.pricing.columnName')}</th>
              <th>{t('customers.pricing.columnValidity')}</th>
              <th>{t('customers.pricing.columnMinimum')}</th>
              <th>{t('customers.pricing.columnSurcharges')}</th>
              <th>{t('customers.pricing.columnNotes')}</th>
              {canManage && <th aria-label={t('customers.pricing.actionsAria')} />}
            </tr>
          </thead>
          <tbody>
            {sharedAssigned.map(({ agreement, assignment }) => (
              <tr key={`shared-${agreement.id}`}>
                <td>
                  <Link to={`/pricing/tables/${agreement.id}`} className="issued-items-link">
                    {agreement.name}
                  </Link>{' '}
                  <Badge tone="info">{t('customers.pricing.sharedTableBadge')}</Badge>
                </td>
                <td>
                  {agreement.effectiveFrom}
                  {agreement.effectiveUntil ? ` – ${agreement.effectiveUntil}` : ' →'}
                </td>
                <td>{agreement.minimumAmount !== null ? `€ ${agreement.minimumAmount.toFixed(2)}` : '—'}</td>
                <td>{assignmentAdjustmentLabel(t, assignment)}</td>
                <td>{agreement.notes ?? '—'}</td>
                {canManage && <td className="issued-items-row-actions">—</td>}
              </tr>
            ))}
            {agreements.map((agreement) => (
              <tr key={agreement.id}>
                <td>
                  <Link to={`/pricing/tables/${agreement.id}`} className="issued-items-link">
                    {agreement.name}
                  </Link>
                  {agreement.baseAgreementId && (
                    <Badge tone="info">{t('customers.pricing.derivedFromBadge', { name: agreement.baseAgreementName ?? '—' })}</Badge>
                  )}
                </td>
                <td>
                  {agreement.effectiveFrom}
                  {agreement.effectiveUntil ? ` – ${agreement.effectiveUntil}` : ' →'}
                </td>
                <td>{agreement.minimumAmount !== null ? `€ ${agreement.minimumAmount.toFixed(2)}` : '—'}</td>
                <td>
                  {agreement.surcharges.length === 0
                    ? '—'
                    : agreement.surcharges
                        .map((s) => `${s.name} (${s.kind === 'Percent' ? `${s.value}%` : `€ ${s.value.toFixed(2)}`})`)
                        .join(', ')}
                </td>
                <td>{agreement.notes ?? '—'}</td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button type="button" className="issued-items-link" onClick={() => openAgreementDraft(agreement)}>
                      {t('ui.actions.edit')}
                    </button>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setDeleteAgreementTarget(agreement)}
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

      <div className="customer-panel-header">
        <h3>{t('customers.pricing.currentPricesTitle')}</h3>
        {canManage && <Button onClick={() => openDraft(null)}>{t('customers.pricing.addRule')}</Button>}
      </div>
      {currentRules.length === 0 && <p className="placeholder-text">{t('customers.pricing.noCurrentRules')}</p>}
      {currentRules.length > 0 && rulesTable(currentRules)}

      {futureRules.length > 0 && (
        <>
          <h4>{t('customers.pricing.futurePricesTitle')}</h4>
          {rulesTable(futureRules)}
        </>
      )}

      {historyRules.length > 0 && (
        <details>
          <summary>{t('customers.pricing.historyTitle', { count: historyRules.length })}</summary>
          {rulesTable(historyRules)}
        </details>
      )}

      <h4>{t('customers.pricing.servicesTitle')}</h4>
      <p className="customer-form-muted">{t('customers.pricing.servicesOverrideWarning')}</p>
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>{t('customers.pricing.columnService')}</th>
            <th>{t('customers.pricing.columnGeneralPrice')}</th>
            <th>{t('customers.pricing.columnCustomerOverride')}</th>
            <th>{t('customers.pricing.columnValidity')}</th>
            <th>{t('customers.pricing.columnEffectivePrice')}</th>
            <th>{t('customers.pricing.columnSource')}</th>
            <th>{t('customers.pricing.columnAutoApply')}</th>
            {canManage && <th aria-label={t('customers.pricing.actionsAria')} />}
          </tr>
        </thead>
        <tbody>
          {config.serviceOptions.map((option) => {
            const hasOverride = option.customerValue !== null || option.disabled
              || option.effectiveFrom !== null || option.effectiveUntil !== null
            return (
              <tr key={option.serviceOptionId}>
                <td>
                  {option.name}
                  {option.customerValue !== null && !option.disabled && (
                    <div className="customer-form-muted" role="note">
                      {option.effectiveFrom
                        ? t('customers.pricing.overrideNoteFromDate', {
                            value: formatServiceValue(option.kind, option.defaultValue),
                          })
                        : t('customers.pricing.overrideNote', {
                            value: formatServiceValue(option.kind, option.defaultValue),
                          })}
                    </div>
                  )}
                  {option.disabled && (
                    <div className="customer-form-muted" role="note">
                      {t('customers.pricing.serviceDisabledNote')}
                    </div>
                  )}
                </td>
                <td>{formatServiceValue(option.kind, option.defaultValue)}</td>
                <td>
                  <input
                    aria-label={t('customers.pricing.overrideAria', { name: option.name })}
                    type="number"
                    step="0.01"
                    defaultValue={option.customerValue ?? ''}
                    placeholder={t('customers.pricing.noOverridePlaceholder')}
                    disabled={!canManage || option.disabled}
                    onBlur={(e) => {
                      const raw = e.target.value
                      const current = option.customerValue === null ? '' : String(option.customerValue)
                      if (raw !== current) {
                        void saveServiceOverride(option.serviceOptionId, { value: raw.trim() === '' ? null : Number(raw) })
                      }
                    }}
                  />
                  {canManage && (
                    <label className="tof-checkbox">
                      <input
                        type="checkbox"
                        checked={option.disabled}
                        onChange={(e) => void saveServiceOverride(option.serviceOptionId, { disabled: e.target.checked })}
                      />
                      {t('customers.pricing.disableForCustomer')}
                    </label>
                  )}
                </td>
                <td>
                  <input
                    aria-label={t('customers.pricing.overrideFromAria', { name: option.name })}
                    type="date"
                    defaultValue={option.effectiveFrom ?? ''}
                    disabled={!canManage || !hasOverride}
                    onBlur={(e) => {
                      const value = e.target.value || null
                      if (value !== option.effectiveFrom) void saveServiceOverride(option.serviceOptionId, { effectiveFrom: value })
                    }}
                  />
                  <input
                    aria-label={t('customers.pricing.overrideUntilAria', { name: option.name })}
                    type="date"
                    defaultValue={option.effectiveUntil ?? ''}
                    disabled={!canManage || !hasOverride}
                    onBlur={(e) => {
                      const value = e.target.value || null
                      if (value !== option.effectiveUntil) void saveServiceOverride(option.serviceOptionId, { effectiveUntil: value })
                    }}
                  />
                </td>
                <td>{option.disabled ? t('customers.pricing.disabledValue') : formatServiceValue(option.kind, option.effectiveValue)}</td>
                <td>{option.source}</td>
                <td>
                  <select
                    aria-label={t('customers.pricing.autoApplyAria', { name: option.name })}
                    value={option.autoApplyOverride === null ? 'inherit' : option.autoApplyOverride ? 'on' : 'off'}
                    disabled={!canManage}
                    onChange={(e) => {
                      const value = e.target.value
                      void saveServiceOverride(option.serviceOptionId, {
                        autoApplyOverride: value === 'inherit' ? null : value === 'on',
                      })
                    }}
                  >
                    <option value="inherit">
                      {t('customers.pricing.autoApplyDefault', {
                        state: option.effectiveAutoApply ? t('customers.pricing.autoApplyOnShort') : t('customers.pricing.autoApplyOffShort'),
                      })}
                    </option>
                    <option value="on">{t('customers.pricing.autoApplyOn')}</option>
                    <option value="off">{t('customers.pricing.autoApplyOff')}</option>
                  </select>
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    {hasOverride && (
                      <button
                        type="button"
                        className="issued-items-link"
                        onClick={() => void saveServiceOverride(option.serviceOptionId, {}, true)}
                      >
                        {t('customers.pricing.useGeneralValueAgain')}
                      </button>
                    )}
                  </td>
                )}
              </tr>
            )
          })}
        </tbody>
      </table>

      {draft && (
        <Modal
          title={draft.rule ? t('customers.pricing.editRuleTitle', { name: draft.rule.name }) : t('customers.pricing.newRuleTitle')}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="price-rule-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="price-rule-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.columnName')} htmlFor="pr-name" required>
                <input id="pr-name" value={draft.name} onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
              <FormField label={t('customers.pricing.priceBasisField')} htmlFor="pr-basis" hint={t('customers.pricing.priceBasisHint')}>
                <select
                  id="pr-basis"
                  value={toPrimarySelectValue(draft.basis)}
                  onChange={(e) => {
                    const value = e.target.value
                    setDraft((d) => (d ? { ...d, basis: value === 'unit' ? 'QuantityBracket' : (value as PriceRuleBasis) } : d))
                  }}
                >
                  {Object.entries(PRIMARY_BASIS_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                  {(draft.basis === 'PerPallet' || draft.basis === 'PerTon') && (
                    <option value={draft.basis}>{PRICE_RULE_BASIS_LABELS[draft.basis]}</option>
                  )}
                </select>
              </FormField>
            </div>
            {(draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket') && (
              <div className="issued-items-form-row">
                <FormField label={t('customers.pricing.methodField')} htmlFor="pr-method">
                  <select
                    id="pr-method"
                    value={draft.basis}
                    onChange={(e) => setDraft((d) => (d ? { ...d, basis: e.target.value as PriceRuleBasis } : d))}
                  >
                    <option value="QuantityBracket">{t('customers.pricing.methodQuantityBracket')}</option>
                    <option value="PerUnit">{t('customers.pricing.methodPerUnit')}</option>
                  </select>
                </FormField>
              </div>
            )}
            <div className="issued-items-form-row">
              {(draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket' || draft.basis === 'Hourly') && (
                <FormField
                  label={t('customers.pricing.columnUnit')}
                  htmlFor="pr-unit"
                  hint={draft.basis === 'Hourly' ? t('customers.pricing.unitHintHourly') : t('customers.pricing.unitHint')}
                >
                  <select id="pr-unit" value={draft.unitTypeId} onChange={(e) => setDraft((d) => (d ? { ...d, unitTypeId: e.target.value } : d))}>
                    <option value="">{t('customers.pricing.chooseUnitOption')}</option>
                    {pricingUnits.map((unit) => (
                      <option key={unit.id} value={unit.id}>
                        {unit.name}
                      </option>
                    ))}
                  </select>
                </FormField>
              )}
              <FormField label={t('customers.pricing.columnZone')} htmlFor="pr-zone" hint={t('customers.pricing.zoneHint')}>
                <select id="pr-zone" value={draft.zoneId} onChange={(e) => setDraft((d) => (d ? { ...d, zoneId: e.target.value } : d))}>
                  <option value="">{t('customers.pricing.allOption')}</option>
                  {zones.map((zone) => (
                    <option key={zone.id} value={zone.id}>
                      {zone.code} — {zone.name}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField
                label={t('customers.pricing.columnAgreement')}
                htmlFor="pr-agreement"
                hint={t('customers.pricing.agreementFieldHint')}
              >
                <select id="pr-agreement" value={draft.agreementId} onChange={(e) => setDraft((d) => (d ? { ...d, agreementId: e.target.value } : d))}>
                  <option value="">{t('customers.pricing.looseRuleOption')}</option>
                  {agreements.filter((agreement) => !agreement.baseAgreementId).map((agreement) => (
                    <option key={agreement.id} value={agreement.id}>
                      {agreement.name}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('customers.pricing.priorityField')} htmlFor="pr-priority" hint={t('customers.pricing.priorityHint')}>
                <input id="pr-priority" type="number" value={draft.priority} onChange={(e) => setDraft((d) => (d ? { ...d, priority: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.validFromField')} htmlFor="pr-from" required>
                <input id="pr-from" type="date" value={draft.effectiveFrom} onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))} />
              </FormField>
              <FormField label={t('customers.pricing.validUntilField')} htmlFor="pr-until" hint={t('customers.pricing.validUntilHint')}>
                <input id="pr-until" type="date" value={draft.effectiveUntil} onChange={(e) => setDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))} />
              </FormField>
            </div>
            {(!usesBrackets || draft.basis === 'PerStop') && (
              <div className="issued-items-form-row">
                <FormField
                  label={priceLabelByBasis[draft.basis] ?? t('customers.pricing.priceLabel.PerUnit')}
                  htmlFor="pr-price"
                  required={draft.basis !== 'PerStop'}
                >
                  <input id="pr-price" type="number" step="0.01" value={draft.unitPrice} onChange={(e) => setDraft((d) => (d ? { ...d, unitPrice: e.target.value } : d))} />
                </FormField>
                <FormField label={t('customers.pricing.minAmountField')} htmlFor="pr-min">
                  <input id="pr-min" type="number" step="0.01" value={draft.minimumAmount} onChange={(e) => setDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))} />
                </FormField>
                <FormField label={t('customers.pricing.maxAmountField')} htmlFor="pr-max" hint={t('customers.pricing.maxAmountHint')}>
                  <input id="pr-max" type="number" step="0.01" value={draft.maximumAmount} onChange={(e) => setDraft((d) => (d ? { ...d, maximumAmount: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {draft.basis === 'Hourly' && (
              <div className="issued-items-form-row">
                <FormField label={t('customers.pricing.minHoursField')} htmlFor="pr-minq" hint={t('customers.pricing.minHoursHint')}>
                  <input id="pr-minq" type="number" step="0.25" value={draft.minimumQuantity} onChange={(e) => setDraft((d) => (d ? { ...d, minimumQuantity: e.target.value } : d))} />
                </FormField>
                <FormField label={t('customers.pricing.roundingStepField')} htmlFor="pr-step" hint={t('customers.pricing.roundingStepHint')}>
                  <input id="pr-step" type="number" step="0.05" value={draft.quantityRoundingStep} onChange={(e) => setDraft((d) => (d ? { ...d, quantityRoundingStep: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {(draft.basis === 'PerKm' || draft.basis === 'PerLoadingMeter' || draft.basis === 'PerVolume' || draft.basis === 'PerStop') && (
              <div className="issued-items-form-row">
                <FormField label={t('customers.pricing.baseAmountField')} htmlFor="pr-base-inline" hint={t('customers.pricing.baseAmountInlineHint')}>
                  <input id="pr-base-inline" type="number" step="0.01" value={draft.baseAmount} onChange={(e) => setDraft((d) => (d ? { ...d, baseAmount: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {usesBrackets && (
              <fieldset className="issued-items-generate-dimension">
                <legend>
                  {draft.basis === 'WeightBracket'
                    ? t('customers.pricing.bracketsWeightLegend')
                    : draft.basis === 'PerStop'
                      ? t('customers.pricing.bracketsStopsLegend')
                      : t('customers.pricing.bracketsQuantityLegend')}
                </legend>
                {draft.basis === 'QuantityBracket' && (
                  <FormField
                    label={t('customers.pricing.bracketModeField')}
                    htmlFor="pr-bracket-mode"
                    hint={t('customers.pricing.bracketModeHint')}
                  >
                    <select
                      id="pr-bracket-mode"
                      value={draft.bracketMode}
                      onChange={(e) => setDraft((d) => (d ? { ...d, bracketMode: e.target.value as BracketSelectionMode } : d))}
                    >
                      {Object.entries(BRACKET_SELECTION_MODE_LABELS).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </FormField>
                )}
                <label className="tof-checkbox">
                  <input
                    type="checkbox"
                    checked={showBracketDimensions}
                    onChange={(e) => setDraft((d) => (d ? { ...d, showBracketDimensions: e.target.checked } : d))}
                  />
                  {t('customers.pricing.extraDimensionsToggle')}
                </label>
                {draft.brackets.map((bracket, index) => (
                  <div key={index} className="issued-items-form-row customer-rule-bracket">
                    <input aria-label={t('customers.pricing.bracketFromAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketFromPlaceholder')} value={bracket.from}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, from: e.target.value } : b)) } : d))} />
                    <input aria-label={t('customers.pricing.bracketToAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketToPlaceholder')} value={bracket.to}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, to: e.target.value } : b)) } : d))} />
                    <input aria-label={t('customers.pricing.bracketPriceAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketPricePlaceholder')} value={bracket.price}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, price: e.target.value } : b)) } : d))} />
                    <input aria-label={t('customers.pricing.bracketExtraAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketExtraPlaceholder')} value={bracket.extra}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, extra: e.target.value } : b)) } : d))} />
                    {showBracketDimensions && (
                      <>
                        <input aria-label={t('customers.pricing.bracketWeightAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketWeightPlaceholder')} value={bracket.weightToKg}
                          onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, weightToKg: e.target.value } : b)) } : d))} />
                        <input aria-label={t('customers.pricing.bracketVolumeAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketVolumePlaceholder')} value={bracket.volumeToM3}
                          onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, volumeToM3: e.target.value } : b)) } : d))} />
                        <input aria-label={t('customers.pricing.bracketLdmAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.bracketLdmPlaceholder')} value={bracket.loadingMetersTo}
                          onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, loadingMetersTo: e.target.value } : b)) } : d))} />
                      </>
                    )}
                    <Button variant="ghost" onClick={() => setDraft((d) => (d ? { ...d, brackets: d.brackets.filter((_, i) => i !== index) } : d))}>
                      {t('ui.actions.delete')}
                    </Button>
                  </div>
                ))}
                <Button
                  variant="secondary"
                  onClick={() =>
                    setDraft((d) =>
                      d
                        ? {
                            ...d,
                            brackets: [
                              ...d.brackets,
                              { from: '', to: '', price: '', extra: '', weightToKg: '', volumeToM3: '', loadingMetersTo: '' },
                            ],
                          }
                        : d,
                    )
                  }
                >
                  {t('customers.pricing.addBracket')}
                </Button>
              </fieldset>
            )}
            {(draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket') && (
            <details>
              <summary>{t('customers.pricing.advancedSummary')}</summary>
              <div className="issued-items-form-row">
                <FormField label={t('customers.pricing.baseAmountField')} htmlFor="pr-base" hint={t('customers.pricing.baseAmountHint')}>
                  <input id="pr-base" type="number" step="0.01" value={draft.baseAmount} onChange={(e) => setDraft((d) => (d ? { ...d, baseAmount: e.target.value } : d))} />
                </FormField>
              </div>
              <div className="issued-items-form-row">
                <FormField label={t('customers.pricing.oversizeLengthField')} htmlFor="pr-ovl">
                  <input id="pr-ovl" type="number" step="0.01" value={draft.oversizeLengthCm} onChange={(e) => setDraft((d) => (d ? { ...d, oversizeLengthCm: e.target.value } : d))} />
                </FormField>
                <FormField label={t('customers.pricing.oversizeWidthField')} htmlFor="pr-ovw">
                  <input id="pr-ovw" type="number" step="0.01" value={draft.oversizeWidthCm} onChange={(e) => setDraft((d) => (d ? { ...d, oversizeWidthCm: e.target.value } : d))} />
                </FormField>
                <FormField label={t('customers.pricing.oversizeFactorField')} htmlFor="pr-ovf" hint={t('customers.pricing.oversizeFactorHint')}>
                  <input id="pr-ovf" type="number" step="0.5" value={draft.oversizeBillableFactor} onChange={(e) => setDraft((d) => (d ? { ...d, oversizeBillableFactor: e.target.value } : d))} />
                </FormField>
              </div>
            </details>
            )}
          </form>
        </Modal>
      )}

      {agreementDraft && (
        <Modal
          title={
            agreementDraft.agreement
              ? t('customers.pricing.editAgreementTitle', { name: agreementDraft.agreement.name })
              : t('customers.pricing.newAgreementTitle')
          }
          onClose={() => setAgreementDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setAgreementDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="pricing-agreement-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="pricing-agreement-form" className="issued-items-form" onSubmit={submitAgreement} noValidate>
            {agreementError && (
              <div className="issued-items-form-error" role="alert">
                {agreementError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.columnName')} htmlFor="pa-name" required hint={t('customers.pricing.agreementNameHint')}>
                <input id="pa-name" value={agreementDraft.name} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
              <FormField label={t('customers.pricing.minPerOrderField')} htmlFor="pa-min">
                <input id="pa-min" type="number" step="0.01" value={agreementDraft.minimumAmount} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))} />
              </FormField>
              <FormField label={t('customers.pricing.maxPerOrderField')} htmlFor="pa-max" hint={t('customers.pricing.maxAmountHint')}>
                <input id="pa-max" type="number" step="0.01" value={agreementDraft.maximumAmount} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, maximumAmount: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.validFromField')} htmlFor="pa-from" required>
                <input id="pa-from" type="date" value={agreementDraft.effectiveFrom} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))} />
              </FormField>
              <FormField label={t('customers.pricing.validUntilField')} htmlFor="pa-until" hint={t('customers.pricing.validUntilHint')}>
                <input id="pa-until" type="date" value={agreementDraft.effectiveUntil} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))} />
              </FormField>
            </div>
            {!agreementDraft.agreement && (
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={agreementDraft.isShared}
                  onChange={(e) => setAgreementDraft((d) => (d ? { ...d, isShared: e.target.checked } : d))}
                />
                {t('customers.pricing.reusableTableToggle')}
              </label>
            )}
            <FormField label={t('customers.pricing.internalNotesField')} htmlFor="pa-notes" hint={t('customers.pricing.internalNotesHint')}>
              <input id="pa-notes" value={agreementDraft.notes} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, notes: e.target.value } : d))} maxLength={2000} />
            </FormField>
            <fieldset className="issued-items-generate-dimension">
              <legend>{t('customers.pricing.derivedFromLegend')}</legend>
              <FormField
                label={t('customers.pricing.baseTableField')}
                htmlFor="pa-base"
                hint={t('customers.pricing.baseTableHint')}
              >
                <select
                  id="pa-base"
                  value={agreementDraft.baseAgreementId}
                  onChange={(e) => setAgreementDraft((d) => (d ? { ...d, baseAgreementId: e.target.value } : d))}
                >
                  <option value="">{t('customers.pricing.noBaseTableOption')}</option>
                  {baseTableOptions
                    .filter((a) => a.id !== agreementDraft.agreement?.id)
                    .map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.name}
                      </option>
                    ))}
                </select>
              </FormField>
              {agreementDraft.baseAgreementId && (
                <>
                  <p className="customer-form-muted" role="note">
                    {t('customers.pricing.derivedTableNote', {
                      name: baseTableOptions.find((a) => a.id === agreementDraft.baseAgreementId)?.name ?? '—',
                    })}
                  </p>
                  {agreementDraft.modifiers.map((modifier, index) => (
                    <div key={index} className="issued-items-form-row customer-rule-bracket">
                      <input
                        aria-label={t('customers.pricing.modifierNameAria', { index: index + 1 })}
                        placeholder={t('customers.pricing.modifierNamePlaceholder')}
                        value={modifier.name}
                        onChange={(e) =>
                          setAgreementDraft((d) =>
                            d ? { ...d, modifiers: d.modifiers.map((m, i) => (i === index ? { ...m, name: e.target.value } : m)) } : d,
                          )
                        }
                      />
                      <input
                        aria-label={t('customers.pricing.modifierCountryAria', { index: index + 1 })}
                        placeholder={t('customers.pricing.modifierCountryPlaceholder')}
                        maxLength={2}
                        value={modifier.countryCode}
                        onChange={(e) =>
                          setAgreementDraft((d) =>
                            d
                              ? {
                                  ...d,
                                  modifiers: d.modifiers.map((m, i) =>
                                    i === index ? { ...m, countryCode: e.target.value.toUpperCase() } : m,
                                  ),
                                }
                              : d,
                          )
                        }
                      />
                      <select
                        aria-label={t('customers.pricing.modifierZoneAria', { index: index + 1 })}
                        value={modifier.zoneId}
                        onChange={(e) =>
                          setAgreementDraft((d) =>
                            d ? { ...d, modifiers: d.modifiers.map((m, i) => (i === index ? { ...m, zoneId: e.target.value } : m)) } : d,
                          )
                        }
                      >
                        <option value="">{t('customers.pricing.allZonesOption')}</option>
                        {zones.map((zone) => (
                          <option key={zone.id} value={zone.id}>
                            {zone.code} — {zone.name}
                          </option>
                        ))}
                      </select>
                      <select
                        aria-label={t('customers.pricing.modifierKindAria', { index: index + 1 })}
                        value={modifier.mode}
                        onChange={(e) =>
                          setAgreementDraft((d) =>
                            d
                              ? {
                                  ...d,
                                  modifiers: d.modifiers.map((m, i) =>
                                    i === index ? { ...m, mode: e.target.value as 'Percent' | 'Fixed' } : m,
                                  ),
                                }
                              : d,
                          )
                        }
                      >
                        <option value="Percent">{t('customers.pricing.kindPercent')}</option>
                        <option value="Fixed">{t('customers.pricing.kindFixed')}</option>
                      </select>
                      <input
                        aria-label={t('customers.pricing.modifierValueAria', { index: index + 1 })}
                        type="number"
                        step="0.01"
                        placeholder={t('customers.pricing.valuePlaceholder')}
                        value={modifier.value}
                        onChange={(e) =>
                          setAgreementDraft((d) =>
                            d ? { ...d, modifiers: d.modifiers.map((m, i) => (i === index ? { ...m, value: e.target.value } : m)) } : d,
                          )
                        }
                      />
                      <Button
                        variant="ghost"
                        aria-label={t('customers.pricing.modifierUpAria', { index: index + 1 })}
                        disabled={index === 0}
                        onClick={() => setAgreementDraft((d) => (d ? { ...d, modifiers: moveItem(d.modifiers, index, index - 1) } : d))}
                      >
                        ↑
                      </Button>
                      <Button
                        variant="ghost"
                        aria-label={t('customers.pricing.modifierDownAria', { index: index + 1 })}
                        disabled={index === agreementDraft.modifiers.length - 1}
                        onClick={() => setAgreementDraft((d) => (d ? { ...d, modifiers: moveItem(d.modifiers, index, index + 1) } : d))}
                      >
                        ↓
                      </Button>
                      <Button
                        variant="ghost"
                        onClick={() => setAgreementDraft((d) => (d ? { ...d, modifiers: d.modifiers.filter((_, i) => i !== index) } : d))}
                      >
                        {t('ui.actions.delete')}
                      </Button>
                    </div>
                  ))}
                  <Button
                    variant="secondary"
                    onClick={() =>
                      setAgreementDraft((d) =>
                        d
                          ? { ...d, modifiers: [...d.modifiers, { name: '', countryCode: '', zoneId: '', mode: 'Percent', value: '' }] }
                          : d,
                      )
                    }
                  >
                    {t('customers.pricing.addModifier')}
                  </Button>
                </>
              )}
            </fieldset>
            <fieldset className="issued-items-generate-dimension">
              <legend>{t('customers.pricing.autoSurchargesLegend')}</legend>
              {agreementDraft.surcharges.map((surcharge, index) => (
                <div key={index} className="issued-items-form-row customer-rule-bracket">
                  <input aria-label={t('customers.pricing.surchargeNameAria', { index: index + 1 })} placeholder={t('customers.pricing.namePlaceholder')} value={surcharge.name}
                    onChange={(e) => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, name: e.target.value } : s)) } : d))} />
                  <select aria-label={t('customers.pricing.surchargeKindAria', { index: index + 1 })} value={surcharge.kind}
                    onChange={(e) => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, kind: e.target.value as 'Percent' | 'Fixed' } : s)) } : d))}>
                    <option value="Percent">{t('customers.pricing.kindPercent')}</option>
                    <option value="Fixed">{t('customers.pricing.kindFixed')}</option>
                  </select>
                  <input aria-label={t('customers.pricing.surchargeValueAria', { index: index + 1 })} type="number" step="0.01" placeholder={t('customers.pricing.valuePlaceholder')} value={surcharge.value}
                    onChange={(e) => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, value: e.target.value } : s)) } : d))} />
                  <Button variant="ghost" onClick={() => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.filter((_, i) => i !== index) } : d))}>
                    {t('ui.actions.delete')}
                  </Button>
                </div>
              ))}
              <Button variant="secondary" onClick={() => setAgreementDraft((d) => (d ? { ...d, surcharges: [...d.surcharges, { name: '', kind: 'Percent', value: '' }] } : d))}>
                {t('customers.pricing.addSurcharge')}
              </Button>
            </fieldset>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('customers.pricing.deleteRuleTitle')}
          message={t('customers.pricing.deleteRuleMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {deleteAgreementTarget && (
        <ConfirmDialog
          title={t('customers.pricing.deleteAgreementTitle')}
          message={t('customers.pricing.deleteAgreementMessage', { name: deleteAgreementTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDeleteAgreement}
          onCancel={() => setDeleteAgreementTarget(null)}
        />
      )}
    </section>
  )
}
