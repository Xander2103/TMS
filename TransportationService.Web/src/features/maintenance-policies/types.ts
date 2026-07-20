export type MaintenancePolicyKind = 'Maintenance' | 'Inspection'
export type FleetAssetKind = 'Vehicle' | 'Trailer'

export const POLICY_KIND_LABELS: Record<MaintenancePolicyKind, string> = {
  Maintenance: 'Onderhoud',
  Inspection: 'Keuring',
}

export const ASSET_KIND_LABELS: Record<FleetAssetKind, string> = {
  Vehicle: 'Voertuig',
  Trailer: 'Oplegger',
}

export interface MaintenancePolicy {
  id: string
  kind: MaintenancePolicyKind
  assetKind: FleetAssetKind
  categoryId: string | null
  categoryName: string | null
  vehicleId: string | null
  vehicleNumber: string | null
  trailerId: string | null
  trailerNumber: string | null
  intervalMonths: number | null
  intervalKm: number | null
  warningDays: number
  description: string | null
  isActive: boolean
}

export interface MaintenancePolicyInput {
  kind: MaintenancePolicyKind
  assetKind: FleetAssetKind
  categoryId: string | null
  vehicleId: string | null
  trailerId: string | null
  intervalMonths: number | null
  intervalKm: number | null
  warningDays: number
  description: string | null
  isActive: boolean
}

/** Human description of which level a rule targets (resolution: asset > category > company). */
export function policyLevelLabel(policy: MaintenancePolicy): string {
  if (policy.vehicleNumber) return `Voertuig ${policy.vehicleNumber}`
  if (policy.trailerNumber) return `Oplegger ${policy.trailerNumber}`
  if (policy.categoryName) return `Categorie ${policy.categoryName}`
  return 'Bedrijfsstandaard'
}
