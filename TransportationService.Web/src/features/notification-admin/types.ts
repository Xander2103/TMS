export type NotificationRecipientType =
  | 'CustomerPrimaryContact'
  | 'CustomerCommunicationRule'
  | 'InternalPermission'
  | 'InternalRole'
  | 'ExplicitEmail'
  | 'Driver'

export const RECIPIENT_TYPE_LABELS: Record<NotificationRecipientType, string> = {
  CustomerPrimaryContact: 'Hoofdcontact van de klant',
  CustomerCommunicationRule: 'Klant-communicatieregel',
  InternalPermission: 'Iedereen met een bepaald recht',
  InternalRole: 'Iedereen met een bepaalde rol',
  ExplicitEmail: 'Specifiek e-mailadres',
  Driver: 'Chauffeur / betrokken medewerker',
}

/** Recipient types whose Value is a free/selected identifier; the other two need none. */
export const RECIPIENT_TYPES_WITH_VALUE: NotificationRecipientType[] = [
  'CustomerCommunicationRule',
  'InternalPermission',
  'InternalRole',
  'ExplicitEmail',
]

export interface RecipientSpec {
  type: NotificationRecipientType
  value: string | null
}

export interface NotificationRule {
  eventKey: string
  label: string
  group: string
  allowedTokens: string[]
  enabled: boolean
  inAppEnabled: boolean
  emailEnabled: boolean
  allowCustomerOverride: boolean
  recipients: RecipientSpec[]
  isCustomized: boolean
  peppolPending: boolean
}

export interface UpsertNotificationRuleInput {
  enabled: boolean
  inAppEnabled: boolean
  emailEnabled: boolean
  allowCustomerOverride: boolean
  recipients: RecipientSpec[]
}

export interface CustomerNotificationOverride {
  eventKey: string
  label: string
  allowCustomerOverride: boolean
  enabled: boolean | null
}

export type MessageChannel = 'Email' | 'Sms'
export type OutboxStatus = 'Pending' | 'Sent' | 'Failed' | 'Suppressed'

export interface OutboxRow {
  id: string
  channel: MessageChannel
  kind: string
  recipientAddress: string
  recipientName: string | null
  subject: string | null
  status: OutboxStatus
  attemptCount: number
  nextAttemptAt: string | null
  sentAt: string | null
  failureReason: string | null
  createdAt: string
  isFallback: boolean
  relatedEntityType: string | null
  relatedEntityId: string | null
}

export interface MessageTemplate {
  id: string
  kind: string
  channel: MessageChannel
  language: string
  customerId: string | null
  subject: string | null
  body: string
  bodyHtml: string | null
  isActive: boolean
}

/** One (kind, channel, language) row as it resolves for a specific customer: either the tenant
 * default (isOverridden false, id null) or that customer's own override (isOverridden true, id
 * set — the override row's own id, usable with the same delete endpoint as a tenant template). */
export interface CustomerMessageTemplate {
  kind: string
  channel: MessageChannel
  language: string
  isOverridden: boolean
  id: string | null
  subject: string | null
  body: string
  bodyHtml: string | null
  isActive: boolean
}

/** Dutch display label for a message kind — mirrors NotificationEventCatalog for the 30 event
 * kinds and adds the (unmanaged, pre-Phase-6) legacy kinds still visible in the outbox. Falls
 * back to the raw code for anything unrecognised, so a future kind never renders blank. */
export const KIND_LABELS: Record<string, string> = {
  // Orders
  order_created: 'Opdracht aangemaakt',
  order_submitted_portal: 'Opdracht ingediend via klantportaal',
  order_accepted: 'Opdracht geaccepteerd',
  order_rejected: 'Opdracht geweigerd',
  order_info_requested: 'Extra informatie gevraagd',
  order_planned: 'Opdracht ingepland',
  order_pickup_window: 'Ophaalvenster bevestigd',
  order_delivery_window: 'Leveringsvenster bevestigd',
  order_pickup_completed: 'Ophaling afgerond',
  order_delivery_completed: 'Levering afgerond',
  order_delay_detected: 'Vertraging vastgesteld',
  order_failed_delivery: 'Levering mislukt',
  order_damage_registered: 'Schade geregistreerd bij opdracht',
  order_pod_available: 'Afleverbewijs beschikbaar',
  // Facturatie
  invoice_draft_ready: 'Conceptfactuur klaar',
  invoice_sent: 'Factuur verzonden',
  invoice_peppol_queued: 'Peppol-factuur in wachtrij',
  invoice_peppol_delivered: 'Peppol-factuur afgeleverd',
  invoice_peppol_failed: 'Peppol-factuur mislukt',
  invoice_credit_note: 'Creditnota aangemaakt',
  // Personeel
  personnel_qualification_expiry: 'Kwalificatie vervalt binnenkort',
  personnel_medical_expiry: 'Medische keuring vervalt binnenkort',
  personnel_document_expiry: 'Persoonsdocument vervalt binnenkort',
  leave_requested: 'Verlofaanvraag ontvangen',
  leave_decided: 'Verlofaanvraag beslist',
  employee_note_pinned: 'Notitie vastgepind aan dashboard',
  // Vloot
  fleet_maintenance_due: 'Onderhoud binnenkort verschuldigd',
  fleet_inspection_due: 'Keuring binnenkort verschuldigd',
  fleet_document_expiry: 'Vlootdocument vervalt binnenkort',
  fleet_damage_created: 'Schadegeval geregistreerd',
  // Legacy (pre-Phase-6) kinds
  order_confirmation: 'Orderbevestiging',
  time_window_confirmation: 'Tijdvak bevestigd',
  driver_en_route: 'Chauffeur onderweg',
  eta_update: 'ETA-update',
  delay: 'Vertraging',
  delivery_completed: 'Levering afgerond (legacy)',
  pod_available: 'Afleverbewijs beschikbaar (legacy)',
  leave_submitted: 'Verlofaanvraag ingediend (legacy)',
  leave_approved: 'Verlof goedgekeurd (legacy)',
  leave_rejected: 'Verlof afgewezen (legacy)',
  planning_changed: 'Planning gewijzigd',
  qualification_expiry: 'Kwalificatie vervalt (legacy)',
  hr_birthday: 'Verjaardag',
  hr_seniority: 'Jubileum',
  hr_employment_end: 'Einde dienstverband',
}

export function kindLabel(kind: string): string {
  return KIND_LABELS[kind] ?? kind
}
