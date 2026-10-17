import { useEffect, useState } from 'react'
import { searchDrivers } from '../drivers/api/driversApi'
import type { DriverListItem } from '../drivers/types'
import { getTrailerOptions } from '../trailers/api/trailersApi'
import type { TrailerOption } from '../trailers/types'
import { getVehicleOptions } from '../vehicles/api/vehiclesApi'
import type { VehicleOption } from '../vehicles/types'

export type ResourceListKey = 'drivers' | 'vehicles' | 'trailers'
/** Per picker: the list is still loading, usable, or failed (shown inside the dropdown). */
export type ResourceListStatus = 'loading' | 'ready' | 'error'

export interface PlanningResources {
  drivers: DriverListItem[]
  vehicles: VehicleOption[]
  trailers: TrailerOption[]
  status: Record<ResourceListKey, ResourceListStatus>
}

/**
 * Active drivers, vehicles and trailers for the assignment pickers. Each list settles on its own,
 * so one failing endpoint never blocks the other two. `enabled = false` (read-only host) loads
 * nothing. `availabilityOn` (yyyy-MM-dd, the trip date) makes each driver's availability reflect
 * THAT day — an approved absence or a non-deployable employment shows before selection (HR
 * acceptance P2-7); the driver list reloads when the date changes. The same date makes the vehicle
 * and trailer options carry the fleet readiness of THAT day (planned maintenance window, expired
 * inspection, …) — FL-H1/H2/H4.
 */
export function usePlanningResources(enabled = true, availabilityOn?: string | null): PlanningResources {
  const [drivers, setDrivers] = useState<DriverListItem[]>([])
  const [vehicles, setVehicles] = useState<VehicleOption[]>([])
  const [trailers, setTrailers] = useState<TrailerOption[]>([])
  const [status, setStatus] = useState<Record<ResourceListKey, ResourceListStatus>>({
    drivers: 'loading',
    vehicles: 'loading',
    trailers: 'loading',
  })

  useEffect(() => {
    if (!enabled) return
    let mounted = true
    searchDrivers({ isActive: true, page: 1, pageSize: 200, availabilityOn: availabilityOn || undefined })
      .then((data) => {
        if (!mounted) return
        setDrivers(data.items)
        setStatus((current) => ({ ...current, drivers: 'ready' }))
      })
      .catch(() => {
        if (mounted) setStatus((current) => ({ ...current, drivers: 'error' }))
      })
    return () => {
      mounted = false
    }
  }, [enabled, availabilityOn])

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

  return { drivers, vehicles, trailers, status }
}
