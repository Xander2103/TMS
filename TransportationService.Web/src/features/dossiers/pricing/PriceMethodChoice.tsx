import { useId } from 'react'
import { useLocale } from '../../../i18n/localeContext'

export type PriceMethod = 'agreed' | 'lines' | 'free'

interface PriceMethodChoiceProps {
  value: PriceMethod
  onChange: (method: PriceMethod) => void
  disabled?: boolean
}

/**
 * B13: an unpriced unit asks ONE question — "Prijs bepalen via: afgesproken prijs, verkooplijnen
 * of gratis?" — and the panel then shows only the controls of that answer. The pricing domain is
 * untouched: each answer maps onto the existing commands (one-off/fixed price, sales lines, the
 * explicit free decision).
 */
export function PriceMethodChoice({ value, onChange, disabled = false }: PriceMethodChoiceProps) {
  const { t } = useLocale()
  const name = useId()
  const options: PriceMethod[] = ['agreed', 'lines', 'free']
  return (
    <fieldset className="dossier-price-method" disabled={disabled}>
      <legend>{t('dossierPricing.method.legend')}</legend>
      {options.map((option) => (
        <div key={option} className="dossier-price-method-option" data-checked={value === option || undefined}>
          <input
            id={`${name}-${option}`}
            type="radio"
            name={name}
            value={option}
            checked={value === option}
            onChange={() => onChange(option)}
            aria-describedby={`${name}-${option}-hint`}
          />
          <span>
            <label htmlFor={`${name}-${option}`}>{t(`dossierPricing.method.${option}`)}</label>
            <small id={`${name}-${option}-hint`}>{t(`dossierPricing.method.${option}Hint`)}</small>
          </span>
        </div>
      ))}
    </fieldset>
  )
}
