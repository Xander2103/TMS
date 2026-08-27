import {
  Box,
  Caravan,
  ClipboardList,
  Construction,
  MoreHorizontal,
  Move,
  RotateCcw,
  Route,
  Ship,
  Snowflake,
  Truck,
  Warehouse,
  Zap,
  type LucideIcon,
} from 'lucide-react'

/**
 * Curated icon map for activity types. The backend stores only the KEY (max 50 chars);
 * rendering resolves it here so tenants can never inject arbitrary icon names. Unknown or
 * empty keys fall back to the default icon. `labelKey` is a translation key — render via t().
 */
export const ACTIVITY_TYPE_ICONS: Record<string, { icon: LucideIcon; labelKey: string }> = {
  route: { icon: Route, labelKey: 'dossiers.icons.route' },
  truck: { icon: Truck, labelKey: 'dossiers.icons.truck' },
  crane: { icon: Construction, labelKey: 'dossiers.icons.crane' },
  trailer: { icon: Caravan, labelKey: 'dossiers.icons.trailer' },
  warehouse: { icon: Warehouse, labelKey: 'dossiers.icons.warehouse' },
  zap: { icon: Zap, labelKey: 'dossiers.icons.zap' },
  rotate: { icon: RotateCcw, labelKey: 'dossiers.icons.rotate' },
  move: { icon: Move, labelKey: 'dossiers.icons.move' },
  more: { icon: MoreHorizontal, labelKey: 'dossiers.icons.more' },
  box: { icon: Box, labelKey: 'dossiers.icons.box' },
  ship: { icon: Ship, labelKey: 'dossiers.icons.ship' },
  snowflake: { icon: Snowflake, labelKey: 'dossiers.icons.snowflake' },
}

export const DEFAULT_ACTIVITY_TYPE_ICON: LucideIcon = ClipboardList

/** Resolves an icon key to its component; unknown/null keys render the default icon. */
export function activityTypeIcon(key: string | null | undefined): LucideIcon {
  return (key ? ACTIVITY_TYPE_ICONS[key]?.icon : undefined) ?? DEFAULT_ACTIVITY_TYPE_ICON
}
