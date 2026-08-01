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

export const INVENTORY_STATUS_LABELS: Record<InventoryStatus, string> = {
  Normal: 'Normaal',
  LowStock: 'Lage voorraad',
  CriticalStock: 'Kritiek',
  OutOfStock: 'Niet op voorraad',
  NegativeStock: 'Negatief',
}

export const INVENTORY_STATUS_TONES: Record<InventoryStatus, BadgeTone> = {
  Normal: 'success',
  LowStock: 'warning',
  CriticalStock: 'danger',
  OutOfStock: 'danger',
  NegativeStock: 'danger',
}
