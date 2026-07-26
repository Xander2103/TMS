import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { formatServiceValue } from '../../tarification/serviceValueFormat'
import type { SurchargeKind } from '../../tarification/types'
import {
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
  type CustomerPricingConfig,
  type PriceRule,
  type PriceRuleBasis,
  type PriceRuleBracketInput,
  type PricingAgreement,
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
  baseAmount: string
  priority: string
  minimumQuantity: string
  quantityRoundingStep: string
  oversizeLengthCm: string
  oversizeWidthCm: string
  oversizeBillableFactor: string
  brackets: { from: string; to: string; price: string; extra: string }[]
}

/** Maps a stored basis onto the editor's primary "Prijsbasis" choice (spec §10). */
const toPrimarySelectValue = (basis: PriceRuleBasis): string =>
  basis === 'PerUnit' || basis === 'QuantityBracket' ? 'unit' : basis

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
}

/** A shared (reusable) rate table this customer is assigned to, with its adjustment. */
interface AssignedSharedAgreement {
  agreement: PricingAgreement
  assignment: PricingAssignment
}

function assignmentAdjustmentLabel(assignment: PricingAssignment): string {
  const parts: string[] = []
  if (assignment.percentAdjustment !== null) {
    parts.push(`${assignment.percentAdjustment > 0 ? '+' : ''}${assignment.percentAdjustment}%`)
  }
  if (assignment.fixedAdjustment !== null) {
    parts.push(`${assignment.fixedAdjustment > 0 ? '+' : ''}€ ${assignment.fixedAdjustment.toFixed(2)}`)
  }
  return parts.length > 0 ? parts.join(', ') : 'geen aanpassing'
}

const today = () => new Date().toISOString().slice(0, 10)

function ruleValueSummary(rule: PriceRule): string {
  if (rule.brackets.length > 0) return `${rule.brackets.length} staffels`
  const parts: string[] = []
  if (rule.baseAmount !== null) parts.push(`basis € ${rule.baseAmount.toFixed(2)}`)
  if (rule.unitPrice !== null) parts.push(`€ ${rule.unitPrice.toFixed(2)}`)
  if (rule.minimumAmount !== null) parts.push(`min € ${rule.minimumAmount.toFixed(2)}`)
  return parts.join(', ') || '—'
}

/**
 * The customer's commercial tariff overview (spec §13): pricing agreements (tarievenkaarten),
 * current prices, scheduled future versions and price history, plus service-option prices.
 * Versioning happens via effective windows — old versions are never overwritten.
 */
