import { useLocale } from '../../../i18n/localeContext'
import type { UnitOptionItem } from '../components/UnitSelect'
import type { CargoItem } from '../types'
import { describeCargoItem, unitLabelFrom } from './cargoLabels'

interface CargoLinkSelectProps {
  /** Prefix for the checkbox ids (one select per dialog). */
  idPrefix: string
  cargoItems: CargoItem[]
  units: UnitOptionItem[]
  value: string[]
  onChange: (cargoItemIds: string[]) => void
  disabled?: boolean
}

/**
 * D4: optional link between a sales line and the goods lines it is about. One, several or none —
 * none means the line covers the whole trip/activity. Renders nothing for an order without goods.
 */
export function CargoLinkSelect({ idPrefix, cargoItems, units, value, onChange, disabled }: CargoLinkSelectProps) {
  const { t } = useLocale()
  if (cargoItems.length === 0) return null
  const items = [...cargoItems].sort((a, b) => a.sequence - b.sequence)
  const unitLabel = unitLabelFrom(units)

  function toggle(id: string, checked: boolean) {
    onChange(checked ? [...value.filter((v) => v !== id), id] : value.filter((v) => v !== id))
  }

  return (
    <fieldset className="order-cargo-link">
      <legend>{t('dossierPricing.goodsLink.label')}</legend>
      <p className="order-cargo-link-hint">{t('dossierPricing.goodsLink.hint')}</p>
      {items.map((item) => (
        <label key={item.id} htmlFor={`${idPrefix}-${item.id}`} className="order-cargo-link-option">
          <input
            id={`${idPrefix}-${item.id}`}
            type="checkbox"
            checked={value.includes(item.id)}
            onChange={(event) => toggle(item.id, event.target.checked)}
            disabled={disabled}
          />{' '}
          {describeCargoItem(item, unitLabel)}
        </label>
      ))}
    </fieldset>
  )
}
