import {
  BadgeEuro, ClipboardList, Contact, CircleUser, Database, LayoutDashboard,
  MessageSquare, Settings, Truck, UsersRound, Warehouse, type LucideIcon,
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

/** Stamgegevens is data-driven: adding a lookup to the registry makes it appear here. */
function masterDataModule(): NavModule {
  return {
    id: 'stamgegevens',
    label: 'Stamgegevens',
    icon: Database,
    subgroups: [
      {
        label: 'Algemeen',
        items: [
          { label: 'Eigen bedrijven', to: '/settings/legal-entities', permissions: ['legal_entities.view', 'legal_entities.manage'] },
          { label: 'Verlof (types & saldi)', to: '/settings/leave', permissions: ['leave_types.manage'] },
          { label: 'Eenheden', to: '/master-data/eenheden', permissions: ['unit_types.view', 'unit_types.manage', 'tariffs.view', 'tariffs.manage'] },
          { label: 'Services & toeslagen', to: '/master-data/services', permissions: ['tariffs.view', 'tariffs.manage'] },
          ...lookupItems('organisatie'),
          ...lookupItems('referentie'),
        ],
      },
      { label: 'Categorieën', items: lookupItems('categorieen') },
      {
        // Truly template-only after omitting not-yet-built items, so it stays "Templates".
        label: 'Templates',
        items: [
          { label: 'Bedrijfsmiddelen (sjablonen)', to: '/settings/issued-item-templates', permissions: ['issued_items.manage_templates'] },
        ],
      },
    ],
  }
}

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
      id: 'dashboard',
      label: 'Dashboard',
      icon: LayoutDashboard,
      items: [
        { label: 'Dashboard', to: '/dashboard', permissions: ['dashboard.view'] },
        { label: "KPI's", to: '/kpi', permissions: ['kpi.view'] },
        { label: 'Rendement', to: '/profitability', permissions: ['profitability.view'] },
        { label: 'Rapporten', to: '/reports', permissions: ['reports.view'] },
      ],
    },
    {
      id: 'transport',
      label: 'Transport',
      icon: ClipboardList,
      items: [
        { label: 'Transportopdrachten', to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
        { label: 'Dossiers', to: '/dossiers', permissions: ['dossiers.view', 'dossiers.manage'] },
        { label: 'Planning', to: '/planning', permissions: ['planning.view'] },
        { label: 'Planbord', to: '/planning-center', permissions: ['planning.view'] },
        { label: 'Operationeel centrum', to: '/operations', permissions: ['operations.view'] },
        { label: 'Mijn ritten', to: '/my-trips', permissions: ['driver_workflow.view'] },
        { label: 'Chauffeursapp', to: '/driver', permissions: ['driver_workflow.view'] },
      ],
    },
    {
      id: 'magazijn',
      label: 'Magazijn',
      icon: Warehouse,
      items: [
        { label: 'Magazijnen', to: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'] },
        { label: 'Magazijn', to: '/warehouse', permissions: ['warehouse.view'] },
        { label: 'Dockplanning', to: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'] },
        { label: 'Incidenten', to: '/incidents', permissions: ['incidents.view', 'incidents.manage'] },
        { label: 'Afwijkingen', to: '/exceptions', permissions: ['exceptions.view'] },
      ],
    },
    {
      id: 'klanten',
      label: 'Klanten',
      icon: Contact,
      items: [
        { label: 'Klanten', to: '/customers', permissions: ['customers.view'] },
        { label: 'Klantportaal', to: '/customer-portal', permissions: ['customer_portal.view'] },
        { label: 'Facturen', to: '/invoices', permissions: ['invoices.view'] },
        { label: 'Kostentarieven', to: '/cost-rates', permissions: ['trip_costs.view', 'trip_costs.manage'] },
      ],
    },
    {
      id: 'prijzen',
      label: 'Prijzen',
      icon: BadgeEuro,
      items: [
        { label: 'Tarieventabellen', to: '/pricing/tables', permissions: ['tariffs.view', 'tariffs.manage'] },
        { label: 'Prijsinstellingen', to: '/settings/pricing', permissions: ['tariffs.manage'] },
      ],
    },
    {
      id: 'personeel',
      label: 'Personeel',
      icon: UsersRound,
      items: [
        { label: 'Medewerkers', to: '/employees', permissions: ['employees.view'] },
        { label: 'Personeelsplanning', to: '/employee-planning', permissions: ['employee_planning.view', 'employee_planning.manage'] },
        { label: 'Afwezigheden', to: '/absences', permissions: ['absences.view'] },
        { label: 'Kwalificaties', to: '/qualifications', permissions: ['employee_documents.view'] },
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
        { label: 'Locaties', to: '/locations', permissions: ['locations.view'] },
      ],
    },
    {
      id: 'communicatie',
      label: 'Communicatie',
      icon: MessageSquare,
      items: [
        { label: 'Berichten', to: '/inbox' },
        { label: 'Meldingen', to: '/notifications', badge: 'notifications' },
        { label: 'E-mail / SMS', to: '/messaging', permissions: ['messaging.manage'] },
        { label: 'EDI', to: '/edi', permissions: ['edi.manage'] },
        { label: 'Integraties', to: '/integrations', permissions: ['integrations.manage'] },
      ],
    },
    {
      id: 'beheer',
      label: 'Beheer',
      icon: Settings,
      items: [
        { label: 'Gebruikers', to: '/users', permissions: ['users.view'] },
        { label: 'Rollen & rechten', to: '/roles', permissions: ['roles.view'] },
        { label: 'Functie → rol', to: '/job-function-mappings', permissions: ['roles.view', 'roles.manage_permissions'] },
        { label: 'Instellingen', to: '/settings', end: true, permissions: ['company_settings.view', 'company_settings.manage'] },
        { label: 'Boekhouding', to: '/settings/accounting', permissions: ['accounting.view', 'accounting.manage'] },
      ],
    },
    masterDataModule(),
  ]
}
