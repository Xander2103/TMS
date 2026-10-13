export type NotificationRecipientType =
  | 'CustomerPrimaryContact'
  | 'CustomerCommunicationRule'
  | 'InternalPermission'
  | 'InternalRole'
  | 'ExplicitEmail'
  | 'Driver'

/** Translation keys per recipient type; render via t(RECIPIENT_TYPE_LABELS[type]). */
export const RECIPIENT_TYPE_LABELS: Record<NotificationRecipientType, string> = {
  CustomerPrimaryContact: 'notificationAdmin.recipientType.CustomerPrimaryContact',
  CustomerCommunicationRule: 'notificationAdmin.recipientType.CustomerCommunicationRule',
  InternalPermission: 'notificationAdmin.recipientType.InternalPermission',
  InternalRole: 'notificationAdmin.recipientType.InternalRole',
  ExplicitEmail: 'notificationAdmin.recipientType.ExplicitEmail',
  Driver: 'notificationAdmin.recipientType.Driver',
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
  /** P9: klantmail van deze gebeurtenis wordt vastgehouden voor controle door dispatch. */
  requiresReview: boolean
}

export interface UpsertNotificationRuleInput {
  enabled: boolean
  inAppEnabled: boolean
  emailEnabled: boolean
  allowCustomerOverride: boolean
  recipients: RecipientSpec[]
  /** P9: null = catalogusstandaard voor deze gebeurtenis behouden. */
  requiresReview: boolean | null
}

export interface CustomerNotificationOverride {
  eventKey: string
  label: string
  allowCustomerOverride: boolean
  enabled: boolean | null
}

export type MessageChannel = 'Email' | 'Sms'
export type OutboxStatus = 'Pending' | 'Sent' | 'Failed' | 'Suppressed' | 'AwaitingReview'

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

/** Message-kind display keys — mirrors NotificationEventCatalog for the 30 event kinds and adds
 * the (unmanaged, pre-Phase-6) legacy kinds still visible in the outbox. The set of KNOWN kinds
 * lives here; the display text lives in locales/<lang>/notificationAdmin.json under `kind.*`.
 * Unknown kinds fall back to the raw code, so a future kind never renders blank. */
export const KNOWN_KINDS: readonly string[] = [
  // Orders
  'order_created',
  'order_submitted_portal',
  'order_accepted',
  'order_rejected',
  'order_info_requested',
  'order_planned',
  'order_pickup_window',
  'order_delivery_window',
  'order_pickup_completed',
  'order_delivery_completed',
  'order_delay_detected',
  'order_failed_delivery',
  'order_damage_registered',
  'order_pod_available',
  // Facturatie
  'invoice_draft_ready',
  'invoice_sent',
  'invoice_peppol_queued',
  'invoice_peppol_delivered',
  'invoice_peppol_failed',
  'invoice_credit_note',
  // Personeel
  'personnel_qualification_expiry',
  'personnel_medical_expiry',
  'personnel_document_expiry',
  'leave_requested',
  'leave_decided',
  'employee_note_pinned',
  // Vloot
  'fleet_maintenance_due',
  'fleet_inspection_due',
  'fleet_document_expiry',
  'fleet_damage_created',
  // Legacy (pre-Phase-6) kinds
  'order_confirmation',
  'time_window_confirmation',
  'driver_en_route',
  'eta_update',
  'delay',
  'delivery_completed',
  'pod_available',
  'leave_submitted',
  'leave_approved',
  'leave_rejected',
  'planning_changed',
  'qualification_expiry',
  'hr_birthday',
  'hr_seniority',
  'hr_employment_end',
]

export function kindLabel(t: (key: string) => string, kind: string): string {
  return KNOWN_KINDS.includes(kind) ? t(`notificationAdmin.kind.${kind}`) : kind
}
