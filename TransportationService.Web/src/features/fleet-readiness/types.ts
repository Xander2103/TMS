/** FL-H1/H2/H4: the server's fleet readiness verdict for one asset on one day. */
export type ReadinessLevel = 'Deployable' | 'Warning' | 'Blocked'
export type ReadinessSeverity = 'Blocking' | 'Warning' | 'Info'
export type FleetAssetKind = 'vehicle' | 'trailer'

export interface ReadinessIssue {
  code: string
  message: string
  severity: ReadinessSeverity
  from?: string | null
  until?: string | null
  /** Set when the reason only matters for specific work (e.g. ADR orders). */
  conditional?: string | null
}

export interface AssetReadiness {
  assetId: string
  assetKind: string
  internalNumber: string
  licensePlate: string
  date: string
  level: ReadinessLevel
  deployable: boolean
  issues: ReadinessIssue[]
  summary: string
}

export interface TripReference {
  id: string
  tripNumber: string
  tripDate: string
  status: string
}

/** FL-B1/M8: what still points at an asset — delete guard and deactivation warning. */
export interface AssetReferences {
  plannedTrips: number
  completedTrips: number
  otherTrips: number
  maintenance: number
  damage: number
  documents: number
  inspections: number
  fuel: number
  leasing: number
  tachograph: number
  tankCards: number
  isFixedDriverVehicle: boolean
  isCurrentDriverVehicle: boolean
  upcomingTrips: TripReference[]
  totalTrips: number
  hasReferences: boolean
}
