import { describe, expect, it } from 'vitest'
import {
  API_ACCEPTS_VEHICLE_SELECTION_SOURCE,
  EMPTY_VEHICLE_DRAFT,
  canSuggestVehicle,
  vehicleDraftAfterDriverChange,
  vehicleDraftAfterManualPick,
  withVehicleSelectionSource,
} from '../vehicleSelection'
import type { TripInput } from '../types'

describe('vehicleSelection', () => {
  it('only lets a suggestion fill an empty field or replace an earlier suggestion', () => {
    expect(canSuggestVehicle(EMPTY_VEHICLE_DRAFT)).toBe(true)
    expect(canSuggestVehicle({ vehicleId: 'v-1', source: 'Suggested' })).toBe(true)
    expect(canSuggestVehicle({ vehicleId: 'v-1', source: 'Manual' })).toBe(false)
    // Legacy rows have no source: treated as a manual choice.
    expect(canSuggestVehicle({ vehicleId: 'v-1', source: null })).toBe(false)
  })

  it('makes a suggestion follow the driver and leaves a manual choice alone', () => {
    expect(vehicleDraftAfterDriverChange(EMPTY_VEHICLE_DRAFT, 'v-1')).toEqual({ vehicleId: 'v-1', source: 'Suggested' })
    expect(vehicleDraftAfterDriverChange({ vehicleId: 'v-1', source: 'Suggested' }, 'v-2')).toEqual({
      vehicleId: 'v-2',
      source: 'Suggested',
    })
    expect(vehicleDraftAfterDriverChange({ vehicleId: 'v-1', source: 'Suggested' }, null)).toEqual(EMPTY_VEHICLE_DRAFT)
    const manual = { vehicleId: 'v-3', source: 'Manual' as const }
    expect(vehicleDraftAfterDriverChange(manual, 'v-1')).toBe(manual)
    expect(vehicleDraftAfterDriverChange(manual, null)).toBe(manual)
  })

  it('marks a hand-picked vehicle as manual and an emptied field as open for suggestions', () => {
    expect(vehicleDraftAfterManualPick('v-9')).toEqual({ vehicleId: 'v-9', source: 'Manual' })
    expect(vehicleDraftAfterManualPick(null)).toEqual(EMPTY_VEHICLE_DRAFT)
  })

  it('sends how the vehicle was chosen, and no source without a vehicle', () => {
    const input: TripInput = {
      tripDate: '2026-09-22',
      driverId: 'd-1',
      vehicleId: 'v-1',
      trailerId: null,
      plannedStart: null,
      plannedEnd: null,
      notes: null,
      orderIds: [],
      plannedDistanceKm: null,
      plannedEmptyKm: null,
    }
    expect(API_ACCEPTS_VEHICLE_SELECTION_SOURCE).toBe(true)
    expect(withVehicleSelectionSource(input, 'Suggested')).toMatchObject({ vehicleId: 'v-1', vehicleSelectionSource: 'Suggested' })
    expect(withVehicleSelectionSource(input, 'Manual')).toMatchObject({ vehicleSelectionSource: 'Manual' })
    // Legacy rows keep their unknown origin; an emptied vehicle never carries one.
    expect(withVehicleSelectionSource(input, null)).toMatchObject({ vehicleSelectionSource: null })
    expect(withVehicleSelectionSource({ ...input, vehicleId: null }, 'Manual')).toMatchObject({ vehicleSelectionSource: null })
  })
})
