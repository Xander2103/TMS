import { useEffect, useState } from 'react'
import { getTrailerOptions } from '../trailers/api/trailersApi'
import type { TrailerOption } from '../trailers/types'
import { getVehicleOptions } from '../vehicles/api/vehiclesApi'
import type { VehicleOption } from '../vehicles/types'

export type ResourceListKey = 'vehicles' | 'trailers'
/** Per picker: the list is still loading, usable, or failed (shown inside the dropdown). */
export type ResourceListStatus = 'loading' | 'ready' | 'error'

export interface PlanningResources {
  vehicles: VehicleOption[]
  trailers: TrailerOption[]
  status: Record<ResourceListKey, ResourceListStatus>
}

/**
 * Vehicles and trailers for the assignment pickers. Each list settles on its own, so one failing
 * endpoint never blocks the other. `enabled = false` (read-only host) loads nothing.
 * `availabilityOn` (yyyy-MM-dd, the trip date) makes the options carry the fleet readiness of
 * THAT day (planned maintenance window, expired inspection, …) — FL-H1/H2/H4.
 *
 * PERF-H3: drivers are no longer preloaded here (the first 200 active drivers, which hid every
 * later one). The driver pickers search the server per keystroke through
 * {@link import('./resourceOptions').driverOptionSearch}, which evaluates availability on the same day.
 */
export function usePlanningResources(enabled = true, availabilityOn?: string | null): PlanningResources {
  const [vehicles, setVehicles] = useState<VehicleOption[]>([])
  const [trailers, setTrailers] = useState<TrailerOption[]>([])
  const [status, setStatus] = useState<Record<ResourceListKey, ResourceListStatus>>({
    vehicles: 'loading',
    trailers: 'loading',
  })

  useEffect(() => {
    if (!enabled) return
    let mounted = true
    const settle = (key: ResourceListKey, next: ResourceListStatus) => {
      if (mounted) setStatus((current) => ({ ...current, [key]: next }))
    }
    getVehicleOptions(availabilityOn)
      .then((data) => {
        if (mounted) setVehicles(data)
        settle('vehicles', 'ready')
      })
      .catch(() => settle('vehicles', 'error'))
    getTrailerOptions(availabilityOn)
      .then((data) => {
        if (mounted) setTrailers(data)
        settle('trailers', 'ready')
      })
      .catch(() => settle('trailers', 'error'))
    return () => {
      mounted = false
    }
  }, [enabled, availabilityOn])

  return { vehicles, trailers, status }
}
