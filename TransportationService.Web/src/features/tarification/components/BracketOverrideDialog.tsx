import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../../customers/api/customersApi'
import { createBracketOverride, type PriceRule, type PriceRuleBracket } from '../api/pricingApi'

interface BracketOverrideDialogProps {
  rule: PriceRule
  bracket: PriceRuleBracket
  onSaved: () => void
  onClose: () => void
}

function bracketLabel(bracket: PriceRuleBracket): string {
  return bracket.toQuantity !== null ? `${bracket.fromQuantity}–${bracket.toQuantity}` : `${bracket.fromQuantity}+`
}

/**
 * "Maak klantafwijking": one customer-specific price for a single bracket row of a shared rule.
 * All other rows keep following the shared table; the engine falls back automatically.
 */
export function BracketOverrideDialog({ rule, bracket, onSaved, onClose }: BracketOverrideDialogProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [customers, setCustomers] = useState<SearchableSelectOption[]>([])
  const [customersLoading, setCustomersLoading] = useState(true)
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [price, setPrice] = useState('')
  const [pricePerExtraUnit, setPricePerExtraUnit] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [effectiveUntil, setEffectiveUntil] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    // The backend caps pages at 200 rows; keep fetching so tenants with more active
    // customers can still pick any of them (hard stop at 5 pages = 1000 as a safety net).
    async function loadAllCustomers() {
      const options: SearchableSelectOption[] = []
      for (let page = 1; page <= 5; page++) {
        const result = await searchCustomers({ page, pageSize: 200, isActive: true })
        options.push(...result.items.map((c) => ({ value: c.id, label: c.name })))
        if (options.length >= result.totalCount) break
      }
      return options
    }
    loadAllCustomers()
      .then((options) => {
        if (mounted) setCustomers(options)
      })
      .catch(() => {
        if (mounted) setCustomers([])
      })
      .finally(() => {
        if (mounted) setCustomersLoading(false)
      })
    return () => {
      mounted = false
    }
  }, [])

  async function submit() {
    if (!customerId) {
      setError(t('tarification.common.chooseCustomer'))
      return
    }
    const parsedPrice = Number(price)
    if (price.trim() === '' || Number.isNaN(parsedPrice) || parsedPrice < 0) {
      setError(t('tarification.override.invalidPrice'))
      return
    }
    const parsedExtra = pricePerExtraUnit.trim() === '' ? null : Number(pricePerExtraUnit)
    if (parsedExtra !== null && (Number.isNaN(parsedExtra) || parsedExtra < 0)) {
      setError(t('tarification.override.invalidExtra'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await createBracketOverride(rule.id, {
        customerId,
        fromQuantity: bracket.fromQuantity,
        toQuantity: bracket.toQuantity,
        weightToKg: bracket.weightToKg,
        volumeToM3: bracket.volumeToM3,
        loadingMetersTo: bracket.loadingMetersTo,
        price: parsedPrice,
        pricePerExtraUnit: parsedExtra,
        effectiveFrom: effectiveFrom || null,
        effectiveUntil: effectiveUntil || null,
      })
      showSuccess(t('tarification.override.added'))
      onSaved()
    } catch (err) {
      const message = localizeApiError(t, err, t('tarification.override.saveError'))
      showError(message)
      setError(message)
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('tarification.override.title', { range: bracketLabel(bracket), name: rule.name })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? t('tarification.override.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <p className="placeholder-text">{t('tarification.override.intro', { price: bracket.price.toFixed(2) })}</p>
      <FormField label={t('tarification.common.customer')} required error={!customerId && error ? error : undefined}>
        <SearchableSelect
          value={customerId}
          onChange={setCustomerId}
          options={customers}
          isLoading={customersLoading}
          ariaLabel={t('tarification.override.customerAria')}
          placeholder={t('tarification.override.customerPlaceholder')}
        />
      </FormField>
      <FormField label={t('tarification.override.priceLabel')} required>
        <input
          type="number"
          step="0.01"
          min="0"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          aria-label={t('tarification.override.ariaPrice')}
        />
      </FormField>
      {bracket.toQuantity === null && (
        <FormField
          label={t('tarification.override.extraLabel')}
          hint={t('tarification.override.extraHint', {
            suffix: bracket.pricePerExtraUnit !== null ? ` (€ ${bracket.pricePerExtraUnit.toFixed(2)})` : '',
          })}
        >
          <input
            type="number"
            step="0.01"
            min="0"
            value={pricePerExtraUnit}
            onChange={(e) => setPricePerExtraUnit(e.target.value)}
            aria-label={t('tarification.override.ariaExtra')}
          />
        </FormField>
      )}
      <FormField label={t('tarification.common.validFrom')} hint={t('tarification.override.fromHint')}>
        <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} aria-label={t('tarification.override.ariaFrom')} />
      </FormField>
      <FormField label={t('tarification.common.validUntil')}>
        <input type="date" value={effectiveUntil} onChange={(e) => setEffectiveUntil(e.target.value)} aria-label={t('tarification.override.ariaUntil')} />
      </FormField>
      {error && customerId && <p role="alert" className="placeholder-text">{error}</p>}
    </Modal>
  )
}
