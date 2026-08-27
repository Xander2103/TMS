import { apiClient } from '../../../api/apiClient'

/**
 * Sprint 3 — "who receives what?" configured on the contact. These endpoints write the same
 * communication rules the advanced screen edits; they are just the simple way in.
 */

/** The three business groups a normal user thinks in. */
export type CustomerNotificationGroup = 'Transport' | 'Facturatie' | 'Algemeen'

export interface CustomerNotificationOption {
  key: string
  group: CustomerNotificationGroup
}

export interface ContactSubscriptions {
  contactId: string
  optionKeys: string[]
}

export interface NotificationRecipientLine {
  contactId: string | null
  name: string
  email: string | null
  /** CC address or fallback contact — routing detail, hidden unless advanced is shown. */
  isAdvanced: boolean
  isActive: boolean
}

export interface NotificationOverviewLine {
  optionKey: string
  group: CustomerNotificationGroup
  recipients: NotificationRecipientLine[]
}

/** Translation key per option; the option keys themselves are stable API values. */
export const NOTIFICATION_OPTION_KEYS: Record<string, string> = {
  'order-confirmation': 'customers.notifications.orderConfirmation',
  planning: 'customers.notifications.planning',
  eta: 'customers.notifications.eta',
  'delivery-pod': 'customers.notifications.deliveryPod',
  'delivery-problem': 'customers.notifications.deliveryProblem',
  redelivery: 'customers.notifications.redelivery',
  invoice: 'customers.notifications.invoice',
  'credit-note': 'customers.notifications.creditNote',
  'invoice-reminder': 'customers.notifications.invoiceReminder',
  general: 'customers.notifications.general',
}

export const NOTIFICATION_GROUP_KEYS: Record<CustomerNotificationGroup, string> = {
  Transport: 'customers.notifications.groupTransport',
  Facturatie: 'customers.notifications.groupInvoicing',
  Algemeen: 'customers.notifications.groupGeneral',
}

/** Languages offered to normal users; raw locale codes are never shown. */
export const CONTACT_LANGUAGES = ['nl', 'fr', 'en', 'de'] as const
export type ContactLanguage = (typeof CONTACT_LANGUAGES)[number]

export const CONTACT_LANGUAGE_KEYS: Record<ContactLanguage, string> = {
  nl: 'customers.notifications.languageNl',
  fr: 'customers.notifications.languageFr',
  en: 'customers.notifications.languageEn',
  de: 'customers.notifications.languageDe',
}

export function getNotificationOptions(): Promise<CustomerNotificationOption[]> {
  return apiClient.getJson<CustomerNotificationOption[]>('/api/customer-notification-options')
}

export function getContactNotifications(customerId: string, contactId: string): Promise<ContactSubscriptions> {
  return apiClient.getJson<ContactSubscriptions>(`/api/customers/${customerId}/contacts/${contactId}/notifications`)
}

export function setContactNotifications(
  customerId: string,
  contactId: string,
  optionKeys: string[],
): Promise<ContactSubscriptions> {
  return apiClient.putJson<ContactSubscriptions, { optionKeys: string[] }>(
    `/api/customers/${customerId}/contacts/${contactId}/notifications`,
    { optionKeys },
  )
}

export function getNotificationOverview(customerId: string): Promise<NotificationOverviewLine[]> {
  return apiClient.getJson<NotificationOverviewLine[]>(`/api/customers/${customerId}/notification-overview`)
}
