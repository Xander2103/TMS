import type { BadgeTone } from '../../components/ui/Badge'
import type { ReadinessIssue, ReadinessLevel, ReadinessSeverity } from './types'

export const READINESS_LEVEL_LABELS: Record<ReadinessLevel, string> = {
  Deployable: 'fleet.readiness.level.Deployable',
  Warning: 'fleet.readiness.level.Warning',
  Blocked: 'fleet.readiness.level.Blocked',
}

export const READINESS_LEVEL_TONES: Record<ReadinessLevel, BadgeTone> = {
  Deployable: 'success',
  Warning: 'warning',
  Blocked: 'danger',
}

export const READINESS_SEVERITY_ICONS: Record<ReadinessSeverity, string> = {
  Blocking: '⛔',
  Warning: '⚠',
  Info: 'ℹ',
}

/**
 * A conditional blocker (expired ADR certificate, crane inspection…) only blocks the work that
 * needs it — Planning applies it per order; on the asset page it reads as a warning.
 */
export function displaySeverity(issue: ReadinessIssue): ReadinessSeverity {
  return issue.conditional && issue.severity === 'Blocking' ? 'Warning' : issue.severity
}

/**
 * Picker suffix for a vehicle/trailer option: "⛔ …" when blocked, "⚠ …" with attention points,
 * nothing when deployable or when the payload carries no readiness (older API).
 */
export function readinessHint(option: { readiness?: ReadinessLevel | null; readinessSummary?: string | null }): string | null {
  if (!option.readiness || option.readiness === 'Deployable') return null
  const icon = option.readiness === 'Blocked' ? '⛔' : '⚠'
  return option.readinessSummary ? `${icon} ${option.readinessSummary}` : icon
}
