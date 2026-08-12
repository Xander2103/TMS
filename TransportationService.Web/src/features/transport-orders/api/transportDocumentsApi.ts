import { apiBaseUrl } from '../../../config/env'
import { apiClient } from '../../../api/apiClient'
import { getAccessToken } from '../../auth/authStorage'

export type TransportDocumentKind = 'delivery-note' | 'cmr'

/** Documentkeuze op orderniveau; null = volgens klantinstelling/regels. */
export type OrderDocumentPreference = 'Own' | 'CustomerDocument' | 'NoneRequired'

/** Resultaat van de documentstrategie-resolver voor één order (GET .../documents/strategy). */
export interface OrderDocumentStrategy {
  kind: 'DeliveryNote' | 'Cmr' | null
  usesCustomerDocument: boolean
  noneRequired: boolean
  undecided: boolean
  source: 'OrderOverride' | 'CustomerDefault' | 'TenantRule' | 'BuiltInDefault'
  reason: string
  orderPreference: OrderDocumentPreference | null
  customerStrategy: string
}

export function getOrderDocumentStrategy(orderId: string): Promise<OrderDocumentStrategy> {
  return apiClient.getJson<OrderDocumentStrategy>(`/api/orders/${orderId}/documents/strategy`)
}

/** Zet (of wist, met null) de documentkeuze van deze opdracht; geeft de nieuwe strategie terug. */
export function setOrderDocumentPreference(
  orderId: string,
  preference: OrderDocumentPreference | null,
): Promise<OrderDocumentStrategy> {
  return apiClient.putJson<OrderDocumentStrategy, { preference: OrderDocumentPreference | null }>(
    `/api/orders/${orderId}/documents/preference`,
    { preference },
  )
}

async function downloadPdf(url: string, fallbackName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}${url}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new Error('Het document kon niet worden gegenereerd.')
  const blob = await response.blob()
  const disposition = response.headers.get('content-disposition')
  const fileName = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? fallbackName
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}

/** Wave 9: leveringsbon of CMR van één order. */
export function downloadOrderDocument(orderId: string, kind: TransportDocumentKind, orderNumber: string): Promise<void> {
  return downloadPdf(`/api/orders/${orderId}/documents/${kind}`, `${kind}-${orderNumber}.pdf`)
}

/** Wave 9: één samengevoegde PDF voor alle orders van een rit (routevolgorde). */
export function downloadTripDocuments(tripId: string, kind: TransportDocumentKind, tripNumber: string): Promise<void> {
  return downloadPdf(`/api/trips/${tripId}/documents/${kind}`, `${kind}-${tripNumber}.pdf`)
}
