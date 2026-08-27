export type MaintenancePolicyKind = 'Maintenance' | 'Inspection'
export type FleetAssetKind = 'Vehicle' | 'Trailer'

/** i18n-keys (maintenance.policy.kind.*) — render via t(POLICY_KIND_LABELS[x]). */
export const POLICY_KIND_LABELS: Record<MaintenancePolicyKind, string> = {
  Maintenance: 'maintenance.policy.kind.Maintenance',
  Inspection: 'maintenance.policy.kind.Inspection',
}

/** i18n-keys (maintenance.policy.assetKind.*) — render via t(ASSET_KIND_LABELS[x]). */
export const ASSET_KIND_LABELS: Record<FleetAssetKind, string> = {
  Vehicle: 'maintenance.policy.assetKind.Vehicle',
  Trailer: 'maintenance.policy.assetKind.Trailer',
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

export type MaintenancePolicyLevel = 'Asset' | 'Category' | 'CompanyDefault'

export interface EffectivePolicy {
  policyId: string
  level: MaintenancePolicyLevel
  sourceLabel: string
  intervalMonths: number | null
  intervalKm: number | null
  warningDays: number
  description: string | null
}

export interface EffectivePolicies {
  maintenance: EffectivePolicy | null
  inspection: EffectivePolicy | null
}

/** Human description of which level a rule targets (resolution: asset > category > company). */
export function policyLevelLabel(
  t: (key: string, params?: Record<string, string | number>) => string,
  policy: MaintenancePolicy,
): string {
  if (policy.vehicleNumber) return t('maintenance.policies.levelVehicle', { number: policy.vehicleNumber })
  if (policy.trailerNumber) return t('maintenance.policies.levelTrailer', { number: policy.trailerNumber })
  if (policy.categoryName) return t('maintenance.policies.levelCategory', { name: policy.categoryName })
  return t('maintenance.policies.levelCompany')
}
