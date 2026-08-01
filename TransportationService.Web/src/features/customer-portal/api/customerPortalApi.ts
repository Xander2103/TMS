import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { PackageUnitType } from '../../packages/types'
import type { StopType, TransportOrderStatus } from '../../transport-orders/types'
import type { PortalAnnouncement } from './portalAnnouncementsApi'

export interface PortalContext {
  customerId: string
  customerName: string
}

export interface PortalOrderListItem {
  id: string
  orderNumber: string
  orderDate: string
  status: TransportOrderStatus
  customerReference: string | null
  goodsDescription: string | null
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
}

export interface PortalStop {
  sequence: number
  stopType: StopType
  locationName: string
  city: string | null
  requestedFrom: string | null
  requestedTo: string | null
  reference: string | null
  instructions: string | null
}

export interface PortalCargo {
  sequence: number
  description: string
  expectedQuantity: number
  quantityUnit: string | null
  unitType: PackageUnitType | null
  adrRequired: boolean
}

export interface PortalTimelineEvent {
  status: TransportOrderStatus
  changedAt: string
  reason: string | null
}

export interface PortalException {
  type: string
  description: string
  status: string
  occurredAt: string
}

export interface PortalOrderDetail {
  id: string
  orderNumber: string
  orderDate: string
  status: TransportOrderStatus
  customerReference: string | null
  goodsDescription: string | null
  notes: string | null
  cancellationReason: string | null
  stops: PortalStop[]
  cargoItems: PortalCargo[]
  timeline: PortalTimelineEvent[]
  exceptions: PortalException[]
}

export interface PortalStopInput {
  stopType: StopType
  locationId: string | null
  locationName: string | null
  address: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  requestedFrom: string | null
  requestedTo: string | null
  reference: string | null
  instructions: string | null
}

export interface PortalCargoInput {
  description: string
  expectedQuantity: number
  quantityUnit: string | null
  unitType: PackageUnitType | null
  totalWeightKg: number | null
  adrRequired: boolean
  adrDetails: string | null
}

export interface PortalCreateOrderInput {
  customerReference: string | null
  orderDate: string | null
  goodsDescription: string | null
  remarks: string | null
  stops: PortalStopInput[]
  cargoItems: PortalCargoInput[]
}

export interface PortalLocation {
  id: string
  name: string
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  isDefaultLoadingLocation: boolean
  isDefaultUnloadingLocation: boolean
}

export function getPortalContext(): Promise<PortalContext> {
  return apiClient.getJson<PortalContext>('/api/customer-portal/context')
}

export function listPortalOrders(): Promise<PortalOrderListItem[]> {
  return apiClient.getJson<PortalOrderListItem[]>('/api/customer-portal/orders')
}

export function getPortalOrder(id: string): Promise<PortalOrderDetail> {
  return apiClient.getJson<PortalOrderDetail>(`/api/customer-portal/orders/${id}`)
}

export function submitPortalOrder(input: PortalCreateOrderInput): Promise<PortalOrderDetail> {
  return apiClient.postJson<PortalOrderDetail, PortalCreateOrderInput>('/api/customer-portal/orders', input)
}

export function listPortalLocations(): Promise<PortalLocation[]> {
  return apiClient.getJson<PortalLocation[]>('/api/customer-portal/locations')
}

export function createPortalLocation(input: {
  name: string
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
}): Promise<PortalLocation> {
  return apiClient.postJson<PortalLocation, typeof input>('/api/customer-portal/locations', input)
}

// --- Portal user management (customer_portal.manage_users) ---

export interface PortalUserGrants {
  documents: boolean
  invoices: boolean
  manageUsers: boolean
}

export interface PortalUserListItem {
  id: string
  email: string
  firstName: string
  lastName: string
  isActive: boolean
  isBlocked: boolean
  hasPendingActivation: boolean
  grants: PortalUserGrants
}

