/**
 * Redesign 2026-09-12: the transport order is one shell with subsection navigation. The URL
 * segment after the order id selects the section; the overview is the order root.
 */
export const ORDER_TABS = ['overzicht', 'lading', 'prijs', 'stops', 'colli', 'historiek', 'berichten'] as const

export type OrderTab = (typeof ORDER_TABS)[number]

export const ORDER_TAB_LABEL_KEYS: Record<OrderTab, string> = {
  overzicht: 'transportOrders.detail.nav.overview',
  lading: 'transportOrders.detail.nav.lading',
  prijs: 'transportOrders.detail.nav.price',
  stops: 'transportOrders.detail.nav.stops',
  colli: 'transportOrders.detail.nav.colli',
  historiek: 'transportOrders.detail.nav.history',
  berichten: 'transportOrders.detail.nav.messages',
}

export function isOrderTab(value: string | undefined): value is OrderTab {
  return value !== undefined && (ORDER_TABS as readonly string[]).includes(value)
}

export function orderTabPath(orderId: string, tab: OrderTab): string {
  return tab === 'overzicht' ? `/transport-orders/${orderId}` : `/transport-orders/${orderId}/${tab}`
}
