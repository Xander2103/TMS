import type { DossierDetail, DossierOrder } from './types'

/**
 * M4: orders of the dossier still waiting for the planner's portal review (status Submitted).
 * While any exists, the dossier's own "Bevestigen" (dossier confirmation) is blocked — the
 * planner first accepts, questions or rejects the customer's request.
 */
export function submittedPortalOrders(dossier: Pick<DossierDetail, 'orders'>): DossierOrder[] {
  return dossier.orders.filter((order) => order.status === 'Submitted')
}
