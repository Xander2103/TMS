import { useEffect, useState } from 'react'
import { getVehicle } from '../vehicles/api/vehiclesApi'

/** `Vehicle.payloadKg` / `Vehicle.tailLiftCapacityKg`; null = unknown ("Capaciteit nog te controleren"). */
export interface VehicleCapacity {
  payloadKg: number | null
  tailLiftCapacityKg: number | null
}

export const UNKNOWN_VEHICLE_CAPACITY: VehicleCapacity = { payloadKg: null, tailLiftCapacityKg: null }

/** One lookup per vehicle per session: capacities are master data that rarely change. */
const cache = new Map<string, VehicleCapacity>()

/** Test hook: start every test from an empty cache. */
export function clearVehicleCapacityCache(): void {
  cache.clear()
}

/**
 * Capacities of the vehicle an activity is planned on (D4), for the goods capacity hint. Cached
 * per vehicle id; a request for a vehicle that is no longer the target is aborted and its answer
 * dropped. A failed or forbidden lookup is simply "unknown" — never a guessed capacity.
 */
export function useVehicleCapacity(vehicleId: string | null): VehicleCapacity {
  const [loaded, setLoaded] = useState<{ vehicleId: string; capacity: VehicleCapacity } | null>(null)

  useEffect(() => {
    if (!vehicleId || cache.has(vehicleId)) return
    const controller = new AbortController()
    getVehicle(vehicleId, controller.signal)
      .then((vehicle) => {
        if (controller.signal.aborted) return
        const capacity: VehicleCapacity = {
          payloadKg: vehicle.payloadKg ?? null,
          tailLiftCapacityKg: vehicle.tailLiftCapacityKg ?? null,
        }
        cache.set(vehicleId, capacity)
        setLoaded({ vehicleId, capacity })
      })
      .catch(() => {
        // Unknown stays unknown; the next mount retries.
      })
    return () => controller.abort()
  }, [vehicleId])

  if (!vehicleId) return UNKNOWN_VEHICLE_CAPACITY
  return cache.get(vehicleId) ?? (loaded?.vehicleId === vehicleId ? loaded.capacity : UNKNOWN_VEHICLE_CAPACITY)
}
