import {
  BarChart3, CalendarClock, CircleUser, Contact, FolderOpen, LayoutDashboard,
  Settings, Truck, UsersRound, Warehouse, type LucideIcon,
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
  subgroups?: NavSubgroup[]
}

/** Lookup resources of one registry group as nav items (view+manage = any-of). */
function lookupItems(group: LookupGroup): NavItem[] {
  return LOOKUP_RESOURCES.filter((r) => r.group === group).map((r) => ({
    label: `navigation.lookups.${r.slug}`,
    to: `/master-data/${r.slug}`,
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
      { label: 'navigation.menu.settings', to: '/settings', end: true, permissions: ['company_settings.view', 'company_settings.manage'] },
    ],
    subgroups: [
      {
        label: 'navigation.menu.groups.pricing',
        items: [
          { label: 'navigation.menu.priceTables', to: '/pricing/tables', permissions: ['tariffs.view', 'tariffs.manage'] },
          { label: 'navigation.menu.pricingSettings', to: '/settings/pricing', permissions: ['tariffs.manage'] },
          { label: 'navigation.menu.costRates', to: '/cost-rates', permissions: ['trip_costs.view', 'trip_costs.manage'] },
        ],
      },
      {
        label: 'navigation.menu.groups.integrationsNotifications',
        items: [
          { label: 'navigation.menu.edi', to: '/edi', permissions: ['edi.view', 'edi.manage'] },
          { label: 'navigation.menu.integrations', to: '/integrations', permissions: ['integrations.manage'] },
          { label: 'navigation.menu.notificationRules', to: '/settings/notifications', permissions: ['notification_rules.view'] },
          { label: 'navigation.menu.escalationRules', to: '/settings/escalations', permissions: ['escalations.manage'] },
        ],
      },
      {
        label: 'navigation.menu.groups.administration',
        items: [
          { label: 'navigation.menu.users', to: '/users', permissions: ['users.view'] },
          { label: 'navigation.menu.rolesRights', to: '/roles', permissions: ['roles.view'] },
          { label: 'navigation.menu.jobFunctionRoles', to: '/job-function-mappings', permissions: ['roles.view', 'roles.manage_permissions'] },
          { label: 'navigation.menu.accounting', to: '/settings/accounting', permissions: ['accounting.view', 'accounting.manage'] },
          { label: 'navigation.menu.documentRules', to: '/settings/document-rules', permissions: ['company_settings.view', 'company_settings.manage'] },
          { label: 'navigation.menu.chargePolicies', to: '/settings/charge-policies', permissions: ['problems.approve_charge', 'incidents.manage'] },
          { label: 'navigation.menu.systemInfo', to: '/settings/system', permissions: ['system_info.view'] },
          { label: 'navigation.menu.legalEntities', to: '/settings/legal-entities', permissions: ['legal_entities.view', 'legal_entities.manage'] },
          { label: 'navigation.menu.portalAnnouncements', to: '/settings/portal-announcements', permissions: ['portal_announcements.manage'] },
          { label: 'navigation.menu.portalMessages', to: '/settings/portal-messages', permissions: ['portal_messages.view', 'portal_messages.send'] },
        ],
      },
      {
        label: 'navigation.menu.groups.personnel',
        items: [
          { label: 'navigation.menu.leaveSettings', to: '/settings/leave', permissions: ['leave_types.manage'] },
          { label: 'navigation.menu.attendanceSettings', to: '/settings/attendance', permissions: ['attendance.manage_settings', 'attendance.manage_kiosks'] },
          { label: 'navigation.menu.hrReminders', to: '/settings/hr-reminders', permissions: ['hr_settings.manage'] },
          { label: 'navigation.menu.issuedItemTemplates', to: '/settings/issued-item-templates', permissions: ['issued_items.manage_templates'] },
          { label: 'navigation.menu.taskTemplates', to: '/settings/task-templates', permissions: ['tasks.manage_templates', 'tasks.manage_recurring'] },
        ],
      },
      {
        label: 'navigation.menu.groups.masterData',
        items: [
          { label: 'navigation.menu.activityTypes', to: '/settings/activity-types', permissions: ['activity_types.view', 'activity_types.manage'] },
          { label: 'navigation.menu.units', to: '/master-data/eenheden', permissions: ['unit_types.view', 'unit_types.manage', 'tariffs.view', 'tariffs.manage'] },
          { label: 'navigation.menu.servicesSurcharges', to: '/master-data/services', permissions: ['tariffs.view', 'tariffs.manage'] },
          ...lookupItems('organisatie'),
          ...lookupItems('referentie'),
          ...lookupItems('categorieen'),
        ],
      },
    ],
  }
}

/**
 * Wave 1 §14 doelboom: operationele groepen (Vandaag → Rapportage) + één Parameters-groep.
 * Paden veranderen NOOIT — enkel groepering en labels. /my-trips en /driver staan bewust
 * niet meer in de zijbalk: chauffeurs krijgen de driver-shell, de routes blijven bestaan.
 */
export function getNavModules(): NavModule[] {
  return [
    {
      id: 'portaal',
      label: 'navigation.menu.modules.portaal',
      icon: CircleUser,
      requiresEmployee: true,
      items: [
        { label: 'navigation.menu.myDashboard', to: '/portal', end: true },
        { label: 'navigation.menu.myTime', to: '/portal/time', permissions: ['attendance.self'] },
        { label: 'navigation.menu.myPlanning', to: '/portal/planning' },
        { label: 'navigation.menu.myAbsences', to: '/portal/absences' },
        { label: 'navigation.menu.myQualifications', to: '/portal/qualifications' },
        { label: 'navigation.menu.myProfile', to: '/portal/profile' },
      ],
    },
    {
      id: 'vandaag',
      label: 'navigation.menu.modules.vandaag',
      icon: LayoutDashboard,
      items: [
        { label: 'navigation.menu.dashboard', to: '/dashboard', permissions: ['dashboard.view'] },
        { label: 'navigation.menu.inbox', to: '/inbox' },
        { label: 'navigation.menu.notifications', to: '/notifications', badge: 'notifications' },
      ],
      subgroups: [
        {
          label: 'navigation.menu.groups.problems',
          items: [
            { label: 'navigation.menu.incidents', to: '/incidents', permissions: ['incidents.view', 'incidents.manage'] },
            { label: 'navigation.menu.exceptions', to: '/exceptions', permissions: ['exceptions.view'] },
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
        { label: 'navigation.menu.dossiers', to: '/dossiers', permissions: ['dossiers.view', 'dossiers.manage'] },
        { label: 'navigation.menu.classicOrders', to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
        { label: 'navigation.menu.excelImport', to: '/order-imports', permissions: ['orders.create', 'orders.manage'] },
      ],
    },
    {
      id: 'planning',
      label: 'navigation.menu.modules.planning',
      icon: CalendarClock,
      items: [
        { label: 'navigation.menu.planningBoard', to: '/planning-center', permissions: ['planning.view'] },
        { label: 'navigation.menu.tripList', to: '/planning', permissions: ['planning.view'] },
        { label: 'navigation.menu.liveOperations', to: '/operations', permissions: ['operations.view'] },
      ],
    },
    {
      id: 'magazijn',
      label: 'navigation.menu.modules.magazijn',
      icon: Warehouse,
      items: [
        { label: 'navigation.menu.warehouseScanning', to: '/warehouse', permissions: ['warehouse.view'] },
        { label: 'navigation.menu.warehouseTrace', to: '/warehouse/trace', permissions: ['warehouse.view', 'scanning.execute'] },
        { label: 'navigation.menu.warehousesAdmin', to: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'] },
        { label: 'navigation.menu.dockPlanning', to: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'] },
      ],
    },
    {
      id: 'klanten',
      label: 'navigation.menu.modules.klanten',
      icon: Contact,
      items: [
        { label: 'navigation.menu.customers', to: '/customers', permissions: ['customers.view'] },
        { label: 'navigation.menu.locations', to: '/locations', permissions: ['locations.view'] },
      ],
      subgroups: [
        {
          label: 'navigation.menu.groups.billing',
          items: [
            { label: 'navigation.menu.invoices', to: '/invoices', permissions: ['invoices.view'] },
            { label: 'navigation.menu.invoiceControl', to: '/invoice-control', permissions: ['invoices.view'] },
            { label: 'navigation.menu.peppol', to: '/peppol', permissions: ['peppol.view'] },
          ],
        },
      ],
    },
    {
      id: 'personeel',
      label: 'navigation.menu.modules.personeel',
      icon: UsersRound,
      items: [
        { label: 'navigation.menu.employees', to: '/employees', permissions: ['employees.view'] },
        { label: 'navigation.menu.tasks', to: '/tasks', permissions: ['tasks.view_own', 'tasks.view_team', 'tasks.view_all'] },
        { label: 'navigation.menu.employeePlanning', to: '/employee-planning', permissions: ['employee_planning.view', 'employee_planning.manage'] },
        { label: 'navigation.menu.attendance', to: '/attendance', permissions: ['attendance.view'] },
        { label: 'navigation.menu.absences', to: '/absences', permissions: ['absences.view'] },
        { label: 'navigation.menu.qualifications', to: '/qualifications', permissions: ['employee_documents.view'] },
        // Voorraad van bedrijfsmiddelen hoort bij het personeelsdomein (uitgifte aan medewerkers).
        { label: 'navigation.menu.inventory', to: '/inventory', permissions: ['inventory.view', 'inventory.manage'] },
      ],
    },
    {
      id: 'vloot',
      label: 'navigation.menu.modules.vloot',
      icon: Truck,
      items: [
        { label: 'navigation.menu.fleetOverview', to: '/fleet', permissions: ['vehicles.view'] },
        { label: 'navigation.menu.vehicles', to: '/vehicles', permissions: ['vehicles.view'] },
        { label: 'navigation.menu.trailers', to: '/trailers', permissions: ['trailers.view'] },
        { label: 'navigation.menu.tankCards', to: '/tank-cards', permissions: ['tank_cards.view'] },
        { label: 'navigation.menu.maintenance', to: '/maintenance-policies', permissions: ['maintenance_policies.view', 'maintenance_policies.manage'] },
      ],
    },
    {
      id: 'rapportage',
      label: 'navigation.menu.modules.rapportage',
      icon: BarChart3,
      items: [
        { label: 'navigation.menu.kpis', to: '/kpi', permissions: ['kpi.view'] },
        { label: 'navigation.menu.profitability', to: '/profitability', permissions: ['profitability.view'] },
        { label: 'navigation.menu.reports', to: '/reports', permissions: ['reports.view'] },
      ],
    },
    parametersModule(),
  ]
}
