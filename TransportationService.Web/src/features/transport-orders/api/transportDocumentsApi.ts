import { apiClient, LONG_RUNNING_TIMEOUT_MS } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'

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
  // PERF-H7: through apiClient (bearer + refresh) with the long-running deadline; the message
  // stays the server's detail or the translated "kon niet worden gegenereerd".
  let blob: Blob
  let serverFileName: string | null
  try {
    ;({ blob, fileName: serverFileName } = await apiClient.fetchBlob(url, { timeoutMs: LONG_RUNNING_TIMEOUT_MS }))
  } catch (err) {
    throw new Error(describeApiError(err, translate(getActiveLocale(), 'transportOrders.documents.generateFailed')).message, { cause: err })
  }
  const fileName = serverFileName ?? fallbackName
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
