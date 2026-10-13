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
  | 'PerKm'

/** Vertaalsleutels — renderen als t(SURCHARGE_KIND_LABELS[kind]). */
export const SURCHARGE_KIND_LABELS: Record<SurchargeKind, string> = {
  Fixed: 'tarification.surchargeKind.Fixed',
  Percent: 'tarification.surchargeKind.Percent',
  PerHour: 'tarification.surchargeKind.PerHour',
  PerStop: 'tarification.surchargeKind.PerStop',
  PerUnit: 'tarification.surchargeKind.PerUnit',
  PerOrderLine: 'tarification.surchargeKind.PerOrderLine',
  PerKg: 'tarification.surchargeKind.PerKg',
  PerM3: 'tarification.surchargeKind.PerM3',
  PerLdm: 'tarification.surchargeKind.PerLdm',
  PerDay: 'tarification.surchargeKind.PerDay',
  PerPalletDay: 'tarification.surchargeKind.PerPalletDay',
  PerKm: 'tarification.surchargeKind.PerKm',
}

/** Calculation bases that need a managed unit (Kind == 'PerUnit' only, e.g. Colli, Pallet). */
export const SURCHARGE_KIND_NEEDS_UNIT = (kind: SurchargeKind): boolean => kind === 'PerUnit'
