/**
 * Central command registry for the command palette. Commands never bypass permissions:
 * each carries the any-of permission list the sidebar/backend uses, and the palette filters
 * through the same hasAnyPermission gate. Navigation only — destructive actions keep their
 * own confirmation flows on the target pages.
 *
 * i18n-wave: `label` is een VERTAALSLEUTEL (commands.*) en `group` een stabiele code
 * ('create' | 'navigate') — nooit displaytekst als discriminator. De fuzzy matcher krijgt
 * de vertaalde label-tekst aangereikt zodat zoeken in elke UI-taal werkt (§47); keywords
 * bevatten NL/FR/EN-synoniemen zodat ook anderstalige termen blijven matchen.
 */

export type CommandGroup = 'create' | 'navigate'

export interface AppCommand {
  id: string
  /** Translation key (commands.*). */
  label: string
  /** Extra terms the fuzzy matcher may hit (NL/FR/EN synonyms). */
  keywords?: string
  route: string
  /** Any-of permissions; empty = any signed-in user. */
  permissions: string[]
  group: CommandGroup
}

export const APP_COMMANDS: AppCommand[] = [
  {
    id: 'create-order', label: 'commands.createOrder', keywords: 'order aanmaken create commande transport',
    route: '/transport-orders/new', permissions: ['orders.create', 'orders.manage'], group: 'create',
  },
  {
    id: 'create-customer', label: 'commands.createCustomer', keywords: 'customer aanmaken client créer',
    route: '/customers/new', permissions: ['customers.create'], group: 'create',
  },
  {
    id: 'create-trip', label: 'commands.createTrip', keywords: 'trip rit aanmaken planbord tournée planifier',
    route: '/planning-center', permissions: ['planning.create'], group: 'create',
  },
  {
    id: 'report-incident', label: 'commands.reportIncident', keywords: 'incident registreren déclarer report',
    route: '/incidents', permissions: ['incidents.manage'], group: 'create',
  },
  {
    id: 'go-planning-center', label: 'commands.goPlanningCenter', keywords: 'planning dispatcher board planbord tableau',
    route: '/planning-center', permissions: ['planning.view'], group: 'navigate',
  },
  {
    id: 'go-operations', label: 'commands.goOperations', keywords: 'operations control live operationeel centrum suivi direct',
    route: '/operations', permissions: ['operations.view'], group: 'navigate',
  },
  {
    id: 'go-driver', label: 'commands.goDriver', keywords: 'driver mobiel chauffeur app',
    route: '/driver', permissions: ['driver_workflow.view'], group: 'navigate',
  },
  {
    id: 'go-dashboard', label: 'commands.goDashboard', keywords: 'today overzicht tableau bord aujourd hui',
    route: '/dashboard', permissions: ['dashboard.view'], group: 'navigate',
  },
  {
    id: 'go-my-time', label: 'commands.goMyTime', keywords: 'uren prikklok inpunten uitpunten tijd attendance heures pointer hours clock',
    route: '/portal/time', permissions: ['attendance.self'], group: 'navigate',
  },
  {
    id: 'go-attendance', label: 'commands.goAttendance', keywords: 'aanwezigheid urenregistratie prikklok wie is er attendance présences pointage',
    route: '/attendance', permissions: ['attendance.view'], group: 'navigate',
  },
  {
    id: 'go-profitability', label: 'commands.goProfitability', keywords: 'marge profitability winst rentabilité',
    route: '/profitability', permissions: ['profitability.view'], group: 'navigate',
  },
  {
    id: 'go-dock-planning', label: 'commands.goDockPlanning', keywords: 'magazijn dock warehouse quai',
    route: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'], group: 'navigate',
  },
  {
    id: 'go-warehouses', label: 'commands.goWarehouses', keywords: 'warehouse depots magazijn entrepôt',
    route: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'], group: 'navigate',
  },
  {
    id: 'go-warehouse-scanning', label: 'commands.goWarehouseScanning', keywords: 'warehouse magazijn laadlijst scannen chargement scan',
    route: '/warehouse', permissions: ['warehouse.view'], group: 'navigate',
  },
  {
    id: 'go-packages', label: 'commands.goPackages', keywords: 'package barcode pakket colli colis',
    route: '/packages', permissions: ['packages.view'], group: 'navigate',
  },
  {
    id: 'go-dossiers', label: 'commands.goDossiers', keywords: 'dossier file',
    route: '/dossiers', permissions: ['dossiers.view'], group: 'navigate',
  },
]

type TranslateLabel = (key: string) => string

/**
 * Safe subsequence match ("plb" hits "PLanBord"), case-insensitive, used ONLY for the
 * static command list — entity search stays server-side and permission-filtered. The
 * haystack is the TRANSLATED label plus the multilingual keywords.
 */
export function matchesCommand(command: AppCommand, query: string, translate: TranslateLabel): boolean {
  const term = query.trim().toLowerCase()
  if (term.length === 0) return true
  const haystack = `${translate(command.label)} ${command.keywords ?? ''}`.toLowerCase()
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
  translate: TranslateLabel,
): AppCommand[] {
  return APP_COMMANDS
    .filter((command) => command.permissions.length === 0 || hasAnyPermission(command.permissions))
    .filter((command) => matchesCommand(command, query, translate))
    .slice(0, 8)
}
