import type { BadgeTone } from '../../components/ui/Badge'

/** Server-computed stock status of a template or variant. */
export type InventoryStatus = 'Normal' | 'LowStock' | 'CriticalStock' | 'OutOfStock' | 'NegativeStock'

export const INVENTORY_STATUSES: InventoryStatus[] = [
  'Normal',
  'LowStock',
  'CriticalStock',
  'OutOfStock',
  'NegativeStock',
]

/** Vertaalsleutels — renderen als t(INVENTORY_STATUS_LABELS[status]). */
export const INVENTORY_STATUS_LABELS: Record<InventoryStatus, string> = {
  Normal: 'issuedItems.inventoryStatus.Normal',
  LowStock: 'issuedItems.inventoryStatus.LowStock',
  CriticalStock: 'issuedItems.inventoryStatus.CriticalStock',
  OutOfStock: 'issuedItems.inventoryStatus.OutOfStock',
  NegativeStock: 'issuedItems.inventoryStatus.NegativeStock',
}

export const INVENTORY_STATUS_TONES: Record<InventoryStatus, BadgeTone> = {
  Normal: 'success',
  LowStock: 'warning',
  CriticalStock: 'danger',
  OutOfStock: 'danger',
  NegativeStock: 'danger',
}
