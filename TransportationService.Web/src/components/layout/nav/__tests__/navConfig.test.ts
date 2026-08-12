import { describe, expect, it } from 'vitest'
import { getNavModules, type NavItem } from '../navConfig'
import { filterModule, type VisibleModule } from '../navState'

/**
 * Fixture: permission codes of the seeded "dispatcher" role template, copied from
 * TransportationService.Api/Data/DefaultRoleDefinitions.cs (CommonViewPermissions +
 * dispatcher-specific codes). Kept as literals on purpose: the test proves the §14
 * navigation claim against the REAL template, so a template change must surface here.
 */
const DISPATCHER_PERMISSIONS = [
  // CommonViewPermissions
  'dashboard.view', 'customers.view', 'locations.view', 'drivers.view', 'vehicles.view',
  'trailers.view', 'employees.view', 'absences.view', 'departments.view', 'job_functions.view',
  'vehicle_categories.view', 'trailer_categories.view', 'driver_categories.view',
  'customer_categories.view', 'contact_departments.view', 'reference_data.view',
  'leave_balances.view_own',
  // dispatcher template
  'orders.view', 'orders.change_status', 'orders.assign',
  'planning.view', 'planning.create', 'planning.edit',
  'driver_workflow.view',
  'scanning.view', 'scanning.correct',
  'exceptions.view', 'exceptions.create', 'exceptions.resolve',
  'pod.view', 'pod.correct',
  'employee_planning.view',
  'employee_documents.view',
  'packages.view',
  'package_exceptions.create', 'package_exceptions.manage',
  'scanning.override',
  'warehouse.view', 'warehouse.release_trip',
  'dossiers.view',
  'activity_types.view',
  'incidents.view', 'incidents.manage',
  'reports.view',
  'operations.view', 'operations.manage_alerts',
  'warehouse.schedule',
  'legal_entities.view',
  'customer_messages.view', 'customer_messages.send',
]

/** Fixture: the seeded "magazijn" role template (DefaultRoleDefinitions.cs). */
const MAGAZIJN_PERMISSIONS = [
  'warehouse.view', 'warehouse.release_trip',
  'packages.view',
  'scanning.view', 'scanning.execute',
  'package_exceptions.create',
  'exceptions.view', 'exceptions.create',
  'planning.view',
  'operations.view',
  'warehouse.manage', 'warehouse.schedule', 'warehouse.conflict_override',
  'issued_items.view',
  'inventory.view', 'inventory.adjust',
]

function hasAnyOf(granted: string[]): (codes: string[]) => boolean {
  return (codes) => codes.some((c) => granted.includes(c))
}

/** Sidebar as a given permission set sees it (internal user without employee link). */
function visibleFor(granted: string[]): VisibleModule[] {
  return getNavModules()
    .map((m) => filterModule(m, { hasAnyPermission: hasAnyOf(granted), hasEmployee: false, query: '' }))
    .filter((vm): vm is VisibleModule => vm !== null)
}

function leavesOf(vm: VisibleModule): NavItem[] {
  return [...vm.items, ...vm.subgroups.flatMap((sg) => sg.items)]
}

