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

/** Only ever populated for a caller holding employee_notes.view. */
export interface PinnedEmployeeNote {
  noteId: string
  employeeId: string
  employeeName: string
  excerpt: string
  /** When the note was pinned (not when it was originally written). */
  pinnedAt: string
  /** Who pinned the note (not necessarily its original author). */
  authorName: string | null
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
  qualificationsExpiring30d: number
  qualificationsExpired: number
  openIncidentCount: number
  missingPodCount: number
  failedScanCount: number
  overdueMaintenanceCount: number
  recentOrders: RecentOrder[]
  tripsToday: TripListItem[]
  pinnedEmployeeNotes: PinnedEmployeeNote[]
}
