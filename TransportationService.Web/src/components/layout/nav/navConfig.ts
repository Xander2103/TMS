import {
  BarChart3, CalendarClock, CircleUser, Contact, FolderOpen, LayoutDashboard,
  Settings, Truck, UsersRound, Warehouse, type LucideIcon,
} from 'lucide-react'
import { LOOKUP_RESOURCES, type LookupGroup } from '../../../features/master-data/lookupRegistry'

export type BadgeKey = 'notifications'

export interface NavItem {
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
  label: string
  items: NavItem[]
}

export interface NavModule {
  id: string
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
    label: r.title,
    to: `/master-data/${r.slug}`,
    permissions: [r.viewPermission, r.managePermission],
  }))
}

/**
 * Alle configuratie onder één ingeklapte groep (Wave 1 §14): instellingen, prijzen,
 * koppelingen, beheer, HR-configuratie en stamgegevens. Paden blijven ongewijzigd;
 * enkel de groepering verandert. Stamgegevens blijft registry-gedreven: een lookup
 * toevoegen aan lookupRegistry maakt hem hier automatisch zichtbaar.
 */
function parametersModule(): NavModule {
  return {
    id: 'parameters',
    label: 'Parameters',
    icon: Settings,
    items: [
      { label: 'Instellingen', to: '/settings', end: true, permissions: ['company_settings.view', 'company_settings.manage'] },
    ],
    subgroups: [
      {
        label: 'Prijzen',
        items: [
          { label: 'Tarieventabellen', to: '/pricing/tables', permissions: ['tariffs.view', 'tariffs.manage'] },
          { label: 'Prijsinstellingen', to: '/settings/pricing', permissions: ['tariffs.manage'] },
          { label: 'Kostentarieven', to: '/cost-rates', permissions: ['trip_costs.view', 'trip_costs.manage'] },
        ],
      },
      {
        label: 'Koppelingen & meldingen',
        items: [
          { label: 'EDI', to: '/edi', permissions: ['edi.view', 'edi.manage'] },
          { label: 'Integraties', to: '/integrations', permissions: ['integrations.manage'] },
          { label: 'Meldingen en e-mails', to: '/settings/notifications', permissions: ['notification_rules.view'] },
          { label: 'Escalatieregels', to: '/settings/escalations', permissions: ['escalations.manage'] },
        ],
      },
      {
        label: 'Beheer',
        items: [
          { label: 'Gebruikers', to: '/users', permissions: ['users.view'] },
          { label: 'Rollen & rechten', to: '/roles', permissions: ['roles.view'] },
          { label: 'Functie → rol', to: '/job-function-mappings', permissions: ['roles.view', 'roles.manage_permissions'] },
          { label: 'Boekhouding', to: '/settings/accounting', permissions: ['accounting.view', 'accounting.manage'] },
          { label: 'Eigen bedrijven', to: '/settings/legal-entities', permissions: ['legal_entities.view', 'legal_entities.manage'] },
          { label: 'Klantportaal mededelingen', to: '/settings/portal-announcements', permissions: ['portal_announcements.manage'] },
          { label: 'Portaalberichten', to: '/settings/portal-messages', permissions: ['portal_messages.view', 'portal_messages.send'] },
        ],
      },
      {
        label: 'Personeel',
        items: [
          { label: 'Verlof (types & saldi)', to: '/settings/leave', permissions: ['leave_types.manage'] },
          { label: 'HR-herinneringen', to: '/settings/hr-reminders', permissions: ['hr_settings.manage'] },
          { label: 'Bedrijfsmiddelen (sjablonen)', to: '/settings/issued-item-templates', permissions: ['issued_items.manage_templates'] },
          { label: 'Taaksjablonen', to: '/settings/task-templates', permissions: ['tasks.manage_templates', 'tasks.manage_recurring'] },
        ],
      },
      {
        label: 'Stamgegevens',
        items: [
          { label: 'Activiteitstypes', to: '/settings/activity-types', permissions: ['activity_types.view', 'activity_types.manage'] },
          { label: 'Eenheden', to: '/master-data/eenheden', permissions: ['unit_types.view', 'unit_types.manage', 'tariffs.view', 'tariffs.manage'] },
          { label: 'Services & toeslagen', to: '/master-data/services', permissions: ['tariffs.view', 'tariffs.manage'] },
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
      label: 'Mijn portaal',
      icon: CircleUser,
      requiresEmployee: true,
      items: [
        { label: 'Mijn dashboard', to: '/portal', end: true },
        { label: 'Mijn planning', to: '/portal/planning' },
        { label: 'Mijn afwezigheden', to: '/portal/absences' },
        { label: 'Mijn kwalificaties', to: '/portal/qualifications' },
        { label: 'Mijn profiel', to: '/portal/profile' },
      ],
    },
    {
      id: 'vandaag',
      label: 'Vandaag',
      icon: LayoutDashboard,
      items: [
        { label: 'Dashboard', to: '/dashboard', permissions: ['dashboard.view'] },
        { label: 'Berichten', to: '/inbox' },
        { label: 'Meldingen', to: '/notifications', badge: 'notifications' },
      ],
      subgroups: [
        {
          label: 'Problemen',
          items: [
            { label: 'Incidenten', to: '/incidents', permissions: ['incidents.view', 'incidents.manage'] },
            { label: 'Afwijkingen', to: '/exceptions', permissions: ['exceptions.view'] },
          ],
        },
      ],
    },
    {
      id: 'dossiers',
      label: 'Dossiers',
      icon: FolderOpen,
      items: [
        // Dossiers eerst: het dossier is het centrale werkobject; de klassieke
        // opdrachtenlijst blijft als secundaire ingang bestaan.
        { label: 'Dossiers', to: '/dossiers', permissions: ['dossiers.view', 'dossiers.manage'] },
        { label: 'Opdrachten (klassiek)', to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
      ],
    },
    {
      id: 'planning',
      label: 'Planning',
      icon: CalendarClock,
      items: [
        { label: 'Planbord', to: '/planning-center', permissions: ['planning.view'] },
        { label: 'Ritlijst', to: '/planning', permissions: ['planning.view'] },
        { label: 'Live opvolging', to: '/operations', permissions: ['operations.view'] },
      ],
    },
    {
      id: 'magazijn',
      label: 'Magazijn',
      icon: Warehouse,
      items: [
        { label: 'Laden & scannen', to: '/warehouse', permissions: ['warehouse.view'] },
        { label: 'Trace & voorraad', to: '/warehouse/trace', permissions: ['warehouse.view', 'scanning.execute'] },
        { label: 'Magazijnen (beheer)', to: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'] },
        { label: 'Dockplanning', to: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'] },
      ],
    },
    {
      id: 'klanten',
      label: 'Klanten',
      icon: Contact,
      items: [
        { label: 'Klanten', to: '/customers', permissions: ['customers.view'] },
        { label: 'Locaties', to: '/locations', permissions: ['locations.view'] },
      ],
      subgroups: [
        {
          label: 'Facturatie',
          items: [
            { label: 'Facturen', to: '/invoices', permissions: ['invoices.view'] },
            { label: 'Peppol', to: '/peppol', permissions: ['peppol.view'] },
          ],
        },
      ],
    },
    {
      id: 'personeel',
      label: 'Personeel',
      icon: UsersRound,
      items: [
        { label: 'Medewerkers', to: '/employees', permissions: ['employees.view'] },
        { label: 'Taken', to: '/tasks', permissions: ['tasks.view_own', 'tasks.view_team', 'tasks.view_all'] },
        { label: 'Personeelsplanning', to: '/employee-planning', permissions: ['employee_planning.view', 'employee_planning.manage'] },
        { label: 'Afwezigheden', to: '/absences', permissions: ['absences.view'] },
        { label: 'Kwalificaties', to: '/qualifications', permissions: ['employee_documents.view'] },
        // Voorraad van bedrijfsmiddelen hoort bij het personeelsdomein (uitgifte aan medewerkers).
        { label: 'Voorraad', to: '/inventory', permissions: ['inventory.view', 'inventory.manage'] },
      ],
    },
    {
      id: 'vloot',
      label: 'Vloot',
      icon: Truck,
      items: [
        { label: 'Vlootoverzicht', to: '/fleet', permissions: ['vehicles.view'] },
        { label: 'Voertuigen', to: '/vehicles', permissions: ['vehicles.view'] },
        { label: 'Opleggers', to: '/trailers', permissions: ['trailers.view'] },
        { label: 'Tankkaarten', to: '/tank-cards', permissions: ['tank_cards.view'] },
        { label: 'Onderhoud', to: '/maintenance-policies', permissions: ['maintenance_policies.view', 'maintenance_policies.manage'] },
      ],
    },
    {
      id: 'rapportage',
      label: 'Rapportage',
      icon: BarChart3,
      items: [
        { label: "KPI's", to: '/kpi', permissions: ['kpi.view'] },
        { label: 'Rendement', to: '/profitability', permissions: ['profitability.view'] },
        { label: 'Rapporten', to: '/reports', permissions: ['reports.view'] },
      ],
    },
    parametersModule(),
  ]
}
