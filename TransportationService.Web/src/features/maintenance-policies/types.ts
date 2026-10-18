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
  /** MD-M11: the rule's level, explicit. A rule of a deleted vehicle/trailer keeps its asset scope. */
  scope: MaintenancePolicyScope
  /** MD-M11: the vehicle/trailer of an asset-level rule was deleted; the rule applies to nothing. */
  assetDeleted: boolean
}

export type MaintenancePolicyScope = 'CompanyDefault' | 'Category' | 'Vehicle' | 'Trailer'

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

/**
 * Human description of which level a rule targets (resolution: asset > category > company).
 * Driven by the explicit `scope`, never by which name happens to be filled in: a rule of a
 * DELETED vehicle has no current vehicle and used to read as "Bedrijfsstandaard" (MD-M11).
 */
export function policyLevelLabel(
  t: (key: string, params?: Record<string, string | number>) => string,
  policy: Pick<MaintenancePolicy, 'scope' | 'assetDeleted' | 'categoryName' | 'vehicleNumber' | 'trailerNumber'>,
): string {
  const unknown = t('maintenance.policies.levelUnknownName')
  switch (policy.scope) {
    case 'Vehicle': {
      const label = t('maintenance.policies.levelVehicle', { number: policy.vehicleNumber ?? unknown })
      return policy.assetDeleted ? `${label} ${t('maintenance.policies.vehicleDeletedSuffix')}` : label
    }
    case 'Trailer': {
      const label = t('maintenance.policies.levelTrailer', { number: policy.trailerNumber ?? unknown })
      return policy.assetDeleted ? `${label} ${t('maintenance.policies.trailerDeletedSuffix')}` : label
    }
    case 'Category':
      return t('maintenance.policies.levelCategory', { name: policy.categoryName ?? unknown })
    default:
      return t('maintenance.policies.levelCompany')
  }
}
