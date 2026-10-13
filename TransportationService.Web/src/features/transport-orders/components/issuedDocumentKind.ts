import type { IssuedTransportDocumentKind } from '../api/issuedDocumentsApi'
import { isOnSiteLiftingOrder } from './onSiteLifting'

/**
 * Which document an order gets by default — from what the app already knows, never from an
 * activity code: an on-site lifting job (craneJobKind) or a `WorkOrder` strategy → werkbon, a
 * `DeliveryNote` strategy → leveringsbon, anything else → vrachtbrief (CMR).
 */
export function defaultIssueKind(
  strategyKind: string | null | undefined,
  craneJobKind: string | null | undefined,
): IssuedTransportDocumentKind {
  if (isOnSiteLiftingOrder({ craneJobKind }) || strategyKind === 'WorkOrder') return 'WorkOrder'
  return strategyKind === 'DeliveryNote' ? 'DeliveryNote' : 'Cmr'
}
