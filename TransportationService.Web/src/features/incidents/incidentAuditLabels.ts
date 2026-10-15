import { useMemo } from 'react'
import { useLocale } from '../../i18n/localeContext'
import type { AuditValueLabels } from '../auditing/formatAuditValues'
import {
  CHARGE_DECISION_LABELS,
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_STATUS_LABELS,
  INCIDENT_TYPE_LABELS,
  RESPONSIBLE_PARTY_LABELS,
  type IncidentSeverity,
  type IncidentStatus,
  type IncidentType,
} from './types'

function byName(labels: Record<string, string>, t: (key: string) => string) {
  return (value: unknown): string | undefined => (typeof value === 'string' && value in labels ? t(labels[value]) : undefined)
}

/**
 * B24/B25: incident audit rows store enums by name ("InProgress", "Proposed"). The generic
 * lifecycle words would misread them (an incident "InProgress" is "In behandeling", not
 * "Onderweg"), so the incident history maps them with the incident's own labels.
 */
export function useIncidentAuditLabels(): AuditValueLabels {
  const { t } = useLocale()
  return useMemo<AuditValueLabels>(() => {
    const status = byName(INCIDENT_STATUS_LABELS as Record<IncidentStatus, string>, t)
    return {
      Status: status,
      From: status,
      To: status,
      Severity: byName(INCIDENT_SEVERITY_LABELS as Record<IncidentSeverity, string>, t),
      IncidentType: byName(INCIDENT_TYPE_LABELS as Record<IncidentType, string>, t),
      ChargeDecision: byName(CHARGE_DECISION_LABELS, t),
      ResponsibleParty: byName(RESPONSIBLE_PARTY_LABELS, t),
      Mode: (value) => (value === 'Auto' ? t('incidents.history.modeAuto') : value === 'Propose' ? t('incidents.history.modePropose') : undefined),
    }
  }, [t])
}
