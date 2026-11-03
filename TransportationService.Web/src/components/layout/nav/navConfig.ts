import {
  Activity, AlarmClock, ArrowLeftRight, Award, BadgeEuro, BarChart3, Bell, BellRing, BookOpen, Boxes,
  BriefcaseBusiness, Building2, Calculator, CalendarClock, CalendarDays, CalendarOff, CalendarRange,
  CalendarX2, CarFront, Caravan, CircleHelp, CirclePlus, CircleUser, ClipboardCopy, ClipboardList, Clock,
  Coins, Contact, Container, FileBarChart, FileCheck2, FileCog, FileSignature, FileText, Fingerprint, Flag,
  FolderOpen, Folders, Fuel, Gauge, GraduationCap, HandCoins, IdCard, Inbox, KanbanSquare, Keyboard,
  Languages, LayoutDashboard, LayoutGrid, ListTodo, MapPin, Megaphone, MessagesSquare, Network, Package,
  PackageOpen, PackageSearch, Palmtree, Plug, Radio, ReceiptText, Route, Ruler, ScanBarcode, SearchCheck,
  Send, Server, Settings, Shapes, ShieldAlert, ShieldCheck, Siren, SlidersHorizontal, Smartphone, SquareUser,
  Store, Table2, Tags, Timer, TrendingUp, TriangleAlert, Truck, Upload, UserCog, UserRound, Users,
  UsersRound, Warehouse, Wrench, type LucideIcon,
} from 'lucide-react'
import { LOOKUP_RESOURCES, type LookupGroup } from '../../../features/master-data/lookupRegistry'

export type BadgeKey = 'notifications'

export interface NavItem {
  /** TRANSLATION KEY (i18n-wave) — render via t(label); nooit rechtstreeks tonen. */
  label: string
  to: string
  /** Any-of permissions required to see this entry; omitted = visible to every signed-in user. */
  permissions?: string[]
  icon?: LucideIcon
  badge?: BadgeKey
  /** Exact-match highlighting: active only on this route, not descendant routes (NavLink `end`). */
  end?: boolean
  /** Future nested submenu — rendered as an inner collapsible group. */
  children?: NavItem[]
}

/** What a route-less sidebar row triggers; the shell that owns that behaviour handles it. */
export type NavActionId = 'shortcuts'

/** A sidebar row that opens something in place instead of navigating. */
export interface NavAction {
  id: NavActionId
  /** Translation key. */
  label: string
  icon?: LucideIcon
}

export interface NavSubgroup {
  /** Translation key. */
  label: string
  items: NavItem[]
}

export interface NavModule {
  id: string
  /** Translation key. */
  label: string
  icon: LucideIcon
  /** Rendered only when the signed-in user has an employee link. */
  requiresEmployee?: boolean
  items?: NavItem[]
  /** Rendered after `items`, with the same row styling. */
  actions?: NavAction[]
  subgroups?: NavSubgroup[]
}

/** Icon per lookup; a lookup added to the registry later shows the generic one until it gets its own. */
const LOOKUP_ICONS: Record<string, LucideIcon> = {
  departments: Network,
  'job-functions': BriefcaseBusiness,
  'vehicle-categories': CarFront,
  'trailer-categories': Caravan,
  'driver-categories': SquareUser,
  'customer-categories': Store,
  'issued-item-categories': Package,
  'task-categories': ClipboardList,
  languages: Languages,
  nationalities: Flag,
  'contract-types': FileSignature,
}

/** Lookup resources of one registry group as nav items (view+manage = any-of); lookups with a home elsewhere are skipped. */
function lookupItems(group: LookupGroup): NavItem[] {
  return LOOKUP_RESOURCES.filter((r) => r.group === group && !r.hiddenFromNav).map((r) => ({
    label: `navigation.lookups.${r.slug}`,
    to: `/master-data/${r.slug}`,
    icon: LOOKUP_ICONS[r.slug] ?? Tags,
    permissions: [r.viewPermission, r.managePermission],
  }))
}

/**
 * Alle configuratie onder één ingeklapte groep (Wave 1 §14): instellingen, prijzen,
 * koppelingen, beheer, HR-configuratie en stamgegevens. Paden blijven ongewijzigd;
 * enkel de groepering verandert. Stamgegevens blijft registry-gedreven: een lookup
 * toevoegen aan lookupRegistry maakt hem hier automatisch zichtbaar.
 * Labels zijn VERTAALSLEUTELS (navigation.menu.*) — de Sidebar/NavModule vertalen ze.
 */
