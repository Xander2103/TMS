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
  // B25: the remaining recurring keys that read as English before.
  ResponsibleUserId: 'Verantwoordelijke', ConfirmedByUserId: 'Bevestigd door', AssignedToUserId: 'Toegewezen aan',
  TargetDossierId: 'Gekoppeld dossier', SourceDossierId: 'Dossier', ActivityTypeId: 'Activiteitstype', ActivityId: 'Activiteit',
  DurationHours: 'Duur (uren)', PlannedStart: 'Geplande start', PlannedEnd: 'Gepland einde', FixedAmount: 'Vast bedrag',
  FreeConfirmed: 'Gratis bevestigd', OrdersMoved: 'Verplaatste opdrachten', WarningsAcknowledged: 'Bevestigde waarschuwingen',
  PackageId: 'Collo', StopId: 'Stop', Priority: 'Prioriteit', RelationType: 'Soort relatie', OccurredAt: 'Tijdstip',
  ResolutionNote: 'Oplossing', Resolution: 'Oplossing', FileName: 'Bestand', ConfirmationSource: 'Bevestigingswijze',
  Version: 'Versie',
  // B24: incident payloads (older status rows store From/To; charge and responsibility keys).
  From: 'Van', To: 'Naar', IncidentType: 'Type', ResponsibleParty: 'Verantwoordelijke partij',
  ResponsibilityNotes: 'Toelichting verantwoordelijkheid', ChargeDecision: 'Doorrekening', ChargeAmount: 'Bedrag doorrekening',
  ChargeDescription: 'Omschrijving doorrekening', PolicyId: 'Doorrekenbeleid', Mode: 'Werkwijze',
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

/**
 * B25: common lifecycle words (new audit rows store enums by NAME) read in Dutch. Only exact,
 * unambiguous words are mapped; an entity with its own meaning (dossier "Closed" = Bevestigd)
 * passes `valueLabels` instead.
 */
const VALUE_WORDS: Record<string, string> = {
  Draft: 'Concept', Submitted: 'Ingediend', Confirmed: 'Bevestigd', Planned: 'Gepland', InProgress: 'Onderweg',
  Completed: 'Afgerond', Invoiced: 'Gefactureerd', Cancelled: 'Geannuleerd', Requested: 'Aangevraagd',
  Approved: 'Goedgekeurd', Rejected: 'Afgewezen', Resolved: 'Opgelost', Sent: 'Verzonden', Paid: 'Betaald',
  Low: 'Laag', Medium: 'Gemiddeld', High: 'Hoog', Critical: 'Kritiek', Normal: 'Normaal', Urgent: 'Dringend',
  Manual: 'Handmatig', Automatic: 'Automatisch', Locked: 'Bevestigd', Reviewed: 'Nagekeken',
}

/** Ids inside the values resolved server-side to human references (order number, name, …). */
export type AuditReferences = Record<string, string>

function formatValue(value: unknown, references?: AuditReferences): string {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'ja' : 'nee'
  if (typeof value === 'number') return String(value)
  if (Array.isArray(value)) {
    if (value.length === 0) return '—'
    if (value.every((entry) => typeof entry !== 'object' || entry === null)) return value.map((entry) => formatValue(entry, references)).join(', ')
    return `${value.length} items`
  }
  if (typeof value === 'object') return '…'
  const text = String(value)
  // A resolvable id reads as what it is; an unresolvable one stays a neutral word, never a GUID.
  if (GUID.test(text)) return references?.[text.toLowerCase()] ?? references?.[text] ?? 'verwijzing'
  if (VALUE_WORDS[text]) return VALUE_WORDS[text]
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

/** One field-level change of an audit entry (keys present on either side; unchanged ones left out). */
export interface AuditChange {
  key: string
  field: string
  before: string | null
  after: string | null
}

function parseValues(json: string | null): Record<string, unknown> | null {
  if (!json) return null
  try {
    const parsed = JSON.parse(json) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null
  } catch {
    return null
  }
}

/** B25: the before/after of an audit entry per field — humanized, masked, references resolved. */
export function auditChanges(
  oldJson: string | null,
  newJson: string | null,
  options: { valueLabels?: AuditValueLabels; references?: AuditReferences } = {},
): AuditChange[] {
  const before = parseValues(oldJson) ?? {}
  const after = parseValues(newJson) ?? {}
  const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])]
  const render = (key: string, value: unknown, present: boolean): string | null => {
    if (!present) return null
    if (value !== null && value !== undefined && value !== '' && isSensitiveAuditKey(key)) return MASK
    return options.valueLabels?.[key]?.(value) ?? formatValue(value, options.references)
  }
  // Unchanged is decided on the RAW values: two masked values must still show that something changed.
  const same = (key: string) => key in before && key in after && JSON.stringify(before[key]) === JSON.stringify(after[key])
  return keys
    .filter((key) => !same(key))
    .map((key) => ({
      key,
      field: auditFieldLabel(key),
      before: render(key, before[key], key in before),
      after: render(key, after[key], key in after),
    }))
}

/**
 * B25: one sentence saying WHAT happened, readable without expanding:
 *  - one field changed on both sides → "Status gewijzigd van Gepland naar Onderweg";
 *  - a creation with a naming value → "Activiteit toegevoegd: Herlevering";
 *  - otherwise the changed fields ("Gewijzigd: Prijs, Referentie").
 */
/** Fields that name what an event is about (an activity label, an order number, …). */
const NAMING_KEY = /^(Label|Name|Title|OrderNumber|ActivityType(Id)?|TransportOrderId|LinkedTransportOrderId|Reason)$/

export function auditSummary(changes: AuditChange[]): string | null {
  const meaningful = changes.filter((change) => change.key !== 'Id' && change.key !== 'Version')
  if (meaningful.length === 0) return null
  if (meaningful.length === 1) {
    const [only] = meaningful
    if (only.before !== null && only.after !== null) return `${only.field} gewijzigd van ${only.before} naar ${only.after}`
    if (only.after !== null) return `${only.field}: ${only.after}`
    return `${only.field}: ${only.before} (verwijderd)`
  }
  const more = (rest: AuditChange[], limit: number) =>
    `${rest.slice(0, limit).map((change) => change.field).join(', ')}${rest.length > limit ? ` +${rest.length - limit}` : ''}`
  // A status move is what the reader looks for first; the other fields follow it.
  const status = meaningful.find((change) => /Status$/.test(change.key) && change.before !== null && change.after !== null)
  if (status) {
    const rest = meaningful.filter((change) => change !== status)
    return `${status.field} gewijzigd van ${status.before} naar ${status.after} · ook: ${more(rest, 3)}`
  }
  // Nothing existed before (a creation): say WHAT was recorded, not which fields "changed".
  if (meaningful.every((change) => change.before === null)) {
    const filled = meaningful
      .filter((change) => change.after !== null && change.after !== '—')
      .sort((a, b) => Number(NAMING_KEY.test(b.key)) - Number(NAMING_KEY.test(a.key)))
    const shown = filled.slice(0, 3).map((change) => `${change.field}: ${change.after}`)
    return `${shown.join(' · ')}${filled.length > 3 ? ` +${filled.length - 3}` : ''}`
  }
  const naming = meaningful.find((change) => NAMING_KEY.test(change.key) && change.after)
  const list = more(meaningful, 4)
  return naming ? `${naming.field}: ${naming.after} · gewijzigd: ${list}` : `Gewijzigd: ${list}`
}
