export type TransportOrderStatus = 'Draft' | 'Confirmed' | 'Planned' | 'InProgress' | 'Completed' | 'Cancelled'
export type StopType = 'Loading' | 'Unloading'

export const ORDER_STATUS_LABELS: Record<TransportOrderStatus, string> = {
  Draft: 'Concept',
  Confirmed: 'Bevestigd',
  Planned: 'Gepland',
  InProgress: 'In uitvoering',
  Completed: 'Afgerond',
  Cancelled: 'Geannuleerd',
}

export const ORDER_STATUSES: TransportOrderStatus[] = ['Draft', 'Confirmed', 'Planned', 'InProgress', 'Completed', 'Cancelled']

export const ORDER_STATUS_TONE: Record<TransportOrderStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Draft: 'neutral',
  Confirmed: 'info',
  Planned: 'info',
  InProgress: 'warning',
  Completed: 'success',
  Cancelled: 'danger',
}

/** Action labels for the guarded transitions offered by the backend. */
export const ORDER_TRANSITION_LABELS: Record<TransportOrderStatus, string> = {
  Draft: 'Terug naar concept',
  Confirmed: 'Bevestigen',
  Planned: 'Plannen',
  InProgress: 'Start uitvoering',
  Completed: 'Afronden',
  Cancelled: 'Annuleren',
}

export const STOP_TYPE_LABELS: Record<StopType, string> = {
  Loading: 'Laden',
  Unloading: 'Lossen',
}

export interface TransportOrderListItem {
  id: string
  orderNumber: string
  orderDate: string
  customerId: string
  customerName: string
  customerReference: string | null
  status: TransportOrderStatus
  goodsDescription: string
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
  stopCount: number
  adrRequired: boolean
  craneRequired: boolean
}

export interface TransportOrderStop {
  id: string
  sequence: number
  stopType: StopType
  locationId: string | null
  locationCode: string | null
  locationName: string
  address: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  plannedFrom: string | null
  plannedTo: string | null
  reference: string | null
  instructions: string | null
}

export interface TransportOrderDetail {
  id: string
  orderNumber: string
  orderDate: string
  customerId: string
  customerName: string
  customerReference: string | null
  status: TransportOrderStatus
  goodsDescription: string
  quantity: number | null
  quantityUnit: string | null
  weightKg: number | null
  volumeM3: number | null
  palletCount: number | null
  adrRequired: boolean
  craneRequired: boolean
  agreedPrice: number | null
  notes: string | null
  stops: TransportOrderStop[]
  allowedTransitions: TransportOrderStatus[]
}

export interface StopInput {
  stopType: StopType
  locationId: string | null
  locationName: string | null
  address: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  plannedFrom: string | null
  plannedTo: string | null
  reference: string | null
  instructions: string | null
}

export interface TransportOrderInput {
  customerId: string
  customerReference: string | null
  orderDate: string | null
  goodsDescription: string
  quantity: number | null
  quantityUnit: string | null
  weightKg: number | null
  volumeM3: number | null
  palletCount: number | null
  adrRequired: boolean
  craneRequired: boolean
  agreedPrice: number | null
  notes: string | null
  stops: StopInput[]
}
