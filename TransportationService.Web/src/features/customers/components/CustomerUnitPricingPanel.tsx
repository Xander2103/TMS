import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  PRICE_RULE_BASIS_LABELS,
  createPriceRule,
  createPricingAgreement,
  deletePriceRule,
  deletePricingAgreement,
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
  oversizeLengthCm: string
  oversizeWidthCm: string
  oversizeBillableFactor: string
  brackets: { from: string; to: string; price: string; extra: string }[]
}

interface AgreementDraft {
  agreement: PricingAgreement | null
  name: string
  effectiveFrom: string
  effectiveUntil: string
  minimumAmount: string
  notes: string
  surcharges: { name: string; kind: 'Percent' | 'Fixed'; value: string }[]
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
      listUnitTypeSettings().catch(() => [] as UnitTypeSettings[]),
      listPricingZones().catch(() => [] as PricingZone[]),
    ])
      .then(([configData, ruleData, agreementData, unitData, zoneData]) => {
        setConfig(configData)
        setRules(ruleData)
        setAgreements(agreementData)
        setUnits(unitData)
        setZones(zoneData)
        setLoadError(null)
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

  async function saveOptionPrice(serviceOptionId: string, raw: string) {
    if (!config) return
    const value = raw.trim() === '' ? null : Number(raw)
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
        optionPrices: config.serviceOptions.map((o) => ({
          serviceOptionId: o.serviceOptionId,
          value: o.serviceOptionId === serviceOptionId ? value : o.customerValue,
        })),
      })
      setConfig(saved)
      showSuccess('Klantprijs opgeslagen.')
    } catch (err) {
      showError(describeApiError(err, 'De klantprijs kon niet worden opgeslagen.').message)
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
    const usesBrackets = draft.basis === 'QuantityBracket' || draft.basis === 'WeightBracket'
    const brackets: PriceRuleBracketInput[] = draft.brackets
      .filter((b) => b.from.trim() !== '')
      .map((b) => ({
        fromQuantity: Number(b.from),
        toQuantity: b.to.trim() === '' ? null : Number(b.to),
        price: Number(b.price) || 0,
        pricePerExtraUnit: b.extra.trim() === '' ? null : Number(b.extra),
      }))
    setBusy(true)
    try {
      const input = {
        customerId,
        unitTypeId: draft.unitTypeId || null,
        basis: draft.basis,
        zoneId: draft.zoneId || null,
        agreementId: draft.agreementId || null,
        name: draft.name.trim(),
        effectiveFrom: draft.effectiveFrom,
        effectiveUntil: draft.effectiveUntil || null,
        isActive: true,
        unitPrice: usesBrackets || draft.unitPrice.trim() === '' ? null : Number(draft.unitPrice),
        minimumAmount: draft.minimumAmount.trim() === '' ? null : Number(draft.minimumAmount),
        baseAmount: draft.baseAmount.trim() === '' ? null : Number(draft.baseAmount),
        priority: Number(draft.priority) || 0,
        oversizeLengthCm: draft.oversizeLengthCm.trim() === '' ? null : Number(draft.oversizeLengthCm),
        oversizeWidthCm: draft.oversizeWidthCm.trim() === '' ? null : Number(draft.oversizeWidthCm),
        oversizeBillableFactor: draft.oversizeBillableFactor.trim() === '' ? null : Number(draft.oversizeBillableFactor),
        brackets: usesBrackets ? brackets : null,
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
            notes: agreement.notes ?? '',
            surcharges: agreement.surcharges.map((s) => ({ name: s.name, kind: s.kind, value: String(s.value) })),
          }
        : {
            agreement: null,
            name: '',
            effectiveFrom: today(),
            effectiveUntil: '',
            minimumAmount: '',
            notes: '',
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
        customerId,
        name: agreementDraft.name.trim(),
        effectiveFrom: agreementDraft.effectiveFrom,
        effectiveUntil: agreementDraft.effectiveUntil || null,
        isActive: true,
        minimumAmount: agreementDraft.minimumAmount.trim() === '' ? null : Number(agreementDraft.minimumAmount),
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

  const usesBrackets = draft?.basis === 'QuantityBracket' || draft?.basis === 'WeightBracket'
  const pricingUnits = units.filter((u) => u.isActive && u.allowForPricing)

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
      {agreements.length === 0 && <p className="placeholder-text">Nog geen prijsafspraken; losse prijsregels blijven mogelijk.</p>}
      {agreements.length > 0 && (
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
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>Dienst</th>
            <th>Standaard</th>
            <th>Klantprijs</th>
          </tr>
        </thead>
        <tbody>
          {config.serviceOptions.map((option) => (
            <tr key={option.serviceOptionId}>
              <td>{option.name}</td>
              <td>{option.kind === 'Percent' ? `${option.defaultValue}%` : `€ ${option.defaultValue.toFixed(2)}`}</td>
              <td>
                <input
                  aria-label={`Klantprijs voor ${option.name}`}
                  type="number"
                  step="0.01"
                  defaultValue={option.customerValue ?? ''}
                  placeholder="standaard"
                  disabled={!canManage}
                  onBlur={(e) => {
                    const raw = e.target.value
                    const current = option.customerValue === null ? '' : String(option.customerValue)
                    if (raw !== current) void saveOptionPrice(option.serviceOptionId, raw)
                  }}
                />
              </td>
            </tr>
          ))}
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
              <FormField label="Berekeningswijze" htmlFor="pr-basis">
                <select id="pr-basis" value={draft.basis} onChange={(e) => setDraft((d) => (d ? { ...d, basis: e.target.value as PriceRuleBasis } : d))}>
                  {Object.entries(PRICE_RULE_BASIS_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Eenheid" htmlFor="pr-unit" hint="Order-brede regels (vast/km/pallet/ton) kunnen zonder eenheid.">
                <select id="pr-unit" value={draft.unitTypeId} onChange={(e) => setDraft((d) => (d ? { ...d, unitTypeId: e.target.value } : d))}>
                  <option value="">— Geen (order-breed) —</option>
                  {pricingUnits.map((unit) => (
                    <option key={unit.id} value={unit.id}>
                      {unit.name}
                    </option>
                  ))}
                </select>
              </FormField>
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
            {!usesBrackets && (
              <div className="issued-items-form-row">
                <FormField
                  label={
                    draft.basis === 'Hourly'
                      ? 'Prijs per uur (€)'
                      : draft.basis === 'Fixed'
                        ? 'Vaste prijs (€)'
                        : draft.basis === 'PerKm'
                          ? 'Prijs per km (€)'
                          : draft.basis === 'PerPallet'
                            ? 'Prijs per pallet (€)'
                            : draft.basis === 'PerTon'
                              ? 'Prijs per ton (€)'
                              : 'Prijs per eenheid (€)'
                  }
                  htmlFor="pr-price"
                  required
                >
                  <input id="pr-price" type="number" step="0.01" value={draft.unitPrice} onChange={(e) => setDraft((d) => (d ? { ...d, unitPrice: e.target.value } : d))} />
                </FormField>
                <FormField label="Minimumbedrag (€)" htmlFor="pr-min">
                  <input id="pr-min" type="number" step="0.01" value={draft.minimumAmount} onChange={(e) => setDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))} />
                </FormField>
              </div>
            )}
            {usesBrackets && (
              <fieldset className="issued-items-generate-dimension">
                <legend>{draft.basis === 'WeightBracket' ? 'Staffels (kg)' : 'Staffels (aantal)'}</legend>
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
            <details>
              <summary>Geavanceerd (basisbedrag & buitenmaat)</summary>
              <div className="issued-items-form-row">
                <FormField label="Basisbedrag (€)" htmlFor="pr-base" hint="Wordt bij het berekende bedrag geteld (bv. basiskost vóór km-prijs).">
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
            </div>
            <div className="issued-items-form-row">
              <FormField label="Geldig vanaf" htmlFor="pa-from" required>
                <input id="pa-from" type="date" value={agreementDraft.effectiveFrom} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))} />
              </FormField>
              <FormField label="Geldig tot" htmlFor="pa-until" hint="Leeg = onbeperkt.">
                <input id="pa-until" type="date" value={agreementDraft.effectiveUntil} onChange={(e) => setAgreementDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))} />
              </FormField>
            </div>
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
