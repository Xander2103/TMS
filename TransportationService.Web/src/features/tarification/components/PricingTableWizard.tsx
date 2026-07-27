import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import {
  createPriceRule,
  createPricingAgreement,
  listPricingZones,
  listUnitTypeMaster,
  type PriceRuleBracketInput,
  type PriceRuleInput,
  type PricingZone,
  type UnitCategory,
  type UnitTypeMaster,
} from '../api/pricingApi'
import './pricingWizard.css'

interface PricingTableWizardProps {
  onClose: () => void
  /** openImport = true when the "Excel-import" card was chosen: the caller should open the import dialog on the new (empty) table. */
  onCreated: (agreementId: string, openImport?: boolean) => void
}

type TemplateId =
  | 'hourly'
  | 'pallet-bracket'
  | 'weight-bracket'
  | 'loading-meter'
  | 'zone-table'
  | 'distance'
  | 'fixed'
  | 'combined'
  | 'blank'
  | 'excel-import'

interface TemplateCard {
  id: TemplateId
  label: string
  description: string
}

const TEMPLATE_CARDS: TemplateCard[] = [
  { id: 'hourly', label: 'Uurtarief', description: 'Eén uurtarief; pas nadien het bedrag aan.' },
  { id: 'pallet-bracket', label: 'Palletstaffel', description: 'Staffel op aantal pallets (1 / 2 / 3 / 4+).' },
  { id: 'weight-bracket', label: 'Gewichtsstaffel', description: 'Staffel op ordergewicht (0-100 / 101-500 / 501-1000 kg).' },
  { id: 'loading-meter', label: 'Laadmeter', description: 'Staffel op laadmeters.' },
  { id: 'zone-table', label: 'Zone-tabel', description: 'Eén staffelregel per leveringszone (max. 10).' },
  { id: 'distance', label: 'Afstand (km)', description: 'Prijs per kilometer, ordergewijs.' },
  { id: 'fixed', label: 'Vaste prijs', description: 'Eén vast bedrag per order.' },
  { id: 'combined', label: 'Gecombineerd', description: 'Meerdere berekeningswijzen door elkaar; regels zelf toevoegen.' },
  { id: 'blank', label: 'Leeg starten', description: 'Geen regels; zelf opbouwen.' },
  { id: 'excel-import', label: 'Excel-import', description: 'Maak een lege tabel aan en importeer de regels meteen uit een Excel-bestand.' },
]

const today = () => new Date().toISOString().slice(0, 10)

function bracket(fromQuantity: number, toQuantity: number | null, price = 0): PriceRuleBracketInput {
  return { fromQuantity, toQuantity, price, pricePerExtraUnit: null, weightToKg: null, volumeToM3: null, loadingMetersTo: null }
}

const quantityBrackets = (): PriceRuleBracketInput[] => [
  bracket(1, 1),
  bracket(2, 2),
  bracket(3, 3),
  bracket(4, null),
]

function findUnit(units: UnitTypeMaster[], codeIncludes: string[], category?: UnitCategory): UnitTypeMaster | null {
  const active = units.filter((u) => u.isActive && u.allowForPricing)
  const byCode = active.find((u) => codeIncludes.some((c) => u.code.toUpperCase().includes(c)))
  if (byCode) return byCode
  if (category) {
    const byCategory = active.find((u) => u.category === category)
    if (byCategory) return byCategory
  }
  return null
}

/**
 * Creates the skeleton price rule(s) for the chosen template on a freshly created agreement.
 * Templates needing a unit that cannot be resolved skip rule creation and report why via
 * `showWarning` — the agreement itself is still created, just without that starter rule.
 */
async function createSkeletonRules(
  agreementId: string,
  agreementCustomerId: string | null,
  template: TemplateId,
  effectiveFrom: string,
  units: UnitTypeMaster[],
  zones: PricingZone[],
  showWarning: (message: string) => void,
): Promise<void> {
  const base = (overrides: Partial<PriceRuleInput> & Pick<PriceRuleInput, 'basis' | 'name'>): PriceRuleInput => ({
    customerId: agreementCustomerId,
    unitTypeId: null,
    zoneId: null,
    effectiveFrom,
    effectiveUntil: null,
    isActive: true,
    unitPrice: null,
    minimumAmount: null,
    brackets: null,
    agreementId,
    ...overrides,
  })

  switch (template) {
    case 'hourly': {
      const unit = findUnit(units, ['UUR', 'HOUR'], 'Time')
      if (!unit) {
        showWarning('Voeg een uurregel toe: er is geen tijd-eenheid (bv. Uur) beschikbaar.')
        return
      }
      await createPriceRule(base({ basis: 'Hourly', name: 'Uurtarief', unitTypeId: unit.id, unitPrice: 0 }))
      return
    }
    case 'pallet-bracket': {
      const unit = findUnit(units, ['PALLET'], 'Packaging')
      if (!unit) {
        showWarning('Voeg een verpakkingseenheid toe (bv. Pallet) om de palletstaffel te gebruiken.')
        return
      }
      await createPriceRule(base({ basis: 'QuantityBracket', name: 'Palletstaffel', unitTypeId: unit.id, brackets: quantityBrackets() }))
      return
    }
    case 'weight-bracket': {
      await createPriceRule(base({
        basis: 'WeightBracket',
        name: 'Gewichtsstaffel',
        brackets: [bracket(0, 100), bracket(101, 500), bracket(501, 1000)],
      }))
      return
    }
    case 'loading-meter': {
      const unit = findUnit(units, ['LOADINGMETER'])
      if (!unit) {
        showWarning('Voeg een eenheid met code LOADINGMETER toe om de laadmeterstaffel te gebruiken.')
        return
      }
      await createPriceRule(base({ basis: 'QuantityBracket', name: 'Laadmeter', unitTypeId: unit.id, brackets: quantityBrackets() }))
      return
    }
    case 'zone-table': {
      const unit = findUnit(units, ['PALLET'], 'Packaging')
      if (!unit) {
        showWarning('Voeg een verpakkingseenheid toe (bv. Pallet) om de zone-tabel te gebruiken.')
        return
      }
      const activeZones = zones.filter((z) => z.isActive).slice(0, 10)
      for (const zone of activeZones) {
        // Rules are created sequentially (one POST each) rather than in parallel.
        await createPriceRule(base({
          basis: 'QuantityBracket', name: `Zone ${zone.code}`, unitTypeId: unit.id, zoneId: zone.id, brackets: quantityBrackets(),
        }))
      }
      return
    }
    case 'distance':
      await createPriceRule(base({ basis: 'PerKm', name: 'Afstand', unitPrice: 0 }))
      return
    case 'fixed':
      await createPriceRule(base({ basis: 'Fixed', name: 'Vaste prijs', unitPrice: 0 }))
      return
    case 'combined':
    case 'blank':
    case 'excel-import':
      // No skeleton rules — 'excel-import' opens the import dialog on the fresh, empty table
      // instead (see PricingTableWizard.submit / PricingTableDetailPage's ?import=1 handling).
      return
  }
}

