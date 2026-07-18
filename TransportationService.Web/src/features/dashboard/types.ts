import type { TripListItem } from '../planning/types'
import type { TransportOrderStatus } from '../transport-orders/types'

export interface RecentOrder {
  id: string
  orderNumber: string
  orderDate: string
  customerName: string
  status: TransportOrderStatus
  goodsDescription: string
}

export interface Dashboard {
  ordersOpenCount: number
  ordersInExecutionCount: number
  ordersCompletedThisMonth: number
  tripsTodayTotal: number
  tripsTodayInProgress: number
  tripsTodayWithConflicts: number
  revenueInvoicedThisMonth: number
  outstandingAmount: number
  overdueInvoiceCount: number
  driversAbsentToday: number
  vehiclesAvailable: number
  maintenanceDueCount: number
  inspectionsDueCount: number
  documentsExpiringCount: number
  openDamageCount: number
  recentOrders: RecentOrder[]
  tripsToday: TripListItem[]
}