function parametersModule(): NavModule {
  return {
    id: 'parameters',
    label: 'navigation.menu.modules.parameters',
    icon: Settings,
    items: [
      { label: 'navigation.menu.settings', icon: SlidersHorizontal, to: '/settings', end: true, permissions: ['company_settings.view', 'company_settings.manage'] },
    ],
    subgroups: [
      {
        label: 'navigation.menu.groups.pricing',
        items: [
          { label: 'navigation.menu.priceTables', icon: Table2, to: '/pricing/tables', permissions: ['tariffs.view', 'tariffs.manage'] },
          { label: 'navigation.menu.pricingSettings', icon: BadgeEuro, to: '/settings/pricing', permissions: ['tariffs.manage'] },
          { label: 'navigation.menu.costRates', icon: Coins, to: '/cost-rates', permissions: ['trip_costs.view', 'trip_costs.manage'] },
        ],
      },
      {
        label: 'navigation.menu.groups.integrationsNotifications',
        items: [
          { label: 'navigation.menu.edi', icon: ArrowLeftRight, to: '/edi', permissions: ['edi.view', 'edi.manage'] },
          { label: 'navigation.menu.integrations', icon: Plug, to: '/integrations', permissions: ['integrations.manage'] },
          { label: 'navigation.menu.notificationRules', icon: BellRing, to: '/settings/notifications', permissions: ['notification_rules.view'] },
          { label: 'navigation.menu.escalationRules', icon: Siren, to: '/settings/escalations', permissions: ['escalations.manage'] },
        ],
      },
      {
        label: 'navigation.menu.groups.administration',
        items: [
          { label: 'navigation.menu.users', icon: Users, to: '/users', permissions: ['users.view'] },
          { label: 'navigation.menu.rolesRights', icon: ShieldCheck, to: '/roles', permissions: ['roles.view'] },
          { label: 'navigation.menu.jobFunctionRoles', icon: UserCog, to: '/job-function-mappings', permissions: ['roles.view', 'roles.manage_permissions'] },
          { label: 'navigation.menu.accounting', icon: Calculator, to: '/settings/accounting', permissions: ['accounting.view', 'accounting.manage'] },
          { label: 'navigation.menu.documentRules', icon: FileCog, to: '/settings/document-rules', permissions: ['company_settings.view', 'company_settings.manage'] },
          { label: 'navigation.menu.chargePolicies', icon: HandCoins, to: '/settings/charge-policies', permissions: ['problems.approve_charge', 'incidents.manage'] },
          { label: 'navigation.menu.systemInfo', icon: Server, to: '/settings/system', permissions: ['system_info.view'] },
          { label: 'navigation.menu.legalEntities', icon: Building2, to: '/settings/legal-entities', permissions: ['legal_entities.view', 'legal_entities.manage'] },
          { label: 'navigation.menu.portalAnnouncements', icon: Megaphone, to: '/settings/portal-announcements', permissions: ['portal_announcements.manage'] },
          { label: 'navigation.menu.portalMessages', icon: MessagesSquare, to: '/settings/portal-messages', permissions: ['portal_messages.view', 'portal_messages.send'] },
        ],
      },
      {
        label: 'navigation.menu.groups.personnel',
        items: [
          { label: 'navigation.menu.leaveSettings', icon: Palmtree, to: '/settings/leave', permissions: ['leave_types.manage'] },
          { label: 'navigation.menu.attendanceSettings', icon: Timer, to: '/settings/attendance', permissions: ['attendance.manage_settings', 'attendance.manage_kiosks'] },
          { label: 'navigation.menu.hrReminders', icon: AlarmClock, to: '/settings/hr-reminders', permissions: ['hr_settings.manage'] },
          { label: 'navigation.menu.taskTemplates', icon: ClipboardCopy, to: '/settings/task-templates', permissions: ['tasks.manage_templates', 'tasks.manage_recurring'] },
        ],
      },
      {
        label: 'navigation.menu.groups.masterData',
        items: [
          { label: 'navigation.menu.activityTypes', icon: Shapes, to: '/settings/activity-types', permissions: ['activity_types.view', 'activity_types.manage'] },
          { label: 'navigation.menu.units', icon: Ruler, to: '/master-data/eenheden', permissions: ['unit_types.view', 'unit_types.manage', 'tariffs.view', 'tariffs.manage'] },
          { label: 'navigation.menu.servicesSurcharges', icon: CirclePlus, to: '/master-data/services', permissions: ['tariffs.view', 'tariffs.manage'] },
          ...lookupItems('organisatie'),
          ...lookupItems('referentie'),
          ...lookupItems('categorieen'),
        ],
      },
    ],
  }
}

