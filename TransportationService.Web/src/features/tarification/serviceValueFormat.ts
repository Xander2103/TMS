import type { SurchargeKind } from './types'
import { translate } from '../../i18n/translations'
import { formatCurrency } from '../../utils/numbers'

type TranslateLike = (key: string, params?: Record<string, string | number>) => string

/** NL-default zodat bestaande callers zonder `t` (customers/transport-orders) byte-identiek blijven renderen. */
const nlT: TranslateLike = (key, params) => translate('nl', key, params)

/**
 * "€ 15.00", "8%", "€ 45.00/uur", "€ 1.25/colli" (unitName), "€ 3.00/dag" ... depending on the
 * pricing method. `unitName` is only used for PerUnit (falls back to the translated "eenheid"
 * when unknown). Pass the active `t` for a localised suffix; omitting it keeps Dutch output.
 */
export const formatServiceValue = (
  kind: SurchargeKind,
  value: number,
  unitName?: string | null,
  t: TranslateLike = nlT,
): string => {
  switch (kind) {
    case 'Percent':
      return `${value}%`
    case 'PerHour':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.hour')}`
    case 'PerStop':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.stop')}`
    case 'PerUnit':
      return `${formatCurrency(value)}/${unitName ?? t('tarification.unitSuffix.fallback')}`
    case 'PerOrderLine':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.orderLine')}`
    case 'PerKg':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.kg')}`
    case 'PerM3':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.m3')}`
    case 'PerLdm':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.ldm')}`
    case 'PerDay':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.day')}`
    case 'PerPalletDay':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.palletDay')}`
    case 'PerKm':
      return `${formatCurrency(value)}/${t('tarification.unitSuffix.km')}`
    default:
      return `${formatCurrency(value)}`
  }
}
