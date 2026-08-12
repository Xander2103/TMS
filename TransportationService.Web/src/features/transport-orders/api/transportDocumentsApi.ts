import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

export type TransportDocumentKind = 'delivery-note' | 'cmr'

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