/**
 * Wave 1 §14 doelboom: operationele groepen (Vandaag → Rapportage) + één Parameters-groep, afgesloten door Help.
 * Paden veranderen NOOIT — enkel groepering en labels. /my-trips staat bewust niet meer in de
 * zijbalk: chauffeurs krijgen de driver-shell, /driver enkel voor wie echt uitvoert.
 */
export function getNavModules(): NavModule[] {
  return [
    {
      id: 'portaal',
      label: 'navigation.menu.modules.portaal',
      icon: CircleUser,
      requiresEmployee: true,
      items: [
        { label: 'navigation.menu.myDashboard', icon: LayoutGrid, to: '/portal', end: true },
        { label: 'navigation.menu.myTime', icon: Clock, to: '/portal/time', permissions: ['attendance.self'] },
        { label: 'navigation.menu.myPlanning', icon: CalendarDays, to: '/portal/planning' },
        { label: 'navigation.menu.myAbsences', icon: CalendarOff, to: '/portal/absences' },
        { label: 'navigation.menu.myQualifications', icon: GraduationCap, to: '/portal/qualifications' },
        { label: 'navigation.menu.myProfile', icon: UserRound, to: '/portal/profile' },
      ],
    },
    {
      id: 'vandaag',
      label: 'navigation.menu.modules.vandaag',
      icon: LayoutDashboard,
      items: [
        { label: 'navigation.menu.dashboard', icon: Gauge, to: '/dashboard', permissions: ['dashboard.view'] },
        // UX-1: the way back from the inbox for whoever actually drives (not for view-only dispatchers).
        { label: 'navigation.menu.driverApp', icon: Smartphone, to: '/driver', permissions: ['driver_workflow.execute'] },
        { label: 'navigation.menu.inbox', icon: Inbox, to: '/inbox' },
        { label: 'navigation.menu.notifications', icon: Bell, to: '/notifications', badge: 'notifications' },
      ],
      subgroups: [
        {
          label: 'navigation.menu.groups.problems',
          items: [
            { label: 'navigation.menu.incidents', icon: ShieldAlert, to: '/incidents', permissions: ['incidents.view', 'incidents.manage'] },
            { label: 'navigation.menu.exceptions', icon: TriangleAlert, to: '/exceptions', permissions: ['exceptions.view'] },
          ],
        },
      ],
    },
    {
      id: 'dossiers',
      label: 'navigation.menu.modules.dossiers',
      icon: FolderOpen,
      items: [
        // Dossiers eerst: het dossier is het centrale werkobject; de klassieke
        // opdrachtenlijst blijft als secundaire ingang bestaan.
        { label: 'navigation.menu.dossiers', icon: Folders, to: '/dossiers', permissions: ['dossiers.view', 'dossiers.manage'] },
        { label: 'navigation.menu.classicOrders', icon: FileText, to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
        { label: 'navigation.menu.excelImport', icon: Upload, to: '/order-imports', permissions: ['orders.create', 'orders.manage'] },
      ],
    },
    {
      id: 'planning',
      label: 'navigation.menu.modules.planning',
      icon: CalendarClock,
      items: [
        { label: 'navigation.menu.planningBoard', icon: KanbanSquare, to: '/planning-center', permissions: ['planning.view'] },
        { label: 'navigation.menu.tripList', icon: Route, to: '/planning', permissions: ['planning.view'] },
        { label: 'navigation.menu.liveOperations', icon: Radio, to: '/operations', permissions: ['operations.view'] },
      ],
    },
    {
      id: 'magazijn',
      label: 'navigation.menu.modules.magazijn',
      icon: Warehouse,
      items: [
        { label: 'navigation.menu.warehouseScanning', icon: ScanBarcode, to: '/warehouse', permissions: ['warehouse.view'] },
        // SEC-H1: GET api/warehouse/trace no longer admits scanning-only roles (drivers).
        { label: 'navigation.menu.warehouseTrace', icon: SearchCheck, to: '/warehouse/trace', permissions: ['warehouse.view', 'warehouse.manage'] },
        { label: 'navigation.menu.warehousesAdmin', icon: Warehouse, to: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'] },
        { label: 'navigation.menu.dockPlanning', icon: CalendarRange, to: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'] },
      ],
    },
    {
      id: 'klanten',
      label: 'navigation.menu.modules.klanten',
      icon: Contact,
      items: [
        { label: 'navigation.menu.customers', icon: Contact, to: '/customers', permissions: ['customers.view'] },
        { label: 'navigation.menu.locations', icon: MapPin, to: '/locations', permissions: ['locations.view'] },
      ],
      subgroups: [
        {
          label: 'navigation.menu.groups.billing',
          items: [
            { label: 'navigation.menu.invoices', icon: ReceiptText, to: '/invoices', permissions: ['invoices.view'] },
            { label: 'navigation.menu.invoiceControl', icon: FileCheck2, to: '/invoice-control', permissions: ['invoices.view'] },
            { label: 'navigation.menu.peppol', icon: Send, to: '/peppol', permissions: ['peppol.view'] },
          ],
        },
      ],
    },
    {
      id: 'personeel',
      label: 'navigation.menu.modules.personeel',
      icon: UsersRound,
      items: [
        { label: 'navigation.menu.employees', icon: IdCard, to: '/employees', permissions: ['employees.view'] },
        { label: 'navigation.menu.tasks', icon: ListTodo, to: '/tasks', permissions: ['tasks.view_own', 'tasks.view_team', 'tasks.view_all'] },
        { label: 'navigation.menu.employeePlanning', icon: CalendarClock, to: '/employee-planning', permissions: ['employee_planning.view', 'employee_planning.manage'] },
        { label: 'navigation.menu.attendance', icon: Fingerprint, to: '/attendance', permissions: ['attendance.view'] },
        { label: 'navigation.menu.absences', icon: CalendarX2, to: '/absences', permissions: ['absences.view'] },
        { label: 'navigation.menu.qualifications', icon: Award, to: '/qualifications', permissions: ['employee_documents.view'] },
        // Voorraad van bedrijfsmiddelen hoort bij het personeelsdomein (uitgifte aan medewerkers).
        { label: 'navigation.menu.inventory', icon: Boxes, to: '/inventory', end: true, permissions: ['inventory.view', 'inventory.manage'] },
        // WH-H3/H4: de magazijnbalie — uitgeven, retour verwerken, openstaande uitgiftes en historiek.
        { label: 'navigation.menu.inventoryIssues', icon: PackageOpen, to: '/inventory/issues', permissions: ['inventory.issue', 'inventory.return', 'inventory.loans_view'] },
        // Beheer van bedrijfsmiddelen (categorieën + sjablonen) staat naast de voorraad, niet meer
        // verspreid over Parameters → Personeel en Parameters → Basisgegevens. Het item wijst naar de
        // sectiewortel (niet naar één tabblad): de prefix-match houdt het dan actief op zowel
        // /issued-items/categories als /issued-items/templates(/:id); de indexroute kiest het tabblad.
        { label: 'navigation.menu.issuedItemsAdmin', icon: PackageSearch, to: '/issued-items', permissions: ['issued_items.manage_templates', 'inventory.manage'] },
      ],
    },
    {
      id: 'vloot',
      label: 'navigation.menu.modules.vloot',
      icon: Truck,
      items: [
        { label: 'navigation.menu.fleetOverview', icon: LayoutDashboard, to: '/fleet', permissions: ['vehicles.view'] },
        { label: 'navigation.menu.vehicles', icon: Truck, to: '/vehicles', permissions: ['vehicles.view'] },
        { label: 'navigation.menu.trailers', icon: Container, to: '/trailers', permissions: ['trailers.view'] },
        { label: 'navigation.menu.tankCards', icon: Fuel, to: '/tank-cards', permissions: ['tank_cards.view'] },
        { label: 'navigation.menu.maintenance', icon: Wrench, to: '/maintenance-policies', permissions: ['maintenance_policies.view', 'maintenance_policies.manage'] },
      ],
    },
    {
      id: 'rapportage',
      label: 'navigation.menu.modules.rapportage',
      icon: BarChart3,
      items: [
        { label: 'navigation.menu.kpis', icon: Activity, to: '/kpi', permissions: ['kpi.view'] },
        { label: 'navigation.menu.profitability', icon: TrendingUp, to: '/profitability', permissions: ['profitability.view'] },
        { label: 'navigation.menu.reports', icon: FileBarChart, to: '/reports', permissions: ['reports.view'] },
      ],
    },
    parametersModule(),
    {
      id: 'help',
      label: 'navigation.menu.modules.help',
      icon: CircleHelp,
      // Ungated, like the /help route itself and the '?' shortcut.
      items: [{ label: 'help.title', to: '/help', icon: BookOpen }],
      actions: [{ id: 'shortcuts', label: 'ui.nav.shortcuts', icon: Keyboard }],
    },
  ]
}
