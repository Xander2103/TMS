import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import type { PlanningDriver, PlanningResources, PlanningTrailer, PlanningVehicle } from '../types'

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Resource facts as one searchable line: the same server verdicts the resource cards show. */
function driverIssues(driver: PlanningDriver, t: Translate): string[] {
  return [
    !driver.isActive ? t('planningCenter.resources.inactive') : null,
    driver.isBlocked ? t('planningCenter.resources.blocked') : null,
    driver.employmentRestriction ? t('planningCenter.resources.notDeployable') : null,
    driver.absences.length > 0 ? t('planningCenter.resources.absent', { count: driver.absences.length }) : null,
    driver.qualificationBlocks.length > 0
      ? t('planningCenter.resources.qualificationBlocks', { list: driver.qualificationBlocks.join(', ') })
      : null,
    driver.assignments.length > 0 ? t('planningCenter.resources.trips', { count: driver.assignments.length }) : null,
  ].filter((issue): issue is string => issue !== null)
}

function unitIssues(unit: PlanningVehicle | PlanningTrailer, t: Translate): string[] {
  const unusable = !unit.isActive || unit.operationalStatus !== 'Available' || unit.readiness === 'Blocked'
  return [
    unusable ? `⛔ ${t('planningCenter.resources.notDeployable')}` : unit.readiness === 'Warning' ? '⚠' : null,
    unit.readiness && unit.readiness !== 'Deployable' ? unit.readinessSummary ?? null : null,
    unit.assignments.length > 0 ? t('planningCenter.resources.trips', { count: unit.assignments.length }) : null,
  ].filter((issue): issue is string => issue !== null)
}

export function driverPickerOptions(resources: PlanningResources | null, t: Translate): SearchableSelectOption[] {
  return (resources?.drivers ?? []).map((driver) => ({
    value: driver.id,
    label: `${driver.name} (${driver.driverNumber})`,
    description: driverIssues(driver, t).join(' · ') || t('planningCenter.resources.free'),
  }))
}

export function vehiclePickerOptions(resources: PlanningResources | null, t: Translate): SearchableSelectOption[] {
  return (resources?.vehicles ?? []).map((vehicle) => ({
    value: vehicle.id,
    label: `${vehicle.internalNumber} · ${vehicle.licensePlate}`,
    description: unitIssues(vehicle, t).join(' · ') || t('planningCenter.resources.free'),
  }))
}

export function trailerPickerOptions(resources: PlanningResources | null, t: Translate): SearchableSelectOption[] {
  return (resources?.trailers ?? []).map((trailer) => ({
    value: trailer.id,
    label: `${trailer.internalNumber} · ${trailer.licensePlate}`,
    description: unitIssues(trailer, t).join(' · ') || t('planningCenter.resources.free'),
  }))
}
