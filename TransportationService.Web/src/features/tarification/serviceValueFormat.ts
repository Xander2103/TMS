import type { SurchargeKind } from './types'

/** "€ 15.00", "8%", "€ 45.00/uur" or "€ 20.00/stop" depending on the pricing method. */
export const formatServiceValue = (kind: SurchargeKind, value: number): string => {
  switch (kind) {
    case 'Percent':
      return `${value}%`
    case 'PerHour':
      return `€ ${value.toFixed(2)}/uur`
    case 'PerStop':
      return `€ ${value.toFixed(2)}/stop`
    default:
      return `€ ${value.toFixed(2)}`
  }
}
