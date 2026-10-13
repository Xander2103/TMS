/**
 * Master sprint 2026-09-21 (D5, contract 4.4) — price per activity.
 * Every value here is DERIVED BY THE SERVER; the client renders it and never recomputes a status.
 */

/** Commercial status of a billable activity; the DTO carries null for a non-billable type. */
export type { ActivityPriceStatus } from '../types'

/** One sales line of a STANDALONE activity. `amount` = quantity × unitPrice, computed by the server. */
export interface DossierActivityPriceLine {
  id: string
  sequence: number
  label: string
  quantity: number
  unit: string | null
  unitPrice: number
  amount: number
  salesCategoryId: string | null
}

/** One line of the id-preserving replace: `id` omitted = new line. The amount is never sent. */
export interface ActivityPriceLineInput {
  id?: string
  label: string
  quantity: number
  unit: string | null
  unitPrice: number
  salesCategoryId?: string | null
}

/**
 * PUT …/activities/{activityId}/price-lines. `version` is the price RECORD token
 * (`DossierActivity.pricingVersion`; null while no record exists). Empty list + `freeConfirmed`
 * = explicitly free; empty list without = not priced.
 */
export interface SaveActivityPriceLinesInput {
  version: string | null
  lines: ActivityPriceLineInput[]
  freeConfirmed: boolean
}
