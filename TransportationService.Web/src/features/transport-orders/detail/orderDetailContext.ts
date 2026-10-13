import { createContext, useContext } from 'react'
import type { LegalEntityOption } from '../../legal-entities/types'
import type { OrderPricingLine, OrderPricingSnapshot, TransportOrderDetail, TransportOrderStop } from '../types'
import type { OrderTab } from './orderSections'

/**
 * What the subsections read from the shell. The shell (`TransportOrderDetailPage`) keeps every
 * piece of state, every handler and every dialog exactly as before the redesign; the sections
 * only render against this contract, so the domain flows stay untouched.
 */
export interface OrderDetailWorkspace {
  order: TransportOrderDetail
  busy: boolean
  editing: boolean
  entities: LegalEntityOption[]

  // permissions / editability (computed once in the shell)
  editable: boolean
  deletable: boolean
  planEditable: boolean
  canEditPricingLines: boolean
  canEditPricingStatus: boolean
  canLockPrice: boolean
  canViewPackages: boolean
  canViewMessages: boolean
  canChangeStatus: boolean
  canEditOrder: boolean

  // pricing read model (computed once in the shell)
  pricingStatus: OrderPricingSnapshot['status'] | 'Draft'
  pricingLocked: boolean
  pricingBusy: boolean
  invoiceLines: OrderPricingLine[]
  notAppliedLines: OrderPricingLine[]
  coverage: NonNullable<OrderPricingSnapshot['coverage']>
  unpricedCoverage: NonNullable<OrderPricingSnapshot['coverage']>
  priceDisplay: { labelKey: string; tone: 'neutral' | 'warning' | 'success' | 'danger' }
  totalPrice: number | null

  // helpers
  unitLabel: (code: string | null, legacy: string | null) => string
  aggregateCargo: (items: TransportOrderDetail['cargoItems']) => { unit: string; total: number }[]

  // handlers owned by the shell
  setOrder: (order: TransportOrderDetail) => void
  openTab: (tab: OrderTab) => void
  setPlanStop: (stop: TransportOrderStop) => void
  openCustomerChange: () => void
  openEntityChange: () => void
  handleConfirmPriceClick: () => void
  openReopenPrice: () => void
  handleRecalculateClick: () => void
  openAddLine: () => void
  openCalcDetails: () => void
  handleConfirmLine: (line: OrderPricingLine) => void
  openEditLine: (line: OrderPricingLine) => void
  openRemoveLine: (line: OrderPricingLine) => void
}

export const OrderDetailContext = createContext<OrderDetailWorkspace | null>(null)

export function useOrderDetail(): OrderDetailWorkspace {
  const value = useContext(OrderDetailContext)
  if (!value) throw new Error('useOrderDetail must be used inside TransportOrderDetailPage')
  return value
}
