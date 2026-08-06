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
  employeeId: string | null
  employeeName: string | null
  validFrom: string | null
  validUntil: string | null
  status: TankCardStatus
  isBlocked: boolean
  blockedReason: string | null
  internalName: string | null
  fuelType: string | null
  dailyLimit: number | null
  weeklyLimit: number | null
  monthlyLimit: number | null
  costCenter: string | null
  notes: string | null
}

/**
 * Create/update payload. `driverId` is deliberately absent: the employee is the canonical link
 * and the backend derives/syncs the driver profile from `employeeId` server-side.
 */
export interface TankCardInput {
  cardNumber: string
  provider: string
  vehicleId: string | null
  employeeId: string | null
  validFrom: string | null
  validUntil: string | null
  internalName: string | null
  fuelType: string | null
  dailyLimit: number | null
  weeklyLimit: number | null
  monthlyLimit: number | null
  costCenter: string | null
  notes: string | null
}

/**
 * Builds an update payload from a fetched card, carrying every field over unchanged except the
 * overrides supplied by the caller. Used by link/unlink flows so a partial edit never wipes the
 * card's other fields.
 */
export function tankCardToInput(card: TankCard, overrides: Partial<TankCardInput> = {}): TankCardInput {
  return {
    cardNumber: card.cardNumber,
    provider: card.provider,
    vehicleId: card.vehicleId,
    employeeId: card.employeeId,
    validFrom: card.validFrom,
    validUntil: card.validUntil,
    internalName: card.internalName,
    fuelType: card.fuelType,
    dailyLimit: card.dailyLimit,
    weeklyLimit: card.weeklyLimit,
    monthlyLimit: card.monthlyLimit,
    costCenter: card.costCenter,
    notes: card.notes,
    ...overrides,
  }
}

/** List display: only the tail of the card number is shown (`•••• 1234`). */
export function maskCardNumber(cardNumber: string): string {
  const digits = cardNumber.replace(/\s/g, '')
  if (digits.length <= 4) return cardNumber
  return `•••• ${digits.slice(-4)}`
}
