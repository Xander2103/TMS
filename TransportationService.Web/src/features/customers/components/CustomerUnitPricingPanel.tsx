import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { formatCurrency } from '../../../utils/numbers'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import { formatServiceValue } from '../../tarification/serviceValueFormat'
import { SURCHARGE_KIND_LABELS, type SurchargeKind } from '../../tarification/types'
import { getDieselSurcharge } from '../api/customerBillingConfigApi'
import type { CustomerDieselSurcharge } from '../types'
import {
  BRACKET_SELECTION_MODE_KEYS,
  PRICE_RULE_BASIS_KEYS,
  PRIMARY_BASIS_KEYS,
  createPriceRule,
  createPricingAgreement,
  deletePriceRule,
  deletePricingAgreement,
  getAgreementAssignments,
  getCustomerPricingConfig,
  listCustomerAgreements,
  listCustomerBracketOverrides,
  listPriceRules,
  listPricingAgreements,
  listPricingZones,
  listServiceOptions,
  listUnitTypeSettings,
  saveAgreementAssignments,
  saveCustomerPricingConfig,
  updatePriceRule,
  updatePricingAgreement,
  type BracketSelectionMode,
  type CustomerAgreementLink,
  type CustomerBracketOverrideRow,
  type CustomerOptionPriceInput,
  type CustomerPricingConfig,
  type CustomerServiceOptionPrice,
  type PriceRule,
  type PriceRuleBasis,
  type PriceRuleBracketInput,
  type PricingAgreement,
  type PricingAgreementModifierInput,
  type PricingZone,
  type ServiceOption,
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

/** Modal state for one service override ("toeslag") — explicit save, never blur-autosave. */
interface ServiceDraft {
  /** Null while the user still has to pick a service ("+ Toeslag toevoegen"). */
  serviceOptionId: string | null
  isNew: boolean
  value: string
  disabled: boolean
  minimumAmount: string
  invoiceDescription: string
  effectiveFrom: string
  effectiveUntil: string
  autoApply: 'inherit' | 'on' | 'off'
}

/** Modal state for the customer's ±% / fixed adjustment on one shared table. */
interface AssignmentDraft {
  link: CustomerAgreementLink
  percent: string
  fixed: string
  effectiveFrom: string
  effectiveUntil: string
  notes: string | null
}

function adjustmentLabel(t: TranslateFn, percent: number | null, fixed: number | null): string {
  const parts: string[] = []
  if (percent !== null) parts.push(`${percent > 0 ? '+' : ''}${percent}%`)
  if (fixed !== null) parts.push(`${fixed > 0 ? '+' : ''}${formatCurrency(fixed)}`)
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
  if (rule.baseAmount !== null) parts.push(t('customers.pricing.baseAmountSummary', { amount: formatCurrency(rule.baseAmount) }))
  if (rule.unitPrice !== null) parts.push(formatCurrency(rule.unitPrice))
  if (rule.minimumAmount !== null) parts.push(t('customers.pricing.minAmountSummary', { amount: formatCurrency(rule.minimumAmount) }))
  return parts.join(', ') || '—'
}

/** One row of the "Toeslagen & diensten" table: merged customer view + global service metadata. */
type ServiceRow = CustomerServiceOptionPrice & { meta: ServiceOption | undefined }

/**
 * Customer detail → "Tarieven & toeslagen": everything that determines what THIS customer pays.
 * Three sections, in reading order: (1) Tariefbasis — which rate tables price this customer,
 * (2) Toeslagen & diensten — services/surcharges on top of the base transport (incl. time-based
 * delivery surcharges), (3) Afwijkende prijzen — customer-only price rules. Unit/EDI mapping
 * lives in CustomerUnitsPanel (rendered at the bottom of the tab). All editing goes through
 * explicit modals — never blur-autosave (each save PUTs config; a blur-save's blast radius was
 * the whole config).
 */
export function CustomerUnitPricingPanel({ customerId }: CustomerUnitPricingPanelProps) {
  const { hasPermission } = useAuth()
  const { t, formatDate } = useLocale()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [config, setConfig] = useState<CustomerPricingConfig | null>(null)
  // Sprint 4A: the services table lists the tenant's GENERAL prices. Showing all of them made a
  // company-wide default look like a price agreed with this customer, so only real customer
  // deviations + auto-applied contract services are listed unless the user asks for the full list.
  const [showAllServices, setShowAllServices] = useState(false)
  const [rules, setRules] = useState<PriceRule[]>([])
  const [links, setLinks] = useState<CustomerAgreementLink[]>([])
  const [bracketOverrides, setBracketOverrides] = useState<CustomerBracketOverrideRow[]>([])
  // Own (customer-bound) agreements incl. surcharges/modifiers — the edit-modal source.
  const [agreements, setAgreements] = useState<PricingAgreement[]>([])
  // Company-wide/shared tables (CustomerId null) — the only valid "Afgeleid van" (base table) picks.
  const [baseTableOptions, setBaseTableOptions] = useState<PricingAgreement[]>([])
  const [serviceMeta, setServiceMeta] = useState<ServiceOption[]>([])
  const [diesel, setDiesel] = useState<CustomerDieselSurcharge | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<RuleDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [agreementDraft, setAgreementDraft] = useState<AgreementDraft | null>(null)
  const [agreementError, setAgreementError] = useState<string | null>(null)
  const [serviceDraft, setServiceDraft] = useState<ServiceDraft | null>(null)
  const [serviceError, setServiceError] = useState<string | null>(null)
  const [assignmentDraft, setAssignmentDraft] = useState<AssignmentDraft | null>(null)
  const [assignmentError, setAssignmentError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<PriceRule | null>(null)
  const [deleteAgreementTarget, setDeleteAgreementTarget] = useState<CustomerAgreementLink | null>(null)
  const [resetServiceTarget, setResetServiceTarget] = useState<ServiceRow | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([
      getCustomerPricingConfig(customerId),
      listCustomerAgreements(customerId).catch(() => [] as CustomerAgreementLink[]),
      listCustomerBracketOverrides(customerId).catch(() => [] as CustomerBracketOverrideRow[]),
      listPriceRules(customerId),
      listPricingAgreements(customerId).catch(() => [] as PricingAgreement[]),
      listPricingAgreements().catch(() => [] as PricingAgreement[]),
      listServiceOptions().catch(() => [] as ServiceOption[]),
      listUnitTypeSettings().catch(() => [] as UnitTypeSettings[]),
      listPricingZones().catch(() => [] as PricingZone[]),
      // Diesel is a billing-config read (customers.view); missing rights simply hide the card.
      getDieselSurcharge(customerId).catch(() => null),
    ])
      .then(([configData, linkData, overrideData, ruleData, ownAgreements, companyWideData, serviceData, unitData, zoneData, dieselData]) => {
        setConfig(configData)
        setLinks(linkData)
        setBracketOverrides(overrideData)
        setRules(ruleData)
        setAgreements(ownAgreements)
        setBaseTableOptions(companyWideData)
        setServiceMeta(serviceData)
        setUnits(unitData)
        setZones(zoneData)
        setDiesel(dieselData)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('customers.pricing.loadFailed')))
  }, [customerId, canView, t])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return null
  if (loadError) return <ErrorState message={loadError} />
  if (!config) return <LoadingState message={t('customers.pricing.loading')} />

  // A row is a customer DEVIATION when something was set for this customer specifically:
  // an own value, a deliberate switch-off, or an own validity window.
  const hasCustomerDeviation = (option: CustomerServiceOptionPrice) =>
    option.customerValue !== null || option.disabled || option.effectiveFrom !== null ||
    option.effectiveUntil !== null || option.autoApplyOverride !== null
  const metaById = new Map(serviceMeta.map((s) => [s.id, s]))
  const serviceRows: ServiceRow[] = config.serviceOptions.map((o) => ({ ...o, meta: metaById.get(o.serviceOptionId) }))
  const visibleServiceRows = showAllServices
    ? serviceRows
    : serviceRows.filter((o) => hasCustomerDeviation(o) || o.effectiveAutoApply)

  const now = today()
  const activeRules = rules.filter(
    (r) => r.isActive && r.effectiveFrom <= now && (r.effectiveUntil === null || r.effectiveUntil >= now),
  )
  const futureRules = rules.filter((r) => r.isActive && r.effectiveFrom > now)
  const activeAndPlannedRules = [...activeRules, ...futureRules]
  const historyRules = rules.filter((r) => !r.isActive || (r.effectiveUntil !== null && r.effectiveUntil < now))

  const validity = (from: string | null, until: string | null): string => {
    if (!from && !until) return '—'
    const fromLabel = from ? formatDate(from) : '…'
    return until ? `${fromLabel} – ${formatDate(until)}` : `${fromLabel} →`
  }

  async function saveServiceOverride(row: CustomerOptionPriceInput, successMessage: string) {
    if (!config) return false
    setBusy(true)
    try {
      // The backend leaves option rows that are ABSENT from the request untouched, so a
      // single-row save never clobbers other overrides; units however are a full replace.
      const saved = await saveCustomerPricingConfig(customerId, {
        units: config.preferredUnits.map((u) => ({
          unitTypeId: u.unitTypeId,
          sortOrder: u.sortOrder,
          customerLabel: u.customerLabel,
          ediCode: u.ediCode,
          excelCode: u.excelCode,
          isFavourite: u.isFavourite,
        })),
        optionPrices: [row],
      })
      setConfig(saved)
      showSuccess(successMessage)
      return true
    } catch (err) {
      showError(describeApiError(err, t('customers.pricing.overrideSaveFailed')).message)
      return false
    } finally {
      setBusy(false)
    }
  }

  function openServiceDraft(row: ServiceRow | null) {
    setServiceError(null)
    setServiceDraft(
      row
        ? {
            serviceOptionId: row.serviceOptionId,
            isNew: false,
            value: row.customerValue !== null ? String(row.customerValue) : '',
            disabled: row.disabled,
            minimumAmount: row.minimumAmount !== null ? String(row.minimumAmount) : '',
            invoiceDescription: row.invoiceDescription ?? '',
            effectiveFrom: row.effectiveFrom ?? '',
            effectiveUntil: row.effectiveUntil ?? '',
            autoApply: row.autoApplyOverride === null ? 'inherit' : row.autoApplyOverride ? 'on' : 'off',
          }
        : {
            serviceOptionId: null,
            isNew: true,
            value: '',
            disabled: false,
            minimumAmount: '',
            invoiceDescription: '',
            effectiveFrom: '',
            effectiveUntil: '',
            autoApply: 'inherit',
          },
    )
  }

  async function submitServiceDraft(event: FormEvent) {
    event.preventDefault()
    if (!serviceDraft) return
    if (!serviceDraft.serviceOptionId) {
      setServiceError(t('customers.pricing.chooseServiceError'))
      return
    }
    const ok = await saveServiceOverride(
      {
        serviceOptionId: serviceDraft.serviceOptionId,
        value: serviceDraft.value.trim() === '' ? null : Number(serviceDraft.value),
        disabled: serviceDraft.disabled,
        minimumAmount: serviceDraft.minimumAmount.trim() === '' ? null : Number(serviceDraft.minimumAmount),
        invoiceDescription: serviceDraft.invoiceDescription.trim() === '' ? null : serviceDraft.invoiceDescription.trim(),
        effectiveFrom: serviceDraft.effectiveFrom || null,
        effectiveUntil: serviceDraft.effectiveUntil || null,
        autoApplyOverride: serviceDraft.autoApply === 'inherit' ? null : serviceDraft.autoApply === 'on',
      },
      t('customers.pricing.overrideSaved'),
    )
    if (ok) setServiceDraft(null)
  }

  async function handleResetService() {
    if (!resetServiceTarget) return
    const target = resetServiceTarget
    setResetServiceTarget(null)
    // An all-empty row deletes the override server-side ("Algemene waarde opnieuw gebruiken").
    await saveServiceOverride(
      { serviceOptionId: target.serviceOptionId, value: null, disabled: false, autoApplyOverride: null },
      t('customers.pricing.overrideReset'),
    )
  }

  function openAssignmentDraft(link: CustomerAgreementLink) {
    setAssignmentError(null)
    setAssignmentDraft({
      link,
      percent: link.assignmentPercentAdjustment !== null ? String(link.assignmentPercentAdjustment) : '',
      fixed: link.assignmentFixedAdjustment !== null ? String(link.assignmentFixedAdjustment) : '',
      effectiveFrom: link.assignmentEffectiveFrom ?? '',
      effectiveUntil: link.assignmentEffectiveUntil ?? '',
      notes: null,
    })
  }

  async function submitAssignmentDraft(event: FormEvent) {
    event.preventDefault()
    if (!assignmentDraft) return
    setBusy(true)
    try {
      // The assignments endpoint replaces the full list per table, so re-read it first and only
      // swap this customer's row — other customers' assignments pass through untouched.
      const existing = await getAgreementAssignments(assignmentDraft.link.agreementId)
      const ownRow = existing.find((a) => a.customerId === customerId)
      const nextRows = existing.map((a) =>
        a.customerId === customerId
          ? {
              customerId,
              percentAdjustment: assignmentDraft.percent.trim() === '' ? null : Number(assignmentDraft.percent),
              fixedAdjustment: assignmentDraft.fixed.trim() === '' ? null : Number(assignmentDraft.fixed),
              effectiveFrom: assignmentDraft.effectiveFrom || null,
              effectiveUntil: assignmentDraft.effectiveUntil || null,
              notes: ownRow?.notes ?? null,
            }
          : {
              customerId: a.customerId,
              percentAdjustment: a.percentAdjustment,
              fixedAdjustment: a.fixedAdjustment,
              effectiveFrom: a.effectiveFrom,
              effectiveUntil: a.effectiveUntil,
              notes: a.notes,
            },
      )
      await saveAgreementAssignments(assignmentDraft.link.agreementId, nextRows)
      showSuccess(t('customers.pricing.assignmentSaved'))
      setAssignmentDraft(null)
      reload()
    } catch (err) {
      setAssignmentError(describeApiError(err, t('customers.pricing.assignmentSaveFailed')).message)
    } finally {
      setBusy(false)
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
    const usesBracketsSubmit = draft.basis === 'QuantityBracket' || draft.basis === 'WeightBracket' || draft.basis === 'PerStop'
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
        brackets: usesBracketsSubmit && brackets.length > 0 ? brackets : null,
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
      await deletePricingAgreement(target.agreementId)
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

  const zoneOptions: SearchableSelectOption[] = zones.map((zone) => ({
    value: zone.id,
    label: `${zone.code} — ${zone.name}`,
  }))
  const unitOptions: SearchableSelectOption[] = pricingUnits.map((unit) => ({ value: unit.id, label: unit.name }))
  const primaryBasisOptions: SearchableSelectOption[] = [
    ...Object.entries(PRIMARY_BASIS_KEYS).map(([value, key]) => ({ value, label: t(key) })),
    ...(draft && (draft.basis === 'PerPallet' || draft.basis === 'PerTon')
      ? [{ value: draft.basis, label: t(PRICE_RULE_BASIS_KEYS[draft.basis]) }]
      : []),
  ]
  const kindOptions: SearchableSelectOption[] = [
    { value: 'Percent', label: t('customers.pricing.kindPercent') },
    { value: 'Fixed', label: t('customers.pricing.kindFixed') },
  ]

  const plannedAdjustmentNote = (link: CustomerAgreementLink): string | null => {
    if (!link.plannedAdjustmentDate) return null
    const delta =
      link.plannedAdjustmentPercent !== null
        ? `${link.plannedAdjustmentPercent > 0 ? '+' : ''}${link.plannedAdjustmentPercent}%`
        : link.plannedAdjustmentAmountDelta !== null
          ? `${link.plannedAdjustmentAmountDelta > 0 ? '+' : ''}${formatCurrency(link.plannedAdjustmentAmountDelta)}`
          : ''
    return t('customers.pricing.plannedAdjustmentNote', { delta, date: formatDate(link.plannedAdjustmentDate) })
  }

  /** Human chips describing WHEN a service applies (time conditions, ADR, warehouses, validity). */
  const conditionChips = (row: ServiceRow): ReactNode => {
    const chips: string[] = []
    for (const cond of row.meta?.timeConditions ?? []) {
      const time = cond.timeOfDay ? cond.timeOfDay.slice(0, 5) : ''
      const scope =
        cond.stopScope === 'Loading'
          ? ` (${t('customers.pricing.condLoading')})`
          : cond.stopScope === 'Unloading'
            ? ` (${t('customers.pricing.condUnloading')})`
            : ''
      if (cond.kind === 'StopTimeBefore') chips.push(t('customers.pricing.condBefore', { time }) + scope)
      else if (cond.kind === 'StopTimeAfter') chips.push(t('customers.pricing.condAfter', { time }) + scope)
      else if (cond.kind === 'AppointmentRequired') chips.push(t('customers.pricing.condAppointment') + scope)
      else if (cond.kind === 'Weekend') chips.push(t('customers.pricing.condWeekend'))
      else if (cond.kind === 'Holiday') chips.push(t('customers.pricing.condHoliday'))
    }
    if (row.meta?.onlyForAdr) chips.push(t('customers.pricing.condAdr'))
    for (const name of row.meta?.warehouseNames ?? []) chips.push(name)
    if (row.effectiveFrom) chips.push(t('customers.pricing.condFromDate', { date: formatDate(row.effectiveFrom) }))
    if (row.effectiveUntil) chips.push(t('customers.pricing.condUntilDate', { date: formatDate(row.effectiveUntil) }))
    if (chips.length === 0) return '—'
    return (
      <span className="customer-pricing-chips">
        {chips.map((chip, index) => (
          <Badge key={index} tone="neutral">
            {chip}
          </Badge>
        ))}
      </span>
    )
  }

  const linkColumns: Column<CustomerAgreementLink>[] = [
    {
      key: 'name',
      header: t('customers.pricing.columnName'),
      render: (link) => (
        <span className="customer-pricing-cell">
          <Link to={`/pricing/tables/${link.agreementId}`} className="issued-items-link">
            {link.name}
          </Link>
          {link.isShared && <Badge tone="info">{t('customers.pricing.sharedTableBadge')}</Badge>}
          {link.baseAgreementId && (
            <Badge tone="info">{t('customers.pricing.derivedFromBadge', { name: link.baseAgreementName ?? '—' })}</Badge>
          )}
          {!link.isActive && <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>}
          {plannedAdjustmentNote(link) && (
            <span className="customer-form-muted" role="note">
              {plannedAdjustmentNote(link)}
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'adjustment',
      header: t('customers.pricing.columnAdjustment'),
      render: (link) =>
        link.isShared ? adjustmentLabel(t, link.assignmentPercentAdjustment, link.assignmentFixedAdjustment) : '—',
    },
    {
      key: 'validity',
      header: t('customers.pricing.columnValidity'),
      render: (link) => (
        <span>
          {validity(link.effectiveFrom, link.effectiveUntil)}
          {link.isShared && (link.assignmentEffectiveFrom || link.assignmentEffectiveUntil) && (
            <span className="customer-form-muted">
              {' '}
              ({t('customers.pricing.assignmentValidityNote', {
                validity: validity(link.assignmentEffectiveFrom, link.assignmentEffectiveUntil),
              })})
            </span>
          )}
        </span>
      ),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: <span aria-label={t('customers.pricing.actionsAria')} />,
            align: 'right' as const,
            render: (link: CustomerAgreementLink) =>
              link.isShared ? (
                // Editing a SHARED table from a customer page would change prices for every
                // assigned customer — only the customer-scoped assignment is editable here.
                <span className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openAssignmentDraft(link)}>
                    {t('customers.pricing.editAssignmentAction')}
                  </button>
                </span>
              ) : (
                <span className="issued-items-row-actions">
                  <button
                    type="button"
                    className="issued-items-link"
                    onClick={() => {
                      const agreement = agreements.find((a) => a.id === link.agreementId)
                      if (agreement) openAgreementDraft(agreement)
                    }}
                  >
                    {t('ui.actions.edit')}
                  </button>
                  <button
                    type="button"
                    className="issued-items-link issued-items-link-danger"
                    onClick={() => setDeleteAgreementTarget(link)}
                  >
                    {t('ui.actions.delete')}
                  </button>
                </span>
              ),
          },
        ]
      : []),
  ]

  /** "2", "2 – 5" or "3+", with the optional dimension caps appended ("≤ 120 kg"). */
  const bracketRangeLabel = (row: CustomerBracketOverrideRow): string => {
    const range =
      row.toQuantity === null
        ? `${row.fromQuantity}+`
        : row.fromQuantity === row.toQuantity
          ? `${row.fromQuantity}`
          : `${row.fromQuantity} – ${row.toQuantity}`
    const caps = [
      row.weightToKg !== null ? t('customers.pricing.bracketCapWeight', { value: row.weightToKg }) : null,
      row.volumeToM3 !== null ? t('customers.pricing.bracketCapVolume', { value: row.volumeToM3 }) : null,
      row.loadingMetersTo !== null ? t('customers.pricing.bracketCapLdm', { value: row.loadingMetersTo }) : null,
    ].filter(Boolean)
    return caps.length > 0 ? `${range} · ${caps.join(' · ')}` : range
  }

  const bracketOverrideColumns: Column<CustomerBracketOverrideRow>[] = [
    {
      key: 'rule',
      header: t('customers.pricing.columnRule'),
      render: (row) => (
        <span className="customer-pricing-cell">
          {row.ruleName}
          {row.agreementName && (
            <span className="customer-form-muted" role="note">
              {row.agreementName}
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'bracket',
      header: t('customers.pricing.columnBracket'),
      render: (row) => (row.unitTypeName ? `${bracketRangeLabel(row)} ${row.unitTypeName}` : bracketRangeLabel(row)),
    },
    {
      key: 'standard',
      header: t('customers.pricing.columnStandardPrice'),
      render: (row) => (row.standardPrice !== null ? formatCurrency(row.standardPrice) : '—'),
    },
    {
      key: 'customer',
      header: t('customers.pricing.columnCustomerPrice'),
      render: (row) => (
        <span className="customer-pricing-cell">
          {formatCurrency(row.price)}
          {row.orphaned && <Badge tone="warning">{t('customers.pricing.orphanedOverrideBadge')}</Badge>}
        </span>
      ),
    },
    {
      key: 'validity',
      header: t('customers.pricing.columnValidity'),
      render: (row) => validity(row.effectiveFrom, row.effectiveUntil),
    },
    {
      key: 'link',
      header: <span aria-label={t('customers.pricing.actionsAria')} />,
      align: 'right' as const,
      render: (row) =>
        // Bracket overrides are managed on the rate-table detail (row action "Klantafwijking…");
        // the customer page deliberately deep-links instead of duplicating that editor.
        row.agreementId ? (
          <Link to={`/pricing/tables/${row.agreementId}`} className="issued-items-link">
            {t('customers.pricing.viewInRateTable')}
          </Link>
        ) : (
          '—'
        ),
    },
  ]

  const serviceColumns: Column<ServiceRow>[] = [
    {
      key: 'service',
      header: t('customers.pricing.columnService'),
      render: (row) => (
        <span className="customer-pricing-cell">
          {row.name}
          {row.disabled && <Badge tone="neutral">{t('customers.pricing.disabledValue')}</Badge>}
        </span>
      ),
    },
    {
      key: 'calculation',
      header: t('customers.pricing.columnCalculation'),
      render: (row) => t(SURCHARGE_KIND_LABELS[row.kind]),
    },
    {
      key: 'amount',
      header: t('customers.pricing.columnAmount'),
      render: (row) => (
        <span className="customer-pricing-cell">
          {row.disabled ? '—' : formatServiceValue(row.kind, row.effectiveValue, row.meta?.unitTypeName, t)}
          {hasCustomerDeviation(row) && <Badge tone="warning">{t('customers.pricing.deviatingBadge')}</Badge>}
          {row.customerValue !== null && !row.disabled && (
            <span className="customer-form-muted" role="note">
              {t('customers.pricing.standardValueNote', { value: formatServiceValue(row.kind, row.defaultValue, row.meta?.unitTypeName, t) })}
            </span>
          )}
          {row.minimumAmount !== null && (
            <span className="customer-form-muted" role="note">
              {t('customers.pricing.minAmountSummary', { amount: formatCurrency(row.minimumAmount) })}
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'conditions',
      header: t('customers.pricing.columnConditions'),
      render: (row) => conditionChips(row),
    },
    {
      key: 'auto',
      header: t('customers.pricing.columnAuto'),
      render: (row) =>
        row.effectiveAutoApply ? t('customers.pricing.autoApplyOnShort') : t('customers.pricing.autoApplyOffShort'),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: <span aria-label={t('customers.pricing.actionsAria')} />,
            align: 'right' as const,
            render: (row: ServiceRow) => (
              <span className="issued-items-row-actions">
                <button type="button" className="issued-items-link" onClick={() => openServiceDraft(row)}>
                  {t('ui.actions.edit')}
                </button>
                {hasCustomerDeviation(row) && (
                  <button type="button" className="issued-items-link" onClick={() => setResetServiceTarget(row)}>
                    {t('customers.pricing.useGeneralValueAgain')}
                  </button>
                )}
              </span>
            ),
          },
        ]
      : []),
  ]

  const ruleColumns = (withStatus: boolean): Column<PriceRule>[] => [
    { key: 'name', header: t('customers.pricing.columnName'), render: (rule) => rule.name },
    { key: 'unit', header: t('customers.pricing.columnUnit'), render: (rule) => rule.unitTypeName ?? '—' },
    {
      key: 'basis',
      header: t('customers.pricing.columnCalculation'),
      render: (rule) => t(PRICE_RULE_BASIS_KEYS[rule.basis]),
    },
    { key: 'zone', header: t('customers.pricing.columnZone'), render: (rule) => rule.zoneName ?? t('customers.pricing.allZones') },
    { key: 'value', header: t('customers.pricing.columnValue'), render: (rule) => ruleValueSummary(t, rule) },
    { key: 'agreement', header: t('customers.pricing.columnAgreement'), render: (rule) => rule.agreementName ?? '—' },
    {
      key: 'validity',
      header: t('customers.pricing.columnValidity'),
      render: (rule) => validity(rule.effectiveFrom, rule.effectiveUntil),
    },
    ...(withStatus
      ? [
          {
            key: 'status',
            header: t('customers.pricing.columnStatus'),
            render: (rule: PriceRule) =>
              !rule.isActive ? (
                <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
              ) : rule.effectiveFrom > now ? (
                <Badge tone="info">{t('customers.pricing.statusPlanned', { date: formatDate(rule.effectiveFrom) })}</Badge>
              ) : (
                <Badge tone="success">{t('customers.pricing.statusActive')}</Badge>
              ),
          },
        ]
      : []),
    ...(canManage
      ? [
          {
            key: 'actions',
            header: <span aria-label={t('customers.pricing.actionsAria')} />,
            align: 'right' as const,
            render: (rule: PriceRule) => (
              <span className="issued-items-row-actions">
                <button type="button" className="issued-items-link" onClick={() => openDraft(rule)}>
                  {t('ui.actions.edit')}
                </button>
                <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(rule)}>
                  {t('ui.actions.delete')}
                </button>
              </span>
            ),
          },
        ]
      : []),
  ]

  const serviceDraftMeta = serviceDraft?.serviceOptionId ? metaById.get(serviceDraft.serviceOptionId) : undefined
  const serviceDraftRow = serviceDraft?.serviceOptionId
    ? config.serviceOptions.find((o) => o.serviceOptionId === serviceDraft.serviceOptionId)
    : undefined
  // "+ Toeslag toevoegen" offers every active service that has no deviation yet.
  const addableServiceOptions: SearchableSelectOption[] = serviceRows
    .filter((row) => !hasCustomerDeviation(row))
    .map((row) => ({
      value: row.serviceOptionId,
      label: row.name,
      description: `${t(SURCHARGE_KIND_LABELS[row.kind])} · ${formatServiceValue(row.kind, row.defaultValue, row.meta?.unitTypeName, t)}`,
    }))

  return (
    <section className="customer-panel customer-pricing-panel">
      <FormSection title={t('customers.pricing.basisTitle')} description={t('customers.pricing.basisHint')} columns={1}>
        {canManage && (
          <div className="customer-panel-header customer-pricing-section-actions">
            <Button variant="secondary" onClick={() => openAgreementDraft(null)}>
              {t('customers.pricing.addAgreement')}
            </Button>
          </div>
        )}
        {links.length === 0 ? (
          <EmptyState
            message={t('customers.pricing.basisEmptyBody')}
            action={
              canManage ? (
                <Button variant="secondary" onClick={() => openAgreementDraft(null)}>
                  {t('customers.pricing.addAgreement')}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <DataTable columns={linkColumns} rows={links} rowKey={(link) => `${link.agreementId}-${link.assignmentId ?? 'own'}`} />
        )}
        {bracketOverrides.length > 0 && (
          <details className="customer-pricing-bracket-overrides">
            <summary>{t('customers.pricing.bracketOverridesSummary', { count: bracketOverrides.length })}</summary>
            <p className="customer-form-muted">{t('customers.pricing.bracketOverridesHint')}</p>
            <DataTable
              columns={bracketOverrideColumns}
              rows={bracketOverrides}
              rowKey={(row) => row.id}
            />
          </details>
        )}
      </FormSection>

      <FormSection
        title={t('customers.pricing.servicesSectionTitle')}
        description={t('customers.pricing.servicesSectionHint')}
        columns={1}
      >
        {canManage && (
          <div className="customer-panel-header customer-pricing-section-actions">
            <Button variant="secondary" onClick={() => openServiceDraft(null)}>
              {t('customers.pricing.addServiceOverride')}
            </Button>
          </div>
        )}
        {diesel?.enabled && (
          <p className="customer-form-muted customer-pricing-diesel" role="note">
            <Badge tone="info">{t('customers.pricing.dieselCardTitle')}</Badge>{' '}
            {t('customers.pricing.dieselCardSummary', { percent: diesel.percent })}{' '}
            {t('customers.pricing.dieselCardNote')}
          </p>
        )}
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={showAllServices} onChange={(e) => setShowAllServices(e.target.checked)} />
          {t('customers.pricing.showAllStandardServices')}
        </label>
        {visibleServiceRows.length === 0 ? (
          <EmptyState
            message={showAllServices ? t('customers.pricing.noServices') : t('customers.pricing.servicesEmptyDeviations')}
            action={
              canManage ? (
                <Button variant="secondary" onClick={() => openServiceDraft(null)}>
                  {t('customers.pricing.addServiceOverride')}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <DataTable columns={serviceColumns} rows={visibleServiceRows} rowKey={(row) => row.serviceOptionId} />
        )}
      </FormSection>

      <FormSection
        title={t('customers.pricing.deviationPricesTitle')}
        description={t('customers.pricing.deviationPricesHint')}
        columns={1}
      >
        {canManage && (
          <div className="customer-panel-header customer-pricing-section-actions">
            <Button onClick={() => openDraft(null)}>{t('customers.pricing.addRule')}</Button>
          </div>
        )}
        {activeAndPlannedRules.length === 0 ? (
          <EmptyState
            message={t('customers.pricing.rulesEmptyBody')}
            action={canManage ? <Button variant="secondary" onClick={() => openDraft(null)}>{t('customers.pricing.addRule')}</Button> : undefined}
          />
        ) : (
          <DataTable columns={ruleColumns(true)} rows={activeAndPlannedRules} rowKey={(rule) => rule.id} />
        )}
        {historyRules.length > 0 && (
          <details>
            <summary>{t('customers.pricing.historyTitle', { count: historyRules.length })}</summary>
            <DataTable columns={ruleColumns(false)} rows={historyRules} rowKey={(rule) => rule.id} />
          </details>
        )}
      </FormSection>

      {serviceDraft && (
        <Modal
          title={
            serviceDraft.isNew
              ? t('customers.pricing.newServiceOverrideTitle')
              : t('customers.pricing.editServiceOverrideTitle', { name: serviceDraftRow?.name ?? '' })
          }
          onClose={() => setServiceDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setServiceDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="service-override-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="service-override-form" className="issued-items-form" onSubmit={submitServiceDraft} noValidate>
            {serviceError && (
              <div className="issued-items-form-error" role="alert">
                {serviceError}
              </div>
            )}
            {serviceDraft.isNew && (
              <FormField label={t('customers.pricing.serviceField')} htmlFor="so-service" required>
                <SearchableSelect
                  id="so-service"
                  value={serviceDraft.serviceOptionId}
                  onChange={(value) => setServiceDraft((d) => (d ? { ...d, serviceOptionId: value } : d))}
                  options={addableServiceOptions}
                  placeholder={t('customers.pricing.chooseServicePlaceholder')}
                  emptyMessage={t('customers.pricing.noAddableServices')}
                />
              </FormField>
            )}
            {serviceDraft.serviceOptionId && serviceDraftRow && (
              <p className="customer-form-muted" role="note">
                {t('customers.pricing.standardValueNote', {
                  value: formatServiceValue(serviceDraftRow.kind, serviceDraftRow.defaultValue, serviceDraftMeta?.unitTypeName, t),
                })}
              </p>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.customerValueField')} htmlFor="so-value" hint={t('customers.pricing.customerValueHint')}>
                <input
                  id="so-value"
                  type="number"
                  step="0.01"
                  value={serviceDraft.value}
                  disabled={serviceDraft.disabled}
                  onChange={(e) => setServiceDraft((d) => (d ? { ...d, value: e.target.value } : d))}
                />
              </FormField>
              <FormField label={t('customers.pricing.minAmountField')} htmlFor="so-min">
                <input
                  id="so-min"
                  type="number"
                  step="0.01"
                  value={serviceDraft.minimumAmount}
                  disabled={serviceDraft.disabled}
                  onChange={(e) => setServiceDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.validFromField')} htmlFor="so-from">
                <input
                  id="so-from"
                  type="date"
                  value={serviceDraft.effectiveFrom}
                  onChange={(e) => setServiceDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))}
                />
              </FormField>
              <FormField label={t('customers.pricing.validUntilField')} htmlFor="so-until" hint={t('customers.pricing.validUntilHint')}>
                <input
                  id="so-until"
                  type="date"
                  value={serviceDraft.effectiveUntil}
                  onChange={(e) => setServiceDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.columnAutoApply')} htmlFor="so-auto">
                <SearchableSelect
                  id="so-auto"
                  value={serviceDraft.autoApply}
                  onChange={(value) =>
                    setServiceDraft((d) => (d ? { ...d, autoApply: (value ?? 'inherit') as ServiceDraft['autoApply'] } : d))
                  }
                  options={[
                    {
                      value: 'inherit',
                      label: t('customers.pricing.autoApplyDefault', {
                        state: (serviceDraftMeta?.autoApply ?? false)
                          ? t('customers.pricing.autoApplyOnShort')
                          : t('customers.pricing.autoApplyOffShort'),
                      }),
                    },
                    { value: 'on', label: t('customers.pricing.autoApplyOn') },
                    { value: 'off', label: t('customers.pricing.autoApplyOff') },
                  ]}
                  clearable={false}
                />
              </FormField>
              <FormField label={t('customers.pricing.invoiceDescriptionField')} htmlFor="so-invoice">
                <input
                  id="so-invoice"
                  value={serviceDraft.invoiceDescription}
                  maxLength={200}
                  onChange={(e) => setServiceDraft((d) => (d ? { ...d, invoiceDescription: e.target.value } : d))}
                />
              </FormField>
            </div>
            <label className="tof-checkbox">
              <input
                type="checkbox"
                checked={serviceDraft.disabled}
                onChange={(e) => setServiceDraft((d) => (d ? { ...d, disabled: e.target.checked } : d))}
              />
              {t('customers.pricing.disableForCustomer')}
            </label>
          </form>
        </Modal>
      )}

      {assignmentDraft && (
        <Modal
          title={t('customers.pricing.assignmentModalTitle', { name: assignmentDraft.link.name })}
          onClose={() => setAssignmentDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setAssignmentDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="assignment-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="assignment-form" className="issued-items-form" onSubmit={submitAssignmentDraft} noValidate>
            {assignmentError && (
              <div className="issued-items-form-error" role="alert">
                {assignmentError}
              </div>
            )}
            <p className="customer-form-muted" role="note">
              {t('customers.pricing.assignmentModalHint')}
            </p>
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.assignmentPercentField')} htmlFor="as-percent" hint={t('customers.pricing.assignmentPercentHint')}>
                <input
                  id="as-percent"
                  type="number"
                  step="0.01"
                  value={assignmentDraft.percent}
                  onChange={(e) => setAssignmentDraft((d) => (d ? { ...d, percent: e.target.value } : d))}
                />
              </FormField>
              <FormField label={t('customers.pricing.assignmentFixedField')} htmlFor="as-fixed" hint={t('customers.pricing.assignmentFixedHint')}>
                <input
                  id="as-fixed"
                  type="number"
                  step="0.01"
                  value={assignmentDraft.fixed}
                  onChange={(e) => setAssignmentDraft((d) => (d ? { ...d, fixed: e.target.value } : d))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('customers.pricing.validFromField')} htmlFor="as-from">
                <input
                  id="as-from"
                  type="date"
                  value={assignmentDraft.effectiveFrom}
                  onChange={(e) => setAssignmentDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))}
                />
              </FormField>
              <FormField label={t('customers.pricing.validUntilField')} htmlFor="as-until" hint={t('customers.pricing.validUntilHint')}>
                <input
                  id="as-until"
                  type="date"
                  value={assignmentDraft.effectiveUntil}
                  onChange={(e) => setAssignmentDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))}
                />
              </FormField>
            </div>
          </form>
        </Modal>
      )}

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
                <SearchableSelect
                  id="pr-basis"
                  value={toPrimarySelectValue(draft.basis)}
                  onChange={(value) => {
                    if (!value) return
                    setDraft((d) => (d ? { ...d, basis: value === 'unit' ? 'QuantityBracket' : (value as PriceRuleBasis) } : d))
                  }}
                  options={primaryBasisOptions}
                  clearable={false}
                  ariaLabel={t('customers.pricing.priceBasisField')}
                />
              </FormField>
            </div>
            {(draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket') && (
              <div className="issued-items-form-row">
                <FormField label={t('customers.pricing.methodField')} htmlFor="pr-method">
                  <SearchableSelect
                    id="pr-method"
                    value={draft.basis}
                    onChange={(value) => {
                      if (!value) return
                      setDraft((d) => (d ? { ...d, basis: value as PriceRuleBasis } : d))
                    }}
                    options={[
                      { value: 'QuantityBracket', label: t('customers.pricing.methodQuantityBracket') },
                      { value: 'PerUnit', label: t('customers.pricing.methodPerUnit') },
                    ]}
                    clearable={false}
                    ariaLabel={t('customers.pricing.methodField')}
                  />
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
                  <SearchableSelect
                    id="pr-unit"
                    value={draft.unitTypeId || null}
                    onChange={(value) => setDraft((d) => (d ? { ...d, unitTypeId: value ?? '' } : d))}
                    options={unitOptions}
                    placeholder={t('customers.pricing.chooseUnitOption')}
                    ariaLabel={t('customers.pricing.columnUnit')}
                  />
                </FormField>
              )}
              <FormField label={t('customers.pricing.columnZone')} htmlFor="pr-zone" hint={t('customers.pricing.zoneHint')}>
                <SearchableSelect
                  id="pr-zone"
                  value={draft.zoneId || null}
                  onChange={(value) => setDraft((d) => (d ? { ...d, zoneId: value ?? '' } : d))}
                  options={zoneOptions}
                  placeholder={t('customers.pricing.allOption')}
                  ariaLabel={t('customers.pricing.columnZone')}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField
                label={t('customers.pricing.columnAgreement')}
                htmlFor="pr-agreement"
                hint={t('customers.pricing.agreementFieldHint')}
              >
                <SearchableSelect
                  id="pr-agreement"
                  value={draft.agreementId || null}
                  onChange={(value) => setDraft((d) => (d ? { ...d, agreementId: value ?? '' } : d))}
                  options={agreements
                    .filter((agreement) => !agreement.baseAgreementId)
                    .map((agreement) => ({ value: agreement.id, label: agreement.name }))}
                  placeholder={t('customers.pricing.looseRuleOption')}
                  ariaLabel={t('customers.pricing.columnAgreement')}
                />
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
                    <SearchableSelect
                      id="pr-bracket-mode"
                      value={draft.bracketMode}
                      onChange={(value) => {
                        if (!value) return
                        setDraft((d) => (d ? { ...d, bracketMode: value as BracketSelectionMode } : d))
                      }}
                      options={Object.entries(BRACKET_SELECTION_MODE_KEYS).map(([value, key]) => ({ value, label: t(key) }))}
                      clearable={false}
                      ariaLabel={t('customers.pricing.bracketModeField')}
                    />
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
                <SearchableSelect
                  id="pa-base"
                  value={agreementDraft.baseAgreementId || null}
                  onChange={(value) => setAgreementDraft((d) => (d ? { ...d, baseAgreementId: value ?? '' } : d))}
                  options={baseTableOptions
                    .filter((a) => a.id !== agreementDraft.agreement?.id)
                    .map((a) => ({ value: a.id, label: a.name }))}
                  placeholder={t('customers.pricing.noBaseTableOption')}
                  ariaLabel={t('customers.pricing.baseTableField')}
                />
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
                      <SearchableSelect
                        ariaLabel={t('customers.pricing.modifierZoneAria', { index: index + 1 })}
                        value={modifier.zoneId || null}
                        onChange={(value) =>
                          setAgreementDraft((d) =>
                            d ? { ...d, modifiers: d.modifiers.map((m, i) => (i === index ? { ...m, zoneId: value ?? '' } : m)) } : d,
                          )
                        }
                        options={zoneOptions}
                        placeholder={t('customers.pricing.allZonesOption')}
                      />
                      <SearchableSelect
                        ariaLabel={t('customers.pricing.modifierKindAria', { index: index + 1 })}
                        value={modifier.mode}
                        onChange={(value) =>
                          setAgreementDraft((d) =>
                            d
                              ? {
                                  ...d,
                                  modifiers: d.modifiers.map((m, i) =>
                                    i === index ? { ...m, mode: (value ?? 'Percent') as 'Percent' | 'Fixed' } : m,
                                  ),
                                }
                              : d,
                          )
                        }
                        options={kindOptions}
                        clearable={false}
                      />
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
                  <SearchableSelect
                    ariaLabel={t('customers.pricing.surchargeKindAria', { index: index + 1 })}
                    value={surcharge.kind}
                    onChange={(value) =>
                      setAgreementDraft((d) =>
                        d
                          ? {
                              ...d,
                              surcharges: d.surcharges.map((s, i) =>
                                i === index ? { ...s, kind: (value ?? 'Percent') as SurchargeKind } : s,
                              ),
                            }
                          : d,
                      )
                    }
                    options={kindOptions}
                    clearable={false}
                  />
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

      {resetServiceTarget && (
        <ConfirmDialog
          title={t('customers.pricing.resetOverrideTitle')}
          message={t('customers.pricing.resetOverrideMessage', {
            name: resetServiceTarget.name,
            value: formatServiceValue(resetServiceTarget.kind, resetServiceTarget.defaultValue, resetServiceTarget.meta?.unitTypeName, t),
          })}
          confirmLabel={t('customers.pricing.useGeneralValueAgain')}
          destructive
          onConfirm={handleResetService}
          onCancel={() => setResetServiceTarget(null)}
        />
      )}
    </section>
  )
}
