export type SurchargeKind =
  | 'Percent'
  | 'Fixed'
  | 'PerHour'
  | 'PerStop'
  | 'PerUnit'
  | 'PerOrderLine'
  | 'PerKg'
  | 'PerM3'
  | 'PerLdm'
  | 'PerDay'
  | 'PerPalletDay'

export const SURCHARGE_KIND_LABELS: Record<SurchargeKind, string> = {
  Fixed: 'Vast bedrag',
  Percent: 'Percentage',
  PerHour: 'Per uur',
  PerStop: 'Per stop',
  PerUnit: 'Per eenheid…',
  PerOrderLine: 'Per orderlijn',
  PerKg: 'Per kg',
  PerM3: 'Per m³',
  PerLdm: 'Per laadmeter',
  PerDay: 'Per dag',
  PerPalletDay: 'Per pallet/dag',
}

/** Calculation bases that need a managed unit (Kind == 'PerUnit' only, e.g. Colli, Pallet). */
export const SURCHARGE_KIND_NEEDS_UNIT = (kind: SurchargeKind): boolean => kind === 'PerUnit'
