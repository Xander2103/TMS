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
  deletePriceRule,
  getCustomerPricingConfig,
  listPriceRules,
  listPricingZones,
  listUnitTypeSettings,
  saveCustomerPricingConfig,
  updatePriceRule,
  type CustomerPricingConfig,
  type PriceRule,
  type PriceRuleBasis,
  type PriceRuleBracketInput,
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
  effectiveFrom: string
  effectiveUntil: string
  unitPrice: string
  minimumAmount: string
  brackets: { from: string; to: string; price: string; extra: string }[]
}

/**
 * Customer-specific unit pricing (spec 7): preferred units, parameterized price rules
 * (per-unit / staffel / gewicht / uur / vast, optioneel per zone) and service-option prices.
 */
export function CustomerUnitPricingPanel({ customerId }: CustomerUnitPricingPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [config, setConfig] = useState<CustomerPricingConfig | null>(null)
  const [rules, setRules] = useState<PriceRule[]>([])
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<RuleDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<PriceRule | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([
      getCustomerPricingConfig(customerId),
      listPriceRules(customerId),
      listUnitTypeSettings().catch(() => [] as UnitTypeSettings[]),
      listPricingZones().catch(() => [] as PricingZone[]),
    ])
      .then(([configData, ruleData, unitData, zoneData]) => {
        setConfig(configData)
        setRules(ruleData)
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

  const preferredIds = new Set(config.preferredUnits.map((u) => u.unitTypeId))

  async function togglePreferred(unitTypeId: string) {
    if (!config) return
    const next = preferredIds.has(unitTypeId)
      ? config.preferredUnits.filter((u) => u.unitTypeId !== unitTypeId).map((u) => u.unitTypeId)
      : [...config.preferredUnits.map((u) => u.unitTypeId), unitTypeId]
    try {
      const saved = await saveCustomerPricingConfig(customerId, {
        preferredUnitTypeIds: next,
        optionPrices: config.serviceOptions.map((o) => ({ serviceOptionId: o.serviceOptionId, value: o.customerValue })),
      })
      setConfig(saved)
    } catch (err) {
      showError(describeApiError(err, 'De voorkeurseenheden konden niet worden opgeslagen.').message)
    }
  }

  async function saveOptionPrice(serviceOptionId: string, raw: string) {
    if (!config) return
    const value = raw.trim() === '' ? null : Number(raw)
    try {
      const saved = await saveCustomerPricingConfig(customerId, {
        preferredUnitTypeIds: config.preferredUnits.map((u) => u.unitTypeId),
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
            effectiveFrom: rule.effectiveFrom,
            effectiveUntil: rule.effectiveUntil ?? '',
            unitPrice: rule.unitPrice !== null ? String(rule.unitPrice) : '',
            minimumAmount: rule.minimumAmount !== null ? String(rule.minimumAmount) : '',
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
            effectiveFrom: new Date().toISOString().slice(0, 10),
            effectiveUntil: '',
            unitPrice: '',
            minimumAmount: '',
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
        name: draft.name.trim(),
        effectiveFrom: draft.effectiveFrom,
        effectiveUntil: draft.effectiveUntil || null,
        isActive: true,
        unitPrice: usesBrackets || draft.unitPrice.trim() === '' ? null : Number(draft.unitPrice),
        minimumAmount: draft.minimumAmount.trim() === '' ? null : Number(draft.minimumAmount),
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

  const usesBrackets = draft?.basis === 'QuantityBracket' || draft?.basis === 'WeightBracket'
  const pricingUnits = units.filter((u) => u.isActive && u.allowForPricing)

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Prijsafspraken per eenheid</h3>
        {canManage && <Button onClick={() => openDraft(null)}>+ Prijsregel</Button>}
      </div>

      {rules.length === 0 && <p className="placeholder-text">Nog geen prijsregels voor deze klant.</p>}
      {rules.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Naam</th>
              <th>Eenheid</th>
              <th>Soort</th>
              <th>Zone</th>
              <th>Geldig</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={rule.id}>
                <td>{rule.name}</td>
                <td>{rule.unitTypeName ?? '—'}</td>
                <td>{PRICE_RULE_BASIS_LABELS[rule.basis]}</td>
                <td>{rule.zoneName ?? 'Alle'}</td>
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
      )}

      <h4>Gebruikelijke eenheden</h4>
      <p className="customer-form-muted">
        Deze eenheden staan bovenaan bij orderinvoer voor deze klant; andere actieve eenheden blijven kiesbaar.
      </p>
      <div className="customer-preferred-units">
        {pricingUnits.map((unit) => (
          <label key={unit.id} className="tof-checkbox">
            <input
              type="checkbox"
              checked={preferredIds.has(unit.id)}
              onChange={() => void togglePreferred(unit.id)}
              disabled={!canManage}
            />
            {unit.name}
          </label>
        ))}
        {pricingUnits.length === 0 && <p className="placeholder-text">Geen eenheden beschikbaar voor prijsafspraken.</p>}
      </div>

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
              <FormField label="Soort" htmlFor="pr-basis">
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
              <FormField label="Eenheid" htmlFor="pr-unit" hint="Alleen een vaste prijs kan zonder eenheid.">
                <select id="pr-unit" value={draft.unitTypeId} onChange={(e) => setDraft((d) => (d ? { ...d, unitTypeId: e.target.value } : d))}>
                  <option value="">— Geen (vaste prijs) —</option>
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
                  label={draft.basis === 'Hourly' ? 'Prijs per uur (€)' : draft.basis === 'Fixed' ? 'Vaste prijs (€)' : 'Prijs per eenheid (€)'}
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
    </section>
  )
}
