import { formatDecimal, formatInteger } from '../../../utils/numbers'

/** "11 t" from 11 000 kg, "850 kg" below a tonne, "11,5 t" when the tonnes are not whole. */
export function formatWeightShort(kg: number): string {
  if (kg >= 1000) {
    const tonnes = kg / 1000
    const rounded = Math.round(tonnes * 10) / 10
    return `${Number.isInteger(rounded) ? formatInteger(rounded) : formatDecimal(rounded, 1)} t`
  }
  return `${formatInteger(kg)} kg`
}

/** "Aachen → Düsseldorf"; a missing end reads as "?" so the arrow never dangles. */
export function formatRoute(from: string | null | undefined, to: string | null | undefined): string | null {
  if (!from && !to) return null
  return `${from ?? '?'} → ${to ?? '?'}`
}
