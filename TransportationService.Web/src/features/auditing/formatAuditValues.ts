import { formatDate, formatDateTime } from '../../utils/dates'
import { humanizeCode } from '../../utils/labels'

/**
 * Formats one audit old/new-values JSON blob for display, masking values of sensitive keys.
 *
 * Defence-in-depth: the backend already writes confidential employee fields partially masked
 * into the audit log (EmployeeService.MaskConfidential) and never audits password/token
 * material by convention. This display-side mask catches anything that slips through for
 * viewers who hold audit_logs.view but not the confidential-data permissions.
 *
 * B32: the stored JSON is a developer artefact (property names, GUIDs, enum codes). What the
 * user reads is a humanized version: Dutch field labels where known (sentence-cased property
 * names otherwise), references shown as "verwijzing" instead of a GUID, codes as words, dates
 * in the tenant notation, booleans as ja/nee. The raw payload is never shown, never mutated.
 */
const SENSITIVE_KEY_PATTERN =
  /password|secret|token|securitystamp|iban|bic|nationalregister|identitycard|creditcard/

const MASK = '••••'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const ISO_DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/
/** PascalCase / camelCase / UPPER_SNAKE identifiers and long all-caps codes (enum codes); plain prose and short acronyms (ADR, BE) never match. */
const CODE_LIKE = /^(?:[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+|[A-Z0-9]+(?:_[A-Z0-9]+)+|[a-z0-9]+(?:_[a-z0-9]+)+|[A-Z]{6,})$/

/** Dutch labels for the property names that recur in audit payloads across entities. */
const FIELD_LABELS: Record<string, string> = {
  Name: 'Naam', Title: 'Titel', Label: 'Label', Description: 'Omschrijving', Notes: 'Notities', Note: 'Notitie',
  Status: 'Status', Type: 'Type', Severity: 'Ernst', Reason: 'Reden', Trigger: 'Aanleiding',
  IsActive: 'Actief', Active: 'Actief', IsBlocked: 'Geblokkeerd',
  CustomerId: 'Klant', CustomerName: 'Klant', LegalEntityId: 'Facturerende entiteit', EmployeeId: 'Medewerker',
  DriverId: 'Chauffeur', VehicleId: 'Voertuig', TrailerId: 'Oplegger', TripId: 'Rit', TransportOrderId: 'Transportopdracht',
  LinkedTransportOrderId: 'Transportopdracht', DossierId: 'Dossier', DossierNumber: 'Dossiernummer', OrderNumber: 'Opdrachtnummer',
  DossierDate: 'Dossierdatum', OrderDate: 'Opdrachtdatum', TripDate: 'Ritdatum', PlannedDate: 'Geplande datum',
  ConfirmedAt: 'Bevestigd op', CancelledAt: 'Geannuleerd op', CreatedAt: 'Aangemaakt op', UpdatedAt: 'Bijgewerkt op',
  ActivityType: 'Activiteitstype', ActivityTypeCode: 'Activiteitstype', Template: 'Sjabloon', Sequence: 'Volgorde',
  CompletedOrders: 'Afgeronde opdrachten', ExecutedActivities: 'Uitgevoerde activiteiten',
  CustomerReference: 'Klantreferentie', Reference: 'Referentie', AgreedPrice: 'Afgesproken prijs', Price: 'Prijs',
  Email: 'E-mail', Phone: 'Telefoon', Address: 'Adres', City: 'Gemeente', PostalCode: 'Postcode', Country: 'Land',
  LicensePlate: 'Kenteken', Quantity: 'Aantal', Weight: 'Gewicht', Id: 'Referentie',
}

/** Optional per-field value renderers (an entity can map its enum ordinals or codes to labels). */
export type AuditValueLabels = Record<string, (value: unknown) => string | undefined>

export function isSensitiveAuditKey(key: string): boolean {
  return SENSITIVE_KEY_PATTERN.test(key.toLowerCase().replace(/[^a-z0-9]/gi, ''))
}

/** "LinkedTransportOrderId" → "Transportopdracht"; unknown "PlannedEmptyKm" → "Planned empty km". */
export function auditFieldLabel(key: string): string {
  if (FIELD_LABELS[key]) return FIELD_LABELS[key]
  const base = key.endsWith('Id') && key.length > 2 ? key.slice(0, -2) : key
  if (FIELD_LABELS[base]) return FIELD_LABELS[base]
  return humanizeCode(base)
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'ja' : 'nee'
  if (typeof value === 'number') return String(value)
  if (typeof value === 'object') return '…'
  const text = String(value)
  if (GUID.test(text)) return 'verwijzing'
  if (ISO_DATE_TIME.test(text)) return formatDateTime(text) || text
  if (ISO_DATE.test(text)) return formatDate(text) || text
  if (CODE_LIKE.test(text)) return humanizeCode(text)
  return text
}

export function formatAuditValues(json: string | null, valueLabels?: AuditValueLabels): string {
  if (!json) return ''
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>
    return Object.entries(parsed)
      .map(([key, value]) => {
        const label = auditFieldLabel(key)
        if (value !== null && value !== undefined && value !== '' && isSensitiveAuditKey(key)) {
          return `${label}: ${MASK}`
        }
        const custom = valueLabels?.[key]?.(value)
        return `${label}: ${custom ?? formatValue(value)}`
      })
      .join(' · ')
  } catch {
    return json
  }
}
