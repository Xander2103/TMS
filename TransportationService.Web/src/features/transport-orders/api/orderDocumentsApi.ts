import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
import { getAccessToken } from '../../auth/authStorage'

export type OrderDocumentType = 'CustomerDeliveryNote' | 'DeliveryNote' | 'Cmr' | 'Other'

/** Vertaalsleutels per documenttype — renderen als t(ORDER_DOCUMENT_TYPE_LABELS[type]). */
export const ORDER_DOCUMENT_TYPE_LABELS: Record<OrderDocumentType, string> = {
  CustomerDeliveryNote: 'transportOrders.documentType.CustomerDeliveryNote',
  DeliveryNote: 'transportOrders.documentType.DeliveryNote',
  Cmr: 'transportOrders.documentType.Cmr',
  Other: 'transportOrders.documentType.Other',
}

export const ORDER_DOCUMENT_TYPES = Object.keys(ORDER_DOCUMENT_TYPE_LABELS) as OrderDocumentType[]

export const ORDER_DOCUMENT_ACCEPT = '.pdf,.jpg,.jpeg,.png'

export interface OrderDocument {
  id: string
  transportOrderId: string
  documentType: OrderDocumentType
  customTypeName: string | null
  title: string
  hasAttachment: boolean
  fileName: string | null
  issueDate: string | null
  notes: string | null
}

export interface OrderDocumentInput {
  documentType: OrderDocumentType
  customTypeName: string | null
  title: string
  issueDate: string | null
  notes: string | null
}

export const listOrderDocuments = (orderId: string): Promise<OrderDocument[]> =>
  apiClient.getJson(`/api/transport-orders/${orderId}/documents`)
export const createOrderDocument = (orderId: string, input: OrderDocumentInput): Promise<OrderDocument> =>
  apiClient.postJson(`/api/transport-orders/${orderId}/documents`, input)
export const updateOrderDocument = (id: string, input: OrderDocumentInput): Promise<OrderDocument> =>
  apiClient.putJson(`/api/order-documents/${id}`, input)
export const deleteOrderDocument = (id: string): Promise<void> => apiClient.deleteRequest(`/api/order-documents/${id}`)

export async function uploadOrderDocumentFile(id: string, file: File): Promise<OrderDocument> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/order-documents/${id}/document`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    let message = translate(getActiveLocale(), 'transportOrders.documents.uploadFailed')
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as OrderDocument
}

export async function downloadOrderDocumentFile(id: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/order-documents/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError(translate(getActiveLocale(), 'transportOrders.documents.downloadFailed'), response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
