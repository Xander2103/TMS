export type TankCardStatus = 'Active' | 'ExpiringSoon' | 'Expired' | 'Blocked'

export const TANK_CARD_STATUS_LABELS: Record<TankCardStatus, string> = {
  Active: 'Actief',
  ExpiringSoon: 'Verloopt binnenkort',
  Expired: 'Verlopen',
  Blocked: 'Geblokkeerd',
}

export const TANK_CARD_STATUSES: TankCardStatus[] = ['Active', 'ExpiringSoon', 'Expired', 'Blocked']

export interface TankCard {
  id: string
  cardNumber: string
  provider: string
  vehicleId: string | null
  vehicleInternalNumber: string | null
  vehicleLicensePlate: string | null
  driverId: string | null
  driverName: string | null
  validFrom: string | null
  validUntil: string | null
  status: TankCardStatus
  isBlocked: boolean
  blockedReason: string | null
  notes: string | null
}

export interface TankCardInput {
  cardNumber: string
  provider: string
  vehicleId: string | null
  driverId: string | null
  validFrom: string | null
  validUntil: string | null
  notes: string | null
}

/** List display: only the tail of the card number is shown (`•••• 1234`). */
export function maskCardNumber(cardNumber: string): string {
  const digits = cardNumber.replace(/\s/g, '')
  if (digits.length <= 4) return cardNumber
  return `•••• ${digits.slice(-4)}`
}
