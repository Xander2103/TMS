import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { PICKER_PAGE_SIZE } from '../../customers/api/customerOptions'
import { searchTankCards } from './tankCardsApi'
import { maskCardNumber, type TankCard, type TankCardStatus } from '../types'

export interface TankCardOptionFilters {
  /** Unassigned, unblocked, non-expired cards only (free to link). */
  available?: boolean
  status?: TankCardStatus
}

/** "•••• 1234 (Shell)" plus who or what the card is on, so the planner picks the right one. */
export function tankCardOptionLabel(card: Pick<TankCard, 'cardNumber' | 'provider'>): string {
  return `${maskCardNumber(card.cardNumber)} (${card.provider})`
}

export function toTankCardOption(card: TankCard): SearchableSelectOption {
  return {
    value: card.id,
    label: tankCardOptionLabel(card),
    subtitle: [card.vehicleInternalNumber, card.driverName ?? card.employeeName].filter(Boolean).join(' · ') || undefined,
    keywords: card.cardNumber,
  }
}

/** PERF-H3: one page of tank cards matching `query` (card number, provider). */
export async function searchTankCardOptions(
  query: string,
  signal: AbortSignal,
  filters: TankCardOptionFilters = {},
): Promise<SearchableSelectOption[]> {
  const page = await searchTankCards(
    { search: query.trim() || undefined, available: filters.available, status: filters.status, page: 1, pageSize: PICKER_PAGE_SIZE },
    signal,
  )
  return page.items.map(toTankCardOption)
}
