import { apiClient } from '../../../api/apiClient'
import type { LookupOption } from '../types'

/** `UnitTypeLabelDto`: the label of EVERY unit code ever used (incl. inactive/deleted), for display. */
export interface UnitTypeLabel {
  code: string
  name: string
  isActive: boolean
  allowForOrderEntry: boolean
  allowForPricing: boolean
  isDeleted: boolean
}

/** Active units with `AllowForOrderEntry` — the only codes an order accepts. */
export const listUnitOrderOptions = (): Promise<LookupOption[]> =>
  apiClient.getJson<LookupOption[]>('/api/unit-types/order-options')

export const listUnitLabels = (): Promise<UnitTypeLabel[]> =>
  apiClient.getJson<UnitTypeLabel[]>('/api/unit-types/labels')
