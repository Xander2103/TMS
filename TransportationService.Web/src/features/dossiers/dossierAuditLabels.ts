import type { AuditValueLabels } from '../auditing/formatAuditValues'

/**
 * B25: dossier-specific audit values. A dossier "Closed" means Bevestigd (confirmed), so the
 * generic lifecycle words would mislead; older rows store the status as an ordinal.
 */
const STATUS_WORDS = ['Open', 'Bevestigd', 'Geannuleerd'] as const
const STATUS_BY_NAME: Record<string, string> = { Open: 'Open', Closed: 'Bevestigd', Cancelled: 'Geannuleerd' }

export const dossierAuditLabels: AuditValueLabels = {
  Status: (value) => (typeof value === 'number' ? STATUS_WORDS[value] : typeof value === 'string' ? STATUS_BY_NAME[value] : undefined),
}