export function CustomerUnitPricingPanel({ customerId }: CustomerUnitPricingPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [config, setConfig] = useState<CustomerPricingConfig | null>(null)
  const [rules, setRules] = useState<PriceRule[]>([])
  const [agreements, setAgreements] = useState<PricingAgreement[]>([])
  const [sharedAssigned, setSharedAssigned] = useState<AssignedSharedAgreement[]>([])
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
      .catch(() => setLoadError('De prijsafspraken konden niet worden geladen.'))
  }, [customerId, canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return null
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!config) return <p className="placeholder-text">Prijsafspraken laden…</p>

  const now = today()
  const currentRules = rules.filter(
    (r) => r.isActive && r.effectiveFrom <= now && (r.effectiveUntil === null || r.effectiveUntil >= now),
  )
  const futureRules = rules.filter((r) => r.isActive && r.effectiveFrom > now)
  const historyRules = rules.filter((r) => !r.isActive || (r.effectiveUntil !== null && r.effectiveUntil < now))

  async function saveServiceOverride(
    serviceOptionId: string,
    patch: Partial<{ value: number | null; disabled: boolean; effectiveFrom: string | null; effectiveUntil: string | null }>,
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
            }
          }

          if (reset) {
            // "Algemene waarde opnieuw gebruiken": drop the whole override row.
            return { serviceOptionId: o.serviceOptionId, value: null, disabled: false }
          }

          return {
            serviceOptionId: o.serviceOptionId,
            value: patch.value !== undefined ? patch.value : o.customerValue,
            disabled: patch.disabled !== undefined ? patch.disabled : o.disabled,
            minimumAmount: o.minimumAmount,
            invoiceDescription: o.invoiceDescription,
            effectiveFrom: patch.effectiveFrom !== undefined ? patch.effectiveFrom : o.effectiveFrom,
            effectiveUntil: patch.effectiveUntil !== undefined ? patch.effectiveUntil : o.effectiveUntil,
          }
        }),
      })
      setConfig(saved)
      showSuccess(reset ? 'Algemene waarde wordt opnieuw gebruikt.' : 'Klantoverride opgeslagen.')
    } catch (err) {
      showError(describeApiError(err, 'De klantoverride kon niet worden opgeslagen.').message)
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
            baseAmount: rule.baseAmount !== null ? String(rule.baseAmount) : '',
            priority: String(rule.priority),
            minimumQuantity: rule.minimumQuantity !== null ? String(rule.minimumQuantity) : '',
            quantityRoundingStep: rule.quantityRoundingStep !== null ? String(rule.quantityRoundingStep) : '',
            oversizeLengthCm: rule.oversizeLengthCm !== null ? String(rule.oversizeLengthCm) : '',
            oversizeWidthCm: rule.oversizeWidthCm !== null ? String(rule.oversizeWidthCm) : '',
            oversizeBillableFactor: rule.oversizeBillableFactor !== null ? String(rule.oversizeBillableFactor) : '',
            brackets: rule.brackets.map((b) => ({
              from: String(b.fromQuantity),
              to: b.toQuantity !== null ? String(b.toQuantity) : '',
              price: String(b.price),
              extra: b.pricePerExtraUnit !== null ? String(b.pricePerExtraUnit) : '',
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
            baseAmount: '',
            priority: '0',
            minimumQuantity: '',
            quantityRoundingStep: '',
            oversizeLengthCm: '',
            oversizeWidthCm: '',
            oversizeBillableFactor: '',
            brackets: [{ from: '1', to: '', price: '', extra: '' }],
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
        showSuccess('Prijsregel bijgewerkt.')
      } else {
        await createPriceRule(input)
        showSuccess('Prijsregel toegevoegd.')
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De prijsregel kon niet worden opgeslagen.').message)
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
      showSuccess('Prijsregel verwijderd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De prijsregel kon niet worden verwijderd.').message)
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
      }
      if (agreementDraft.agreement) {
        await updatePricingAgreement(agreementDraft.agreement.id, input)
        showSuccess('Prijsafspraak bijgewerkt.')
      } else {
        await createPricingAgreement(input)
        showSuccess('Prijsafspraak toegevoegd.')
      }
      setAgreementDraft(null)
      reload()
    } catch (err) {
      setAgreementError(describeApiError(err, 'De prijsafspraak kon niet worden opgeslagen.').message)
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
      showSuccess('Prijsafspraak verwijderd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De prijsafspraak kon niet worden verwijderd.').message)
    }
  }

  const usesBrackets = draft?.basis === 'QuantityBracket' || draft?.basis === 'WeightBracket' || draft?.basis === 'PerStop'
  const pricingUnits = units.filter((u) => u.isActive && u.allowForPricing)
  const priceLabelByBasis: Partial<Record<PriceRuleBasis, string>> = {
    PerUnit: 'Prijs per eenheid (€)',
    Hourly: 'Uurtarief (€)',
    Fixed: 'Vaste prijs (€)',
    PerKm: 'Prijs per km (€)',
    PerLoadingMeter: 'Prijs per laadmeter (€)',
    PerVolume: 'Prijs per m³ (€)',
    PerStop: 'Prijs per stop (€) — leeg bij staffels',
    PerPallet: 'Prijs per pallet (€)',
    PerTon: 'Prijs per ton (€)',
  }

  const rulesTable = (list: PriceRule[]) => (
    <table className="issued-items-table">
      <thead>
        <tr>
          <th>Naam</th>
          <th>Eenheid</th>
          <th>Berekeningswijze</th>
          <th>Zone</th>
          <th>Waarde</th>
          <th>Prijsafspraak</th>
          <th>Geldig</th>
          {canManage && <th aria-label="Acties" />}
        </tr>
      </thead>
      <tbody>
        {list.map((rule) => (
          <tr key={rule.id}>
            <td>{rule.name}</td>
            <td>{rule.unitTypeName ?? '—'}</td>
            <td>{PRICE_RULE_BASIS_LABELS[rule.basis]}</td>
            <td>{rule.zoneName ?? 'Alle'}</td>
            <td>{ruleValueSummary(rule)}</td>
            <td>{rule.agreementName ?? '—'}</td>
            <td>
              {rule.effectiveFrom}
              {rule.effectiveUntil ? ` – ${rule.effectiveUntil}` : ' →'}
              {!rule.isActive && <Badge tone="neutral">Inactief</Badge>}
            </td>
            {canManage && (
              <td className="issued-items-row-actions">
                <button type="button" className="issued-items-link" onClick={() => openDraft(rule)}>
                  Bewerken
                </button>
                <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(rule)}>
                  Verwijderen
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
        <h3>Prijsafspraken (tarievenkaarten)</h3>
        {canManage && <Button variant="secondary" onClick={() => openAgreementDraft(null)}>+ Prijsafspraak</Button>}
      </div>
      {agreements.length === 0 && sharedAssigned.length === 0 && (
        <p className="placeholder-text">Nog geen prijsafspraken; losse prijsregels blijven mogelijk.</p>
      )}
      {(agreements.length > 0 || sharedAssigned.length > 0) && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Naam</th>
              <th>Geldig</th>
              <th>Minimum</th>
              <th>Toeslagen</th>
              <th>Notities</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {sharedAssigned.map(({ agreement, assignment }) => (
              <tr key={`shared-${agreement.id}`}>
                <td>
                  {agreement.name} <Badge tone="info">Gedeelde tabel</Badge>
                </td>
                <td>
                  {agreement.effectiveFrom}
                  {agreement.effectiveUntil ? ` – ${agreement.effectiveUntil}` : ' →'}
                </td>
                <td>{agreement.minimumAmount !== null ? `€ ${agreement.minimumAmount.toFixed(2)}` : '—'}</td>
                <td>{assignmentAdjustmentLabel(assignment)}</td>
                <td>{agreement.notes ?? '—'}</td>
                {canManage && <td className="issued-items-row-actions">—</td>}
              </tr>
            ))}
            {agreements.map((agreement) => (
              <tr key={agreement.id}>
                <td>{agreement.name}</td>
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
                      Bewerken
                    </button>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setDeleteAgreementTarget(agreement)}
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

      <div className="customer-panel-header">
        <h3>Actuele prijzen</h3>
        {canManage && <Button onClick={() => openDraft(null)}>+ Prijsregel</Button>}
      </div>
      {currentRules.length === 0 && <p className="placeholder-text">Geen actuele prijsregels voor deze klant.</p>}
      {currentRules.length > 0 && rulesTable(currentRules)}

      {futureRules.length > 0 && (
        <>
          <h4>Toekomstige prijzen</h4>
          {rulesTable(futureRules)}
        </>
      )}

      {historyRules.length > 0 && (
        <details>
          <summary>Prijshistoriek ({historyRules.length})</summary>
          {rulesTable(historyRules)}
        </details>
      )}

      <h4>Diensten & toeslagen</h4>
      <p className="customer-form-muted">
        Let op: wanneer u hier een waarde invult, wordt de algemene standaardregel voor deze klant overschreven.
      </p>
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>Dienst</th>
            <th>Algemene prijs</th>
            <th>Klantoverride</th>
            <th>Geldig</th>
            <th>Effectieve prijs</th>
            <th>Bron</th>
            {canManage && <th aria-label="Acties" />}
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
                      Let op: deze klantprijs overschrijft
                      {option.effectiveFrom ? ' vanaf de ingestelde datum' : ''} de algemene prijs van{' '}
                      {formatServiceValue(option.kind, option.defaultValue)}.
                    </div>
                  )}
                  {option.disabled && (
                    <div className="customer-form-muted" role="note">
                      Let op: deze service is algemeen beschikbaar, maar wordt voor deze klant uitgeschakeld.
                    </div>
                  )}
                </td>
                <td>{formatServiceValue(option.kind, option.defaultValue)}</td>
                <td>
                  <input
                    aria-label={`Klantoverride voor ${option.name}`}
                    type="number"
                    step="0.01"
                    defaultValue={option.customerValue ?? ''}
                    placeholder="geen override"
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
                      Uitschakelen voor deze klant
                    </label>
                  )}
                </td>
                <td>
                  <input
                    aria-label={`Override geldig vanaf voor ${option.name}`}
                    type="date"
                    defaultValue={option.effectiveFrom ?? ''}
                    disabled={!canManage || !hasOverride}
                    onBlur={(e) => {
                      const value = e.target.value || null
                      if (value !== option.effectiveFrom) void saveServiceOverride(option.serviceOptionId, { effectiveFrom: value })
                    }}
                  />
                  <input
                    aria-label={`Override geldig tot voor ${option.name}`}
                    type="date"
                    defaultValue={option.effectiveUntil ?? ''}
                    disabled={!canManage || !hasOverride}
                    onBlur={(e) => {
                      const value = e.target.value || null
                      if (value !== option.effectiveUntil) void saveServiceOverride(option.serviceOptionId, { effectiveUntil: value })
                    }}
                  />
                </td>
                <td>{option.disabled ? 'Uitgeschakeld' : formatServiceValue(option.kind, option.effectiveValue)}</td>
                <td>{option.source}</td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    {hasOverride && (
                      <button
                        type="button"
                        className="issued-items-link"
                        onClick={() => void saveServiceOverride(option.serviceOptionId, {}, true)}
                      >
                        Algemene waarde opnieuw gebruiken
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
          title={draft.rule ? `Prijsregel bewerken — ${draft.rule.name}` : 'Prijsregel toevoegen'}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="price-rule-form" disabled={busy}>
                Opslaan
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
              <FormField label="Naam" htmlFor="pr-name" required>
                <input id="pr-name" value={draft.name} onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
              <FormField label="Prijsbasis" htmlFor="pr-basis" hint="Eén primaire berekeningsbasis; toeslagen komen apart.">
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
                <FormField label="Berekeningswijze" htmlFor="pr-method">
                  <select
                    id="pr-method"
                    value={draft.basis}
                    onChange={(e) => setDraft((d) => (d ? { ...d, basis: e.target.value as PriceRuleBasis } : d))}
                  >
                    <option value="QuantityBracket">Vaste prijs per aantal (staffel)</option>
                    <option value="PerUnit">Prijs per eenheid</option>
                  </select>
                </FormField>
              </div>
            )}
            <div className="issued-items-form-row">
              {(draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket' || draft.basis === 'Hourly') && (
                <FormField
                  label="Eenheid"
                  htmlFor="pr-unit"
                  hint={draft.basis === 'Hourly' ? 'Kies de tijd-eenheid (bv. Uur).' : 'De eenheid waarop deze prijs geldt.'}
                >
                  <select id="pr-unit" value={draft.unitTypeId} onChange={(e) => setDraft((d) => (d ? { ...d, unitTypeId: e.target.value } : d))}>
                    <option value="">— Kies eenheid —</option>
                    {pricingUnits.map((unit) => (
                      <option key={unit.id} value={unit.id}>
                        {unit.name}
                      </option>
                    ))}
                  </select>
                </FormField>
              )}
              <FormField label="Zone" htmlFor="pr-zone" hint="Leeg = alle bestemmingen.">
                <select id="pr-zone" value={draft.zoneId} onChange={(e) => setDraft((d) => (d ? { ...d, zoneId: e.target.value } : d))}>
                  <option value="">— Alle —</option>
                  {zones.map((zone) => (
                    <option key={zone.id} value={zone.id}>
                      {zone.code} — {zone.name}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Prijsafspraak" htmlFor="pr-agreement" hint="Optioneel: groepeer onder een tarievenkaart.">
                <select id="pr-agreement" value={draft.agreementId} onChange={(e) => setDraft((d) => (d ? { ...d, agreementId: e.target.value } : d))}>
                  <option value="">— Losse regel —</option>
                  {agreements.map((agreement) => (
                    <option key={agreement.id} value={agreement.id}>
                      {agreement.name}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Prioriteit" htmlFor="pr-priority" hint="Hoger wint bij gelijke specificiteit.">
                <input id="pr-priority" type="number" value={draft.priority} onChange={(e) => setDraft((d) => (d ? { ...d, priority: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Geldig vanaf" htmlFor="pr-from" required>
                <input id="pr-from" type="date" value={draft.effectiveFrom} onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))} />
              </FormField>
              <FormField label="Geldig tot" htmlFor="pr-until" hint="Leeg = onbeperkt.">
                <input id="pr-until" type="date" value={draft.effectiveUntil} onChange={(e) => setDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))} />
              </FormField>
            </div>
            {(!usesBrackets || draft.basis === 'PerStop') && (
              <div className="issued-items-form-row">
                <FormField
                  label={priceLabelByBasis[draft.basis] ?? 'Prijs per eenheid (€)'}
                  htmlFor="pr-price"
                  required={draft.basis !== 'PerStop'}
                >
                  <input id="pr-price" type="number" step="0.01" value={draft.unitPrice} onChange={(e) => setDraft((d) => (d ? { ...d, unitPrice: e.target.value } : d))} />
                </FormField>
                <FormField label="Minimumbedrag (€)" htmlFor="pr-min">
                  <input id="pr-min" type="number" step="0.01" value={draft.minimumAmount} onChange={(e) => setDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {draft.basis === 'Hourly' && (
              <div className="issued-items-form-row">
                <FormField label="Minimum aantal uur" htmlFor="pr-minq" hint="Bv. 3: minder wordt als 3 uur gefactureerd.">
                  <input id="pr-minq" type="number" step="0.25" value={draft.minimumQuantity} onChange={(e) => setDraft((d) => (d ? { ...d, minimumQuantity: e.target.value } : d))} />
                </FormField>
                <FormField label="Afrondingsstap (uur)" htmlFor="pr-step" hint="Bv. 0,25 = per begonnen kwartier.">
                  <input id="pr-step" type="number" step="0.05" value={draft.quantityRoundingStep} onChange={(e) => setDraft((d) => (d ? { ...d, quantityRoundingStep: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {(draft.basis === 'PerKm' || draft.basis === 'PerLoadingMeter' || draft.basis === 'PerVolume' || draft.basis === 'PerStop') && (
              <div className="issued-items-form-row">
                <FormField label="Basisbedrag (€)" htmlFor="pr-base-inline" hint="Vaste basiskost vóór de variabele prijs.">
                  <input id="pr-base-inline" type="number" step="0.01" value={draft.baseAmount} onChange={(e) => setDraft((d) => (d ? { ...d, baseAmount: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {usesBrackets && (
              <fieldset className="issued-items-generate-dimension">
                <legend>
                  {draft.basis === 'WeightBracket'
                    ? 'Staffels (kg)'
                    : draft.basis === 'PerStop'
                      ? 'Progressieve stops (optioneel: 1e/2e/volgende stop)'
                      : 'Staffels (aantal)'}
                </legend>
                {draft.brackets.map((bracket, index) => (
                  <div key={index} className="issued-items-form-row customer-rule-bracket">
                    <input aria-label={`Staffel ${index + 1} van`} type="number" step="0.01" placeholder="van" value={bracket.from}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, from: e.target.value } : b)) } : d))} />
                    <input aria-label={`Staffel ${index + 1} tot`} type="number" step="0.01" placeholder="tot (leeg = open)" value={bracket.to}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, to: e.target.value } : b)) } : d))} />
                    <input aria-label={`Staffel ${index + 1} prijs`} type="number" step="0.01" placeholder="prijs €" value={bracket.price}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, price: e.target.value } : b)) } : d))} />
                    <input aria-label={`Staffel ${index + 1} extra per eenheid`} type="number" step="0.01" placeholder="€/extra (open staffel)" value={bracket.extra}
                      onChange={(e) => setDraft((d) => (d ? { ...d, brackets: d.brackets.map((b, i) => (i === index ? { ...b, extra: e.target.value } : b)) } : d))} />
                    <Button variant="ghost" onClick={() => setDraft((d) => (d ? { ...d, brackets: d.brackets.filter((_, i) => i !== index) } : d))}>
                      Verwijderen
                    </Button>
                  </div>
                ))}
                <Button variant="secondary" onClick={() => setDraft((d) => (d ? { ...d, brackets: [...d.brackets, { from: '', to: '', price: '', extra: '' }] } : d))}>
                  + Staffel
                </Button>
              </fieldset>
            )}
            {(draft.basis === 'PerUnit' || draft.basis === 'QuantityBracket') && (
            <details>
              <summary>Geavanceerd (basisbedrag & buitenmaat)</summary>
              <div className="issued-items-form-row">
                <FormField label="Basisbedrag (€)" htmlFor="pr-base" hint="Wordt bij het berekende bedrag geteld.">
                  <input id="pr-base" type="number" step="0.01" value={draft.baseAmount} onChange={(e) => setDraft((d) => (d ? { ...d, baseAmount: e.target.value } : d))} />
                </FormField>
              </div>
              <div className="issued-items-form-row">
                <FormField label="Buitenmaat vanaf lengte (cm)" htmlFor="pr-ovl">
                  <input id="pr-ovl" type="number" step="0.01" value={draft.oversizeLengthCm} onChange={(e) => setDraft((d) => (d ? { ...d, oversizeLengthCm: e.target.value } : d))} />
                </FormField>
                <FormField label="Buitenmaat vanaf breedte (cm)" htmlFor="pr-ovw">
                  <input id="pr-ovw" type="number" step="0.01" value={draft.oversizeWidthCm} onChange={(e) => setDraft((d) => (d ? { ...d, oversizeWidthCm: e.target.value } : d))} />
                </FormField>
                <FormField label="Telt als (factureerbare eenheden)" htmlFor="pr-ovf" hint="Bv. 2: een buitenmaat-pallet telt als 2 palletplaatsen.">
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
          title={agreementDraft.agreement ? `Prijsafspraak bewerken — ${agreementDraft.agreement.name}` : 'Prijsafspraak toevoegen'}
          onClose={() => setAgreementDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setAgreementDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="pricing-agreement-form" disabled={busy}>
                Opslaan
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
              <FormField label="Naam" htmlFor="pa-name" required hint='Bv. "Distributie België 2026-Q4".'>
                <input id="pa-name" value={agreementDraft.name} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
              <FormField label="Minimum per order (€)" htmlFor="pa-min">
                <input id="pa-min" type="number" step="0.01" value={agreementDraft.minimumAmount} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))} />
              </FormField>
              <FormField label="Maximumtarief per order (€)" htmlFor="pa-max" hint="Optioneel: bovengrens na het minimum.">
                <input id="pa-max" type="number" step="0.01" value={agreementDraft.maximumAmount} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, maximumAmount: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Geldig vanaf" htmlFor="pa-from" required>
                <input id="pa-from" type="date" value={agreementDraft.effectiveFrom} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))} />
              </FormField>
              <FormField label="Geldig tot" htmlFor="pa-until" hint="Leeg = onbeperkt.">
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
                Herbruikbare tabel (koppelbaar aan meerdere klanten) — niet gekoppeld aan deze klant; koppel klanten nadien via klantkoppelingen.
              </label>
            )}
            <FormField label="Interne notities" htmlFor="pa-notes" hint="Bv. commerciële achtergrond van de afspraak.">
              <input id="pa-notes" value={agreementDraft.notes} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, notes: e.target.value } : d))} maxLength={2000} />
            </FormField>
            <fieldset className="issued-items-generate-dimension">
              <legend>Automatische toeslagen</legend>
              {agreementDraft.surcharges.map((surcharge, index) => (
                <div key={index} className="issued-items-form-row customer-rule-bracket">
                  <input aria-label={`Toeslag ${index + 1} naam`} placeholder="naam" value={surcharge.name}
                    onChange={(e) => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, name: e.target.value } : s)) } : d))} />
                  <select aria-label={`Toeslag ${index + 1} soort`} value={surcharge.kind}
                    onChange={(e) => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, kind: e.target.value as 'Percent' | 'Fixed' } : s)) } : d))}>
                    <option value="Percent">Percentage</option>
                    <option value="Fixed">Vast bedrag</option>
                  </select>
                  <input aria-label={`Toeslag ${index + 1} waarde`} type="number" step="0.01" placeholder="waarde" value={surcharge.value}
                    onChange={(e) => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, value: e.target.value } : s)) } : d))} />
                  <Button variant="ghost" onClick={() => setAgreementDraft((d) => (d ? { ...d, surcharges: d.surcharges.filter((_, i) => i !== index) } : d))}>
                    Verwijderen
                  </Button>
                </div>
              ))}
              <Button variant="secondary" onClick={() => setAgreementDraft((d) => (d ? { ...d, surcharges: [...d.surcharges, { name: '', kind: 'Percent', value: '' }] } : d))}>
                + Toeslag
              </Button>
            </fieldset>
          </form>
        </Modal>
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

      {deleteAgreementTarget && (
        <ConfirmDialog
          title="Prijsafspraak verwijderen"
          message={`Weet je zeker dat je "${deleteAgreementTarget.name}" wilt verwijderen? Dit kan alleen als er geen tariefregels meer aan hangen.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDeleteAgreement}
          onCancel={() => setDeleteAgreementTarget(null)}
        />
      )}
    </section>
  )
}
