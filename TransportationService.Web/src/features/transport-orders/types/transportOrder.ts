export interface TransportOrder {
  id: string
  reference: string
  customerName: string
  pickupAddress: string
  deliveryAddress: string
  status: string
}

export interface CreateTransportOrderInput {
  reference: string
  customerName: string
  pickupAddress: string
  deliveryAddress: string
  status: string
}

export const TRANSPORT_ORDER_STATUS_OPTIONS: readonly string[] = [
  'New',
  'Planned',
  'InProgress',
  'Delivered',
  'Cancelled',
]
