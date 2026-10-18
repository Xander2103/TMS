import { useAuth } from './authContextValue'

/**
 * SEC-M1: the "price right". Mirrors the backend `CommercialVisibility.PriceViewerPermissions`
 * and MUST stay in sync with it: a caller holding none of these gets every commercial amount
 * stripped from orders, dossiers, customer pricing and timelines (agreed/calculated prices,
 * pricing lines, financial totals, invoice links and invoice statuses). The UI then leaves the
 * price surfaces out altogether instead of rendering "—", "€ 0,00" or "Nog geen prijs".
 */
export const PRICE_VIEWER_PERMISSIONS: readonly string[] = [
  'orders.create',
  'orders.edit',
  'orders.manage',
  'orders.override_price',
  'orders.lock_price',
  'orders.confirm_incomplete_price',
  'dossiers.price',
  'tariffs.view',
  'tariffs.manage',
  'invoices.view',
  'accounting.view',
  'profitability.view',
  'problems.approve_charge',
]

/** Whether the signed-in user may see commercial amounts (see PRICE_VIEWER_PERMISSIONS). */
export function useCanViewPrices(): boolean {
  const { hasAnyPermission } = useAuth()
  return hasAnyPermission([...PRICE_VIEWER_PERMISSIONS])
}
