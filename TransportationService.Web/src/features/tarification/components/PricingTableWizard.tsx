import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
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
  labelKey: string
  descriptionKey: string
}

const TEMPLATE_CARDS: TemplateCard[] = [
  { id: 'hourly', labelKey: 'tarification.wizard.templates.hourly.label', descriptionKey: 'tarification.wizard.templates.hourly.description' },
  { id: 'pallet-bracket', labelKey: 'tarification.wizard.templates.palletBracket.label', descriptionKey: 'tarification.wizard.templates.palletBracket.description' },
  { id: 'weight-bracket', labelKey: 'tarification.wizard.templates.weightBracket.label', descriptionKey: 'tarification.wizard.templates.weightBracket.description' },
  { id: 'loading-meter', labelKey: 'tarification.wizard.templates.loadingMeter.label', descriptionKey: 'tarification.wizard.templates.loadingMeter.description' },
  { id: 'zone-table', labelKey: 'tarification.wizard.templates.zoneTable.label', descriptionKey: 'tarification.wizard.templates.zoneTable.description' },
  { id: 'distance', labelKey: 'tarification.wizard.templates.distance.label', descriptionKey: 'tarification.wizard.templates.distance.description' },
  { id: 'fixed', labelKey: 'tarification.wizard.templates.fixed.label', descriptionKey: 'tarification.wizard.templates.fixed.description' },
  { id: 'combined', labelKey: 'tarification.wizard.templates.combined.label', descriptionKey: 'tarification.wizard.templates.combined.description' },
  { id: 'blank', labelKey: 'tarification.wizard.templates.blank.label', descriptionKey: 'tarification.wizard.templates.blank.description' },
  { id: 'excel-import', labelKey: 'tarification.wizard.templates.excelImport.label', descriptionKey: 'tarification.wizard.templates.excelImport.description' },
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
 * Skeleton rule names are seeded in the creator's UI language (they are ordinary editable data).
 */
async function createSkeletonRules(
  agreementId: string,
  agreementCustomerId: string | null,
  template: TemplateId,
  effectiveFrom: string,
  units: UnitTypeMaster[],
  zones: PricingZone[],
  showWarning: (message: string) => void,
  t: TranslateFn,
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
        showWarning(t('tarification.wizard.noTimeUnit'))
        return
      }
      await createPriceRule(base({ basis: 'Hourly', name: t('tarification.wizard.ruleNameHourly'), unitTypeId: unit.id, unitPrice: 0 }))
      return
    }
    case 'pallet-bracket': {
      const unit = findUnit(units, ['PALLET'], 'Packaging')
      if (!unit) {
        showWarning(t('tarification.wizard.noPackagingUnitPallet'))
        return
      }
      await createPriceRule(base({ basis: 'QuantityBracket', name: t('tarification.wizard.ruleNamePalletBracket'), unitTypeId: unit.id, brackets: quantityBrackets() }))
      return
    }
    case 'weight-bracket': {
      await createPriceRule(base({
        basis: 'WeightBracket',
        name: t('tarification.wizard.ruleNameWeightBracket'),
        brackets: [bracket(0, 100), bracket(101, 500), bracket(501, 1000)],
      }))
      return
    }
    case 'loading-meter': {
      const unit = findUnit(units, ['LOADINGMETER'])
      if (!unit) {
        showWarning(t('tarification.wizard.noLoadingMeterUnit'))
        return
      }
      await createPriceRule(base({ basis: 'QuantityBracket', name: t('tarification.wizard.ruleNameLoadingMeter'), unitTypeId: unit.id, brackets: quantityBrackets() }))
      return
    }
    case 'zone-table': {
      const unit = findUnit(units, ['PALLET'], 'Packaging')
      if (!unit) {
        showWarning(t('tarification.wizard.noPackagingUnitZone'))
        return
      }
      const activeZones = zones.filter((z) => z.isActive).slice(0, 10)
      for (const zone of activeZones) {
        // Rules are created sequentially (one POST each) rather than in parallel.
        await createPriceRule(base({
          basis: 'QuantityBracket', name: t('tarification.wizard.ruleNameZone', { code: zone.code }), unitTypeId: unit.id, zoneId: zone.id, brackets: quantityBrackets(),
        }))
      }
      return
    }
    case 'distance':
      await createPriceRule(base({ basis: 'PerKm', name: t('tarification.wizard.ruleNameDistance'), unitPrice: 0 }))
      return
    case 'fixed':
      await createPriceRule(base({ basis: 'Fixed', name: t('tarification.wizard.ruleNameFixed'), unitPrice: 0 }))
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
  const { t } = useLocale()
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
    setName((current) => current || t(TEMPLATE_CARDS.find((c) => c.id === id)!.labelKey))
    setStep(2)
  }

  async function submit() {
    if (!template) return
    if (!name.trim()) {
      setError(t('tarification.common.nameRequired'))
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

      await createSkeletonRules(agreement.id, agreement.customerId, template, effectiveFrom, units, zones, showError, t)
      onCreated(agreement.id, template === 'excel-import')
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.wizard.createError')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('tarification.wizard.title')}
      onClose={onClose}
      busy={busy}
      footer={
        step === 1 ? (
          <Button variant="secondary" onClick={onClose}>
            {t('ui.actions.cancel')}
          </Button>
        ) : (
          <>
            <Button variant="secondary" onClick={() => setStep(1)} disabled={busy}>
              {t('ui.actions.back')}
            </Button>
            <Button onClick={() => void submit()} disabled={busy}>
              {busy ? t('ui.actions.busy') : t('tarification.common.create')}
            </Button>
          </>
        )
      }
    >
      {step === 1 && (
        <>
          <p className="customer-form-muted">{t('tarification.wizard.question')}</p>
          <div className="pricing-wizard-cards">
            {TEMPLATE_CARDS.map((card) => (
              <button
                key={card.id}
                type="button"
                className="pricing-wizard-card"
                onClick={() => selectTemplate(card.id)}
              >
                <strong>{t(card.labelKey)}</strong>
                <span>{t(card.descriptionKey)}</span>
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
          <FormField label={t('tarification.common.name')} htmlFor="wizard-name" required>
            <input id="wizard-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
          </FormField>
          <div className="issued-items-form-row">
            <FormField label={t('tarification.wizard.validFrom')} htmlFor="wizard-from" required>
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
            {t('tarification.wizard.sharedCheckbox')}
          </label>
          {!isShared && (
            <FormField
              label={t('tarification.common.customer')}
              htmlFor="wizard-customer"
              hint={t('tarification.wizard.customerHint')}
            >
              <SearchableSelect
                id="wizard-customer"
                value={customerId}
                onChange={setCustomerId}
                options={customerOptions}
                placeholder={t('tarification.wizard.customerPlaceholder')}
              />
            </FormField>
          )}
        </form>
      )}
    </Modal>
  )
}
