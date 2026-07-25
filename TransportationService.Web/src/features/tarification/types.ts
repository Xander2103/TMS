export type SurchargeKind = 'Percent' | 'Fixed' | 'PerHour' | 'PerStop'

export const SURCHARGE_KIND_LABELS: Record<SurchargeKind, string> = {
  Fixed: 'Vast bedrag',
  Percent: 'Percentage',
  PerHour: 'Per uur',
  PerStop: 'Per stop',
}
