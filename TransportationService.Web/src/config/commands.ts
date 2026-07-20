/**
 * Central command registry for the command palette. Commands never bypass permissions:
 * each carries the any-of permission list the sidebar/backend uses, and the palette filters
 * through the same hasAnyPermission gate. Navigation only — destructive actions keep their
 * own confirmation flows on the target pages.
 */

export interface AppCommand {
  id: string
  label: string
  /** Extra terms the fuzzy matcher may hit (Dutch synonyms). */
  keywords?: string
  route: string
  /** Any-of permissions; empty = any signed-in user. */
  permissions: string[]
  group: 'Aanmaken' | 'Navigeren'
}

export const APP_COMMANDS: AppCommand[] = [
  {
    id: 'create-order', label: 'Nieuwe transportopdracht', keywords: 'order aanmaken create',
    route: '/transport-orders/new', permissions: ['orders.create', 'orders.manage'], group: 'Aanmaken',
  },
  {
    id: 'create-customer', label: 'Nieuwe klant', keywords: 'customer aanmaken',
    route: '/customers/new', permissions: ['customers.create'], group: 'Aanmaken',
  },
  {
    id: 'create-trip', label: 'Nieuwe rit plannen', keywords: 'trip rit aanmaken planbord',
    route: '/planning-center', permissions: ['planning.create'], group: 'Aanmaken',
  },
  {
    id: 'report-incident', label: 'Incident melden', keywords: 'incident registreren',
    route: '/incidents', permissions: ['incidents.manage'], group: 'Aanmaken',
  },
  {
    id: 'go-planning-center', label: 'Planbord openen', keywords: 'planning dispatcher board',
    route: '/planning-center', permissions: ['planning.view'], group: 'Navigeren',
  },
  {
    id: 'go-operations', label: 'Operationeel centrum openen', keywords: 'operations control live',
    route: '/operations', permissions: ['operations.view'], group: 'Navigeren',
  },
  {
    id: 'go-driver', label: 'Chauffeursapp openen', keywords: 'driver mobiel',
    route: '/driver', permissions: ['driver_workflow.view'], group: 'Navigeren',
  },
  {
    id: 'go-dashboard', label: 'Dashboard vandaag openen', keywords: 'today overzicht',
    route: '/dashboard', permissions: ['dashboard.view'], group: 'Navigeren',
  },
  {
    id: 'go-profitability', label: 'Rendement openen', keywords: 'marge profitability winst',
    route: '/profitability', permissions: ['profitability.view'], group: 'Navigeren',
  },
  {
    id: 'go-dock-planning', label: 'Dockplanning openen', keywords: 'magazijn dock warehouse',
    route: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'], group: 'Navigeren',
  },
  {
    id: 'go-warehouses', label: 'Magazijnen openen', keywords: 'warehouse depots',
    route: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'], group: 'Navigeren',
  },
  {
    id: 'go-packages', label: 'Colli zoeken', keywords: 'package barcode pakket',
    route: '/packages', permissions: ['packages.view'], group: 'Navigeren',
  },
  {
    id: 'go-dossiers', label: 'Dossiers openen', keywords: 'dossier',
    route: '/dossiers', permissions: ['dossiers.view'], group: 'Navigeren',
  },
]

/**
 * Safe subsequence match ("plb" hits "PLanBord"), case-insensitive, used ONLY for the
 * static command list — entity search stays server-side and permission-filtered.
 */
export function matchesCommand(command: AppCommand, query: string): boolean {
  const term = query.trim().toLowerCase()
  if (term.length === 0) return true
  const haystack = `${command.label} ${command.keywords ?? ''}`.toLowerCase()
  if (haystack.includes(term)) return true
  let position = 0
  for (const char of term) {
    position = haystack.indexOf(char, position)
    if (position === -1) return false
    position += 1
  }
  return true
}

export function filterCommands(
  query: string,
  hasAnyPermission: (permissions: string[]) => boolean,
): AppCommand[] {
  return APP_COMMANDS
    .filter((command) => command.permissions.length === 0 || hasAnyPermission(command.permissions))
    .filter((command) => matchesCommand(command, query))
    .slice(0, 8)
}