export interface PortalUserInviteResult {
  user: PortalUserListItem
  /** Present ONLY while the backend's registered mail provider is the development sink (no real
   * SMTP/SendGrid provider configured) — see CustomerPortalUserService.IsRawTokenSafeToReturn.
   * Once a live provider is configured, the backend omits this field entirely. */
  activationToken: string | null
  activationTokenExpiresAtUtc: string
}

export function listPortalUsers(): Promise<PortalUserListItem[]> {
  return apiClient.getJson<PortalUserListItem[]>('/api/customer-portal/users')
}

export function invitePortalUser(input: {
  firstName: string
  lastName: string
  email: string
  grants: PortalUserGrants
}): Promise<PortalUserInviteResult> {
  return apiClient.postJson<PortalUserInviteResult, typeof input>('/api/customer-portal/users', input)
}

export function deactivatePortalUser(id: string): Promise<PortalUserListItem> {
  return apiClient.postJson<PortalUserListItem, undefined>(`/api/customer-portal/users/${id}/deactivate`, undefined)
}

export function reactivatePortalUser(id: string): Promise<PortalUserListItem> {
  return apiClient.postJson<PortalUserListItem, undefined>(`/api/customer-portal/users/${id}/reactivate`, undefined)
}

export function resendPortalUserInvite(id: string): Promise<PortalUserInviteResult> {
  return apiClient.postJson<PortalUserInviteResult, undefined>(`/api/customer-portal/users/${id}/resend-invite`, undefined)
}

export function setPortalUserGrants(id: string, grants: PortalUserGrants): Promise<PortalUserListItem> {
  return apiClient.putJson<PortalUserListItem, { grants: PortalUserGrants }>(
    `/api/customer-portal/users/${id}/grants`,
    { grants },
  )
}

// --- Dashboard ---

export interface PortalUpcomingDelivery {
  orderId: string
  orderNumber: string
  plannedAt: string
  city: string | null
}

export interface PortalRecentInvoice {
  id: string
  invoiceNumber: string
  invoiceDate: string
  status: string
  total: number
}

export interface PortalDashboard {
  activeOrders: number
  upcomingDeliveries: PortalUpcomingDelivery[]
  problemOrders: number
  unreadMessages: number
  recentInvoices: PortalRecentInvoice[]
  announcements: PortalAnnouncement[]
}

export function getPortalDashboard(): Promise<PortalDashboard> {
  return apiClient.getJson<PortalDashboard>('/api/customer-portal/dashboard')
}

// --- Announcements (portal read-only view; admin CRUD lives in portalAnnouncementsApi.ts) ---

export function listPortalAnnouncements(): Promise<PortalAnnouncement[]> {
  return apiClient.getJson<PortalAnnouncement[]>('/api/customer-portal/announcements')
}

// --- Messages ---

export interface CustomerMessage {
  id: string
  transportOrderId: string | null
  orderNumber: string | null
  authorIsStaff: boolean
  authorName: string
  body: string
  createdAt: string
}

export function listPortalMessages(orderId?: string): Promise<CustomerMessage[]> {
  const query = orderId ? `?orderId=${orderId}` : ''
  return apiClient.getJson<CustomerMessage[]>(`/api/customer-portal/messages${query}`)
}

export function sendPortalMessage(orderId: string | null, body: string): Promise<CustomerMessage> {
  return apiClient.postJson<CustomerMessage, { orderId: string | null; body: string }>(
    '/api/customer-portal/messages',
    { orderId, body },
  )
}

export function markPortalMessagesRead(orderId: string | null): Promise<void> {
  return apiClient.postJson<void, { orderId: string | null }>('/api/customer-portal/messages/read', { orderId })
}

export function getPortalMessagesUnreadCount(): Promise<{ count: number }> {
  return apiClient.getJson<{ count: number }>('/api/customer-portal/messages/unread-count')
}

// --- Portal-messages feed (staff-authored, multi-language; admin side lives in
// features/portal-messages). Content arrives already resolved to the caller's language. ---

export type PortalFeedPriority = 'Normal' | 'High' | 'Urgent'

