import type { TripInput } from './types'

/**
 * How the vehicle on a trip was chosen (design D1, sprint 2026-09-21).
 * `Suggested` = filled in from the driver's fixed vehicle (`Vehicle.FixedDriverId`);
 * `Manual` = picked by the planner. `null` with a vehicle present is legacy/unknown and is
 * treated as `Manual`, so an existing choice is never silently replaced.
 */
export type VehicleSelectionSource = 'Suggested' | 'Manual'

export interface VehicleDraft {
  vehicleId: string
  source: VehicleSelectionSource | null
}

export const EMPTY_VEHICLE_DRAFT: VehicleDraft = { vehicleId: '', source: null }

/** A suggestion may only fill an empty field or replace an earlier suggestion. */
export function canSuggestVehicle(draft: VehicleDraft): boolean {
  return draft.vehicleId === '' || draft.source === 'Suggested'
}

/**
 * Vehicle draft after the planner changed the driver. `fixedVehicleId` is the new driver's fixed
 * vehicle, or null when there is none (or no driver). A suggestion follows the driver: it is
 * replaced by the new driver's fixed vehicle or dropped; a manual choice always stays.
 */
export function vehicleDraftAfterDriverChange(draft: VehicleDraft, fixedVehicleId: string | null): VehicleDraft {
  if (!canSuggestVehicle(draft)) return draft
  if (fixedVehicleId) return { vehicleId: fixedVehicleId, source: 'Suggested' }
  return draft.vehicleId === '' ? draft : EMPTY_VEHICLE_DRAFT
}

/** Vehicle draft after the planner picked (or cleared) the vehicle by hand. */
export function vehicleDraftAfterManualPick(vehicleId: string | null): VehicleDraft {
  return vehicleId ? { vehicleId, source: 'Manual' } : EMPTY_VEHICLE_DRAFT
}

/**
 * Contract with the API: `PUT /api/trips/{id}`, trip create, `PUT /api/trips/{id}/vehicle` and the
 * dossier plan endpoint accept `vehicleSelectionSource` ('Suggested' | 'Manual' | null) next to
 * the vehicle, and `TripDetail` echoes it back — so a manual choice survives a reload. This
 * function is the only place that decides whether it travels with a trip save.
 */
export const API_ACCEPTS_VEHICLE_SELECTION_SOURCE: boolean = true

export function withVehicleSelectionSource(input: TripInput, source: VehicleSelectionSource | null): TripInput {
  if (!API_ACCEPTS_VEHICLE_SELECTION_SOURCE) return input
  return { ...input, vehicleSelectionSource: input.vehicleId ? source : null }
}
