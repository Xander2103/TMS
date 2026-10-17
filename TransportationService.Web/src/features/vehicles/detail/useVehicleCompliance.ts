import { useEffect, useState } from 'react'
import { useAuth } from '../../auth/authContextValue'
import { listFleetDocuments } from '../../fleet-documents/api/fleetDocumentsApi'
import { listInspections } from '../../inspections/api/inspectionsApi'
import { listMaintenance } from '../../maintenance/api/maintenanceApi'
import { listDamageReports } from '../../damage/api/damageApi'
import { listTachographCalibrations } from '../../fleet-compliance/tachographApi'
import { listLeasingContracts } from '../../fleet-compliance/leasingApi'
import type { VehicleCompliance } from './vehicleCompliance'

const EMPTY: VehicleCompliance = { documents: null, inspections: null, maintenance: null, damage: null, tachograph: null, leasing: null }

/**
 * Loads the vehicle's compliance lists for the overview — in parallel, each only when the user
 * may see it, each failing on its own (a failed list is `null`, never "nothing wrong"). These
 * are per-vehicle lists (a handful of rows), so no summary endpoint is needed. Re-fetched each
 * time the overview opens, so a document added in another section is reflected on return.
 */
export function useVehicleCompliance(vehicleId: string, enabled: boolean) {
  const { hasPermission } = useAuth()
  const can = {
    documents: hasPermission('fleet_documents.view'),
    inspections: hasPermission('inspections.view'),
    maintenance: hasPermission('maintenance.view'),
    damage: hasPermission('damage_reports.view'),
    tachograph: hasPermission('tachograph.view'),
    // Contract rows (no amounts) are readable with vehicles.view.
    leasing: hasPermission('vehicles.view') || hasPermission('fleet_finance.view'),
  }
  const permissionKey = Object.values(can).join(',')
  const [state, setState] = useState<{ key: string; data: VehicleCompliance } | null>(null)
  const requestKey = `${vehicleId}|${permissionKey}`

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    const optional = <T,>(allowed: boolean, load: () => Promise<T>): Promise<T | null> =>
      allowed ? load().catch(() => null) : Promise.resolve(null)
    Promise.all([
      optional(can.documents, () => listFleetDocuments('vehicle', vehicleId)),
      optional(can.inspections, () => listInspections('vehicle', vehicleId)),
      optional(can.maintenance, () => listMaintenance('vehicle', vehicleId)),
      optional(can.damage, () => listDamageReports('vehicle', vehicleId)),
      optional(can.tachograph, () => listTachographCalibrations(vehicleId)),
      optional(can.leasing, () => listLeasingContracts('vehicle', vehicleId)),
    ]).then(([documents, inspections, maintenance, damage, tachograph, leasing]) => {
      if (!cancelled) setState({ key: requestKey, data: { documents, inspections, maintenance, damage, tachograph, leasing } })
    })
    return () => {
      cancelled = true
    }
    // `can` is captured through permissionKey, which is part of requestKey.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, requestKey])

  const loaded = state?.key === requestKey
  return { data: loaded ? state.data : EMPTY, isLoading: enabled && !loaded }
}