describe('getNavModules — §14 target tree', () => {
  const modules = getNavModules()

  it('returns the target groups in order: operational groups first, Parameters last', () => {
    expect(modules.map((m) => m.id)).toEqual([
      'portaal', 'vandaag', 'dossiers', 'planning', 'magazijn', 'klanten',
      'personeel', 'vloot', 'rapportage', 'parameters',
    ])
  })

  it('flags the portal module as employee-only', () => {
    expect(modules.find((m) => m.id === 'portaal')?.requiresEmployee).toBe(true)
  })

  it('Vandaag bundles dashboard, communication and the Problemen pair', () => {
    const vandaag = modules.find((m) => m.id === 'vandaag')!
    expect(vandaag.items!.map((i) => i.to)).toEqual(['/dashboard', '/inbox', '/notifications'])
    expect(vandaag.items!.find((i) => i.to === '/notifications')?.badge).toBe('notifications')
    const problemen = vandaag.subgroups!.find((s) => s.label === 'Problemen')!
    expect(problemen.items.map((i) => i.to)).toEqual(['/incidents', '/exceptions'])
  })

  it('puts Dossiers first and demotes the order list to "Opdrachten (klassiek)"', () => {
    const dossiers = modules.find((m) => m.id === 'dossiers')!
    expect(dossiers.items!.map((i) => [i.to, i.label])).toEqual([
      ['/dossiers', 'Dossiers'],
      ['/transport-orders', 'Opdrachten (klassiek)'],
    ])
  })

  it('renames the planning surfaces: Planbord primary, Ritlijst and Live opvolging secondary', () => {
    const planning = modules.find((m) => m.id === 'planning')!
    const byRoute = new Map(planning.items!.map((i) => [i.to, i.label]))
    expect(planning.items![0].to).toBe('/planning-center')
    expect(byRoute.get('/planning-center')).toBe('Planbord')
    expect(byRoute.get('/planning')).toBe('Ritlijst')
    expect(byRoute.get('/operations')).toBe('Live opvolging')
  })

  it('renames the warehouse entries and keeps Dockplanning under Magazijn', () => {
    const magazijn = modules.find((m) => m.id === 'magazijn')!
    const byRoute = new Map(magazijn.items!.map((i) => [i.to, i.label]))
    expect(byRoute.get('/warehouse')).toBe('Laden & scannen')
    expect(byRoute.get('/warehouses')).toBe('Magazijnen (beheer)')
    expect(byRoute.get('/dock-planning')).toBe('Dockplanning')
  })

  it('moves Locaties and Facturatie (Facturen + Facturatiecontrole + Peppol) under Klanten', () => {
    const klanten = modules.find((m) => m.id === 'klanten')!
    expect(klanten.items!.map((i) => i.to)).toEqual(['/customers', '/locations'])
    const facturatie = klanten.subgroups!.find((s) => s.label === 'Facturatie')!
    expect(facturatie.items.map((i) => i.to)).toEqual(['/invoices', '/invoice-control', '/peppol'])
  })

  it('hides the driver surfaces from the internal sidebar (routes stay reachable)', () => {
    const allRoutes = modules.flatMap((m) => [
      ...(m.items ?? []).map((i) => i.to),
      ...(m.subgroups ?? []).flatMap((sg) => sg.items.map((i) => i.to)),
    ])
    expect(allRoutes).not.toContain('/my-trips')
    expect(allRoutes).not.toContain('/driver')
    expect(allRoutes).not.toContain('/messaging')
  })

  it('collects every configuration entry under Parameters with the five subgroups', () => {
    const parameters = modules.find((m) => m.id === 'parameters')!
    expect(parameters.items!.map((i) => i.to)).toEqual(['/settings'])
    expect(parameters.subgroups!.map((s) => s.label)).toEqual([
      'Prijzen', 'Koppelingen & meldingen', 'Beheer', 'Personeel', 'Stamgegevens',
    ])
    const prijzen = parameters.subgroups!.find((s) => s.label === 'Prijzen')!
    expect(prijzen.items.map((i) => i.to)).toEqual(['/pricing/tables', '/settings/pricing', '/cost-rates'])
    const stamgegevens = parameters.subgroups!.find((s) => s.label === 'Stamgegevens')!
    const stamRoutes = stamgegevens.items.map((i) => i.to)
    expect(stamRoutes).toContain('/settings/activity-types')
    expect(stamRoutes).toContain('/master-data/eenheden')
    expect(stamRoutes).toContain('/master-data/services')
    expect(stamRoutes).toContain('/master-data/customer-categories')
    expect(stamRoutes).toContain('/master-data/languages')
  })

  it('keeps HR configuration under Parameters → Personeel, not in the operational Personeel group', () => {
    const personeel = modules.find((m) => m.id === 'personeel')!
    expect(personeel.items!.map((i) => i.to)).not.toContain('/settings/hr-reminders')
    const parameters = modules.find((m) => m.id === 'parameters')!
    const hrConfig = parameters.subgroups!.find((s) => s.label === 'Personeel')!
    expect(hrConfig.items.map((i) => i.to)).toEqual([
      '/settings/leave', '/settings/hr-reminders', '/settings/issued-item-templates', '/settings/task-templates',
    ])
  })
})

describe('getNavModules — role-scoped sidebars (§14)', () => {
  it('dispatcher: compact operational tree, Dossiers before Opdrachten', () => {
    const visible = visibleFor(DISPATCHER_PERMISSIONS)

    // The §14 claim covers the operational core groups (Vandaag t/m Personeel); Vloot,
    // Rapportage and Parameters stay available but collapsed for the roles that may see them.
    const coreIds = ['vandaag', 'dossiers', 'planning', 'magazijn', 'klanten', 'personeel']
    const coreLeaves = visible
      .filter((vm) => coreIds.includes(vm.module.id))
      .flatMap(leavesOf)
    expect(coreLeaves.length).toBeLessThanOrEqual(20)

    const dossiersModule = visible.find((vm) => vm.module.id === 'dossiers')!
    const labels = leavesOf(dossiersModule).map((i) => i.label)
    expect(labels.indexOf('Dossiers')).toBeGreaterThanOrEqual(0)
    expect(labels.indexOf('Dossiers')).toBeLessThan(labels.indexOf('Opdrachten (klassiek)'))

    // Financial/config entries the dispatcher must NOT see.
    const allRoutes = visible.flatMap(leavesOf).map((i) => i.to)
    expect(allRoutes).not.toContain('/invoices')
    expect(allRoutes).not.toContain('/peppol')
    expect(allRoutes).not.toContain('/kpi')
    expect(allRoutes).not.toContain('/settings')
  })

  it('magazijn: sees warehouse work but no Klanten group and no configuration surface', () => {
    const visible = visibleFor(MAGAZIJN_PERMISSIONS)
    const ids = visible.map((vm) => vm.module.id)
    expect(ids).toContain('magazijn')
    expect(ids).toContain('planning')
    expect(ids).not.toContain('klanten')

    // Parameters shrinks to the single Stamgegevens lookup the template could already
    // open before the redesign (Bedrijfsmiddelcategorieën via issued_items.view —
    // permission arrays are deliberately untouched). No settings/pricing/admin leak.
    const parameters = visible.find((vm) => vm.module.id === 'parameters')
    const parameterLeaves = parameters ? leavesOf(parameters).map((i) => i.label) : []
    expect(parameterLeaves).toEqual(['Bedrijfsmiddelcategorieën'])

    const magazijn = visible.find((vm) => vm.module.id === 'magazijn')!
    expect(leavesOf(magazijn).map((i) => i.label)).toEqual([
      'Laden & scannen', 'Trace & voorraad', 'Magazijnen (beheer)', 'Dockplanning',
    ])
  })
})
