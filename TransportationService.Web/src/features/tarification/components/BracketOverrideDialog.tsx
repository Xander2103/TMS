import { useState } from 'react'
import { formatCurrency } from '../../../utils/numbers'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { searchActiveCustomerOptions } from '../../customers/api/customerOptions'
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
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [price, setPrice] = useState('')
  const [pricePerExtraUnit, setPricePerExtraUnit] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [effectiveUntil, setEffectiveUntil] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

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
      <p className="placeholder-text">{t('tarification.override.intro', { price: formatCurrency(bracket.price) })}</p>
      <FormField label={t('tarification.common.customer')} required error={!customerId && error ? error : undefined}>
        {/* PERF-H3: searched server-side (name, number, VAT) instead of paging through every customer. */}
        <SearchableSelect
          value={customerId}
          onChange={setCustomerId}
          options={[]}
          onSearch={searchActiveCustomerOptions}
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
            suffix: bracket.pricePerExtraUnit !== null ? ` (${formatCurrency(bracket.pricePerExtraUnit)})` : '',
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