export type PortalFeedDisplayMode = 'Notification' | 'DashboardBanner' | 'BlockingAcknowledgement'

/** Mirrors PortalMessageFeedItemDto (camelCase JSON). */
export interface PortalFeedMessage {
  id: string
  title: string
  body: string
  language: string
  priority: PortalFeedPriority
  displayMode: PortalFeedDisplayMode
  requiresAcknowledgement: boolean
  relatedEntityType: 'order' | 'invoice' | null
  relatedEntityId: string | null
  publishedAt: string
  expiresAt: string | null
  readAt: string | null
  acknowledgedAt: string | null
}

export function listPortalFeedMessages(): Promise<PortalFeedMessage[]> {
  return apiClient.getJson<PortalFeedMessage[]>('/api/customer-portal/portal-messages')
}

export function getPortalFeedUnreadCount(): Promise<{ count: number }> {
  return apiClient.getJson<{ count: number }>('/api/customer-portal/portal-messages/unread-count')
}

export function markPortalFeedMessageRead(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/customer-portal/portal-messages/${id}/read`, {})
}

export function acknowledgePortalFeedMessage(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/customer-portal/portal-messages/${id}/acknowledge`, {})
}

// --- Invoices ---

export interface PortalInvoiceListItem {
  id: string
  invoiceNumber: string
  invoiceDate: string
  dueDate: string
  status: string
  total: number
  currency: string
  /** Ruwe Peppol-transmissiestatus (bv. "Delivered"), of null zolang niet via Peppol verzonden. */
  peppolStatus: string | null
  kind: 'Invoice' | 'CreditNote'
}

export interface PortalInvoiceLine {
  description: string
  quantity: number
  unitPrice: number
  vatRatePercent: number
  lineTotal: number
}

export interface PortalInvoiceAttachment {
  id: string
  fileName: string
  sizeBytes: number
}

export interface PortalInvoiceDetail {
  id: string
  invoiceNumber: string
  invoiceDate: string
  dueDate: string
  status: string
  currency: string
  purchaseOrderNumber: string | null
  lines: PortalInvoiceLine[]
  subtotal: number
  vatAmount: number
  total: number
  attachments: PortalInvoiceAttachment[]
  /** See PortalInvoiceListItem.peppolStatus. */
  peppolStatus: string | null
  kind: 'Invoice' | 'CreditNote'
}

export function listPortalInvoices(): Promise<PortalInvoiceListItem[]> {
  return apiClient.getJson<PortalInvoiceListItem[]>('/api/customer-portal/invoices')
}

export function getPortalInvoice(id: string): Promise<PortalInvoiceDetail> {
  return apiClient.getJson<PortalInvoiceDetail>(`/api/customer-portal/invoices/${id}`)
}

async function downloadBlob(path: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('Het bestand kon niet worden gedownload.', response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

export function downloadPortalInvoicePdf(id: string, invoiceNumber: string): Promise<void> {
  return downloadBlob(`/api/customer-portal/invoices/${id}/pdf`, `factuur-${invoiceNumber}.pdf`)
}

export function downloadPortalInvoiceAttachment(invoiceId: string, attachmentId: string, fileName: string): Promise<void> {
  return downloadBlob(`/api/customer-portal/invoices/${invoiceId}/attachments/${attachmentId}/content`, fileName)
}

// --- Documents ---

export type PortalDocumentSource = 'OrderDocument' | 'Pod' | 'InvoiceAttachment'

export interface PortalDocument {
  id: string
  source: PortalDocumentSource
  title: string
  fileName: string | null
  createdAt: string
  orderId: string | null
  orderNumber: string | null
  invoiceId: string | null
  invoiceNumber: string | null
}

export function listPortalDocuments(): Promise<PortalDocument[]> {
  return apiClient.getJson<PortalDocument[]>('/api/customer-portal/documents')
}

export function downloadPortalDocument(source: PortalDocumentSource, id: string, fileName: string): Promise<void> {
  return downloadBlob(`/api/customer-portal/documents/${source}/${id}/content`, fileName)
}
