export type SurchargeKind = 'Percent' | 'Fixed'

export interface RateSurcharge {
  id: string
  name: string
  kind: SurchargeKind
  value: number
}

export interface RateCard {
  id: string
  customerId: string
  customerName: string
  name: string
  currency: string
  effectiveFrom: string
  effectiveUntil: string | null
  baseAmount: number
  perKmRate: number | null
  perPalletRate: number | null
  perTonRate: number | null
  minimumAmount: number | null
  notes: string | null
  surcharges: RateSurcharge[]
}

export interface RateSurchargeInput {
  name: string
  kind: SurchargeKind
  value: number
}

export interface RateCardInput {
  customerId: string
  name: string
  effectiveFrom: string
  effectiveUntil: string | null
  baseAmount: number
  perKmRate: number | null
  perPalletRate: number | null
  perTonRate: number | null
  minimumAmount: number | null
  notes: string | null
  surcharges: RateSurchargeInput[]
}

export interface QuoteLine {
  label: string
  amount: number
}

export interface Quote {
  rateCardId: string
  rateCardName: string
  currency: string
  lines: QuoteLine[]
  total: number
}

export interface QuoteInput {
  customerId: string
  date: string
  distanceKm: number | null
  palletCount: number | null
  weightKg: number | null
}

export const SURCHARGE_KIND_LABELS: Record<SurchargeKind, string> = {
  Percent: 'Percentage',
  Fixed: 'Vast bedrag',
}