/**
 * "Nieuwe tarieventabel" wizard: step 1 picks a calculation-basis template (pre-creating skeleton
 * rules with a €0 placeholder), step 2 captures the agreement's identity (name, validity, shared
 * vs. customer-specific). On submit it creates the agreement, then the template's rules, then
 * navigates to the new table's detail page.
 */
export function PricingTableWizard({ onClose, onCreated }: PricingTableWizardProps) {
  const { showError } = useToast()
  const [step, setStep] = useState<1 | 2>(1)
  const [template, setTemplate] = useState<TemplateId | null>(null)
  const [name, setName] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState(today())
  const [isShared, setIsShared] = useState(false)
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [units, setUnits] = useState<UnitTypeMaster[]>([])
  const [zones, setZones] = useState<PricingZone[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
    listUnitTypeMaster().then(setUnits).catch(() => setUnits([]))
    listPricingZones().then(setZones).catch(() => setZones([]))
  }, [])

  function selectTemplate(id: TemplateId) {
    setTemplate(id)
    setName((current) => current || TEMPLATE_CARDS.find((c) => c.id === id)!.label)
    setStep(2)
  }

  async function submit() {
    if (!template) return
    if (!name.trim()) {
      setError('De naam is verplicht.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const agreement = await createPricingAgreement({
        customerId: isShared ? null : customerId,
        name: name.trim(),
        effectiveFrom,
        effectiveUntil: null,
        isActive: true,
        minimumAmount: null,
        notes: null,
        surcharges: null,
        isShared,
      })

      await createSkeletonRules(agreement.id, agreement.customerId, template, effectiveFrom, units, zones, showError)
      onCreated(agreement.id, template === 'excel-import')
    } catch (err) {
      setError(describeApiError(err, 'De tarieventabel kon niet worden aangemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="Nieuwe tarieventabel"
      onClose={onClose}
      busy={busy}
      footer={
        step === 1 ? (
          <Button variant="secondary" onClick={onClose}>
            Annuleren
          </Button>
        ) : (
          <>
            <Button variant="secondary" onClick={() => setStep(1)} disabled={busy}>
              Terug
            </Button>
            <Button onClick={() => void submit()} disabled={busy}>
              {busy ? 'Bezig...' : 'Aanmaken'}
            </Button>
          </>
        )
      }
    >
      {step === 1 && (
        <>
          <p className="customer-form-muted">Hoe wil je de prijs berekenen?</p>
          <div className="pricing-wizard-cards">
            {TEMPLATE_CARDS.map((card) => (
              <button
                key={card.id}
                type="button"
                className="pricing-wizard-card"
                onClick={() => selectTemplate(card.id)}
              >
                <strong>{card.label}</strong>
                <span>{card.description}</span>
              </button>
            ))}
          </div>
        </>
      )}

      {step === 2 && (
        <form
          id="pricing-wizard-step2"
          className="issued-items-form"
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
          noValidate
        >
          {error && (
            <div className="issued-items-form-error" role="alert">
              {error}
            </div>
          )}
          <FormField label="Naam" htmlFor="wizard-name" required>
            <input id="wizard-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
          </FormField>
          <div className="issued-items-form-row">
            <FormField label="Geldig vanaf" htmlFor="wizard-from" required>
              <input
                id="wizard-from"
                type="date"
                value={effectiveFrom}
                onChange={(e) => setEffectiveFrom(e.target.value)}
              />
            </FormField>
          </div>
          <label className="tof-checkbox">
            <input
              type="checkbox"
              checked={isShared}
              onChange={(e) => {
                setIsShared(e.target.checked)
                if (e.target.checked) setCustomerId(null)
              }}
            />
            Herbruikbare tabel (koppelbaar aan meerdere klanten via klantkoppelingen)
          </label>
          {!isShared && (
            <FormField
              label="Klant"
              htmlFor="wizard-customer"
              hint="Optioneel: leeg laten maakt dit de algemene (bedrijfsbrede) standaardtabel."
            >
              <SearchableSelect
                id="wizard-customer"
                value={customerId}
                onChange={setCustomerId}
                options={customerOptions}
                placeholder="— Algemene standaardtabel —"
              />
            </FormField>
          )}
        </form>
      )}
    </Modal>
  )
}
