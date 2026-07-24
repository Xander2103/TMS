export type SurchargeKind = 'Percent' | 'Fixed'

export const SURCHARGE_KIND_LABELS: Record<SurchargeKind, string> = {
  Percent: 'Percentage',
  Fixed: 'Vast bedrag',
}
