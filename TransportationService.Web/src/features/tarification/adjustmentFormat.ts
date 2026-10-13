import { formatCurrency } from '../../utils/numbers'

/** Shared euro formatting for price-adjustment previews (customer- and agreement-scoped panels); delegates to the central tenant-aware helper. */
export const formatEuro = (value: number): string => formatCurrency(value)

/** "+4%" / "-5%" / "+€ 5,00" / "—" — percent XOR amountDelta, shared by both adjustment panels. */
export function adjustmentSummary(a: { percent: number | null; amountDelta: number | null }): string {
  if (a.percent !== null) return `${a.percent > 0 ? '+' : ''}${a.percent}%`
  if (a.amountDelta !== null) return `${a.amountDelta > 0 ? '+' : ''}${formatEuro(a.amountDelta)}`
  return '—'
}
