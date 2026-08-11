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
 * empty keys fall back to the default icon.
 */
export const ACTIVITY_TYPE_ICONS: Record<string, { icon: LucideIcon; label: string }> = {
  route: { icon: Route, label: 'Route (distributie)' },
  truck: { icon: Truck, label: 'Vrachtwagen (transport)' },
  crane: { icon: Construction, label: 'Kraan' },
  trailer: { icon: Caravan, label: 'Plateau / aanhanger' },
  warehouse: { icon: Warehouse, label: 'Magazijn (opslag)' },
  zap: { icon: Zap, label: 'Bliksem (express)' },
  rotate: { icon: RotateCcw, label: 'Herlevering (retour)' },
  move: { icon: Move, label: 'Positionering (lege rit)' },
  more: { icon: MoreHorizontal, label: 'Overig' },
  box: { icon: Box, label: 'Colli / doos' },
  ship: { icon: Ship, label: 'Schip (container)' },
  snowflake: { icon: Snowflake, label: 'Koeltransport' },
}

export const DEFAULT_ACTIVITY_TYPE_ICON: LucideIcon = ClipboardList

/** Resolves an icon key to its component; unknown/null keys render the default icon. */
export function activityTypeIcon(key: string | null | undefined): LucideIcon {
  return (key ? ACTIVITY_TYPE_ICONS[key]?.icon : undefined) ?? DEFAULT_ACTIVITY_TYPE_ICON
}
