import type { SurchargeKind } from './types'

/**
 * "€ 15.00", "8%", "€ 45.00/uur", "€ 1.25/colli" (unitName), "€ 3.00/dag" ... depending on the
 * pricing method. `unitName` is only used for PerUnit (falls back to "eenheid" when unknown).
 */
export const formatServiceValue = (kind: SurchargeKind, value: number, unitName?: string | null): string => {
  switch (kind) {
    case 'Percent':
      return `${value}%`
    case 'PerHour':
      return `€ ${value.toFixed(2)}/uur`
    case 'PerStop':
      return `€ ${value.toFixed(2)}/stop`
    case 'PerUnit':
      return `€ ${value.toFixed(2)}/${unitName ?? 'eenheid'}`
    case 'PerOrderLine':
      return `€ ${value.toFixed(2)}/orderlijn`
    case 'PerKg':
      return `€ ${value.toFixed(2)}/kg`
    case 'PerM3':
      return `€ ${value.toFixed(2)}/m³`
    case 'PerLdm':
      return `€ ${value.toFixed(2)}/ldm`
    case 'PerDay':
      return `€ ${value.toFixed(2)}/dag`
    case 'PerPalletDay':
      return `€ ${value.toFixed(2)}/pallet-dag`
    case 'PerKm':
      return `€ ${value.toFixed(2)}/km`
    default:
      return `€ ${value.toFixed(2)}`
  }
}
