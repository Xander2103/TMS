/**
 * THE central number/currency formatting layer of the internal app (i18n-wave, §35/§36).
 * Zelfde contract als utils/dates.ts: het DECIMAALTEKEN is een TENANT-INSTELLING
 * (Instellingen → Regionale instellingen, DecimalSeparator "," of "."), géén
 * browserlocale-gok — componenten roepen nooit rechtstreeks toLocaleString voor
 * bedragen/hoeveelheden aan. De groepeerseparator is het spiegelteken van het
 * decimaalteken; alleen de VALUTA-OPMAAK (symboolpositie) volgt de UI-taal:
 *   nl → "€ 1.234,56"   fr → "1 234,56 €"   en → "€1,234.56"-stijl met tenant-tekens.
 * Businesswaarden blijven decimals — dit is presentatie, nooit berekening.
 */

import { getActiveLocale } from '../i18n/activeLocale'

export type DecimalSeparatorPreference = ',' | '.'

let activeSeparator: DecimalSeparatorPreference = ','

export function setDecimalSeparatorPreference(value: string | null | undefined): void {
  if (value === ',' || value === '.') activeSeparator = value
}

export function getDecimalSeparatorPreference(): DecimalSeparatorPreference {
  return activeSeparator
}

/** Back to the built-in default; called on a session change and by tests (see utils/dates). */
export function resetDecimalSeparatorPreference(): void {
  activeSeparator = ','
}

function groupingSeparator(): string {
  // FR gebruikt conventioneel een spatie als duizendtal; NL '.'/EN ',' spiegelen het decimaalteken.
  if (getActiveLocale() === 'fr' && activeSeparator === ',') return ' '
  return activeSeparator === ',' ? '.' : ','
}

/** Kale decimale weergave volgens tenant-instelling: 1234.5 → "1.234,50" (bij komma). */
export function formatDecimal(value: number | null | undefined, fractionDigits = 2): string {
  if (value == null || Number.isNaN(value)) return ''
  const negative = value < 0
  const fixed = Math.abs(value).toFixed(fractionDigits)
  const [integerPart, fractionPart] = fixed.split('.')
  const grouped = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, groupingSeparator())
  const body = fractionPart ? `${grouped}${activeSeparator}${fractionPart}` : grouped
  return negative ? `-${body}` : body
}

/** Geheel getal met groepering: 12345 → "12.345". */
export function formatInteger(value: number | null | undefined): string {
  return formatDecimal(value, 0)
}

/** Percentage: 12.5 → "12,5%" (waarde is al in procenten, geen fractie). */
export function formatPercent(value: number | null | undefined, fractionDigits = 1): string {
  const body = formatDecimal(value, fractionDigits)
  return body === '' ? '' : `${body}%`
}

/** Valuta (EUR-default) met symboolpositie per UI-taal en tenant-cijferconventie. */
export function formatCurrency(value: number | null | undefined, currencySymbol = '€'): string {
  const body = formatDecimal(value, 2)
  if (body === '') return ''
  switch (getActiveLocale()) {
    case 'fr':
      return `${body} ${currencySymbol}`
    case 'en':
      return `${currencySymbol}${body}`
    default:
      return `${currencySymbol} ${body}`
  }
}

/**
 * Hoeveelheid (kg, m³, aantallen, ldm …) volgens tenant-instelling, ZONDER overbodige
 * nullen: 12 → "12", 12.5 → "12,5", 1234.25 → "1.234,25". Max. `maxFractionDigits`
 * decimalen (afgerond), null → ''.
 */
export function formatQuantity(value: number | null | undefined, maxFractionDigits = 3): string {
  if (value == null || Number.isNaN(value)) return ''
  const rounded = Number(Math.abs(value).toFixed(maxFractionDigits))
  const [integerPart, fractionPart = ''] = String(rounded).includes('e')
    ? rounded.toFixed(maxFractionDigits).replace(/\.?0+$/, '').split('.')
    : String(rounded).split('.')
  const grouped = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, groupingSeparator())
  const body = fractionPart ? `${grouped}${activeSeparator}${fractionPart}` : grouped
  return value < 0 && rounded !== 0 ? `-${body}` : body
}
