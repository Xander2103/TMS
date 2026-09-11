import { createContext, useContext } from 'react'
import type { TransportOrderDetail } from '../transport-orders/types'
import type { DossierActivity, DossierDetail, DossierOrder } from './types'

/**
 * Everything a dossier subsection needs from the shell. The shell (`DossierDetailPage`) owns the
 * dossier, the loaded target order, the unit selection and the dirty flag — they survive every
 * subsection switch — and the subsections only render against this contract.
 */
export interface DossierWorkspace {
  dossier: DossierDetail
  isOpen: boolean
  canManage: boolean
  /** dossiers.manage AND orders.edit|orders.manage AND open: inline route editing. */
  canEditRoute: boolean
  canEditOrder: boolean
  canEditPriceLines: boolean
  canEditActivityPrice: boolean
  canCreateLocations: boolean
  busy: boolean

  /** Activities in dossier order. */
  activities: DossierActivity[]
  transportActivities: DossierActivity[]
  billableActivities: DossierActivity[]
  /** Linked orders no activity represents (legacy dossiers). */
  legacyOrders: DossierOrder[]

  /** Target of Route/Goederen (a transport activity) and of Verkoop & prijs (any billable unit). */
  routeActivity: DossierActivity | null
  priceActivity: DossierActivity | null
  priceTargetIsStandalone: boolean
  activityWithoutOrder: DossierActivity | null

  /** The route target's linked order, its loading state and the id it belongs to. */
  firstLinkedOrderId: string | null
  firstOrder: TransportOrderDetail | null
  firstOrderLoading: boolean

  routeDirty: boolean
  setRouteDirty: (dirty: boolean) => void
  selectActivity: (activityId: string) => void

  applyDossier: (dossier: DossierDetail) => void
  handleConflict: (err: unknown) => boolean
  handleOrderSaved: (order: TransportOrderDetail) => void
  retryOrderLoad: () => void

  openActivity: (activity: DossierActivity) => void
  openAddActivity: () => void
  openGoodsDrawer: () => void
  unlinkOrder: (orderId: string) => void
  removeRelation: (relationId: string) => void

  /** Field focusers for the section registry (read the editor handles at call time, never during render). */
  focusRouteField: (field: string | null) => boolean
  focusPriceField: (field: string | null) => boolean
  focusAddActivity: () => boolean
}

export const DossierWorkspaceContext = createContext<DossierWorkspace | null>(null)

export function useDossierWorkspace(): DossierWorkspace {
  const value = useContext(DossierWorkspaceContext)
  if (!value) throw new Error('useDossierWorkspace must be used inside DossierDetailPage')
  return value
}
