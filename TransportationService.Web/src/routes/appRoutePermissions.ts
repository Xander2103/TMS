import { navPermissions } from './routePermissions'

/** Any-of union without duplicates, in first-seen order. */
function anyOf(...lists: string[][]): string[] {
  return [...new Set(lists.flat())]
}

/**
 * SEC-M5: the any-of permissions guarding the remaining internal routes (operations, warehouse,
 * reporting, settings, master data, driver pages). A direct URL for a page the role may not open
 * answers "Geen toegang" instead of endless "Laden…", repeated error toasts and "Opnieuw proberen".
 *
 * Routes with a sidebar entry reuse its rights (navPermissions — single source of truth), so
 * everyone who sees the menu entry opens the page. Where the page's main load also admits other
 * rights (a read-only mode, a second tab), those are added so no role the API serves is locked
 * out. Routes without a sidebar entry mirror the backend [RequirePermission] of their main load.
 * Pages that check per tab themselves (order-imports, edi, issued-items) keep their own logic.
 */
export function appRoutePermissions(): Record<string, string[]> {
  return {
    // Vandaag / planning / rapportage
    '/dashboard': navPermissions('/dashboard'), //                     GET api/dashboard: dashboard.view
    '/operations': navPermissions('/operations'), //                   GET api/operations/overview|alerts: operations.view
    '/kpi': navPermissions('/kpi'), //                                 GET api/kpi/dashboard: kpi.view
    '/kpi/trips': ['profitability.view'], //                           GET api/kpi/trip-profitability
    '/profitability': navPermissions('/profitability'), //             GET api/profitability/*: profitability.view
    '/reports': navPermissions('/reports'), //                         GET api/reports/catalog: reports.view
    '/reports/:reportId': navPermissions('/reports'),
    // Magazijn
    '/warehouse': navPermissions('/warehouse'), //                     GET api/warehouse/trips|packages: warehouse.view
    '/warehouse/trace': navPermissions('/warehouse/trace'), //         GET api/warehouse/trace: warehouse.view|manage
    '/warehouses': navPermissions('/warehouses'), //                   GET api/warehouses: warehouse.view|manage
    '/dock-planning': navPermissions('/dock-planning'), //             GET api/dock-appointments/board: warehouse.view|schedule
    '/packages/:id': ['packages.view', 'packages.manage'], //          GET api/packages/{id}
    '/pods/:id': ['pod.view'], //                                      GET api/pods/{id}
    // Chauffeur: every driver screen loads from api/my/* or api/trips/{id}/execution.
    '/driver': ['driver_workflow.view'], //                            GET api/my/dashboard|documents|incidents
    '/my-trips': ['driver_workflow.view'], //                          GET api/my/trips
    '/my-trips/:id': ['driver_workflow.view'], //                      GET api/trips/{id}/execution
    // Klanten / personeel
    '/locations': navPermissions('/locations'), //                     GET api/locations: locations.view
    '/locations/:id': navPermissions('/locations'),
    '/locations/new': ['locations.create'], //                         POST api/locations
    '/qualifications': navPermissions('/qualifications'), //           GET api/qualifications/expired: employee_documents.view
    '/tasks': navPermissions('/tasks'), //                             GET api/tasks: tasks.view_own|view_team|view_all
    // Parameters
    '/settings': navPermissions('/settings'), //                       GET api/company-settings: company_settings.view
    '/settings/pricing': anyOf(navPermissions('/settings/pricing'), ['tariffs.view']), // read-only for tariffs.view
    '/pricing/tables': navPermissions('/pricing/tables'), //           GET api/pricing/agreements: tariffs.view|manage
    '/pricing/tables/:id': navPermissions('/pricing/tables'),
    '/cost-rates': navPermissions('/cost-rates'), //                   GET api/cost-rates: trip_costs.view|manage
    '/integrations': navPermissions('/integrations'), //               GET api/integrations/calendar-sync: integrations.manage
    '/job-function-mappings': navPermissions('/job-function-mappings'), // GET api/job-function-role-mappings: roles.view|manage_permissions
    '/settings/document-rules': navPermissions('/settings/document-rules'), // company_settings.view|manage
    '/settings/charge-policies': navPermissions('/settings/charge-policies'), // problems.approve_charge|incidents.manage
    '/settings/system': anyOf(navPermissions('/settings/system'), ['backups.view']), // tabs: system_info.view · backups.view
    '/settings/hr-reminders': anyOf(navPermissions('/settings/hr-reminders'), ['employees.view']), // GET: employees.view (read-only)
    '/settings/leave': anyOf(navPermissions('/settings/leave'), ['employees.view']), // GET api/leave-types: employees.view (read-only)
    '/settings/attendance': navPermissions('/settings/attendance'), // tabs: attendance.manage_settings · manage_kiosks
    '/settings/task-templates': anyOf(navPermissions('/settings/task-templates'), ['tasks.assign']), // GET api/task-templates: manage_templates|assign
    '/settings/activity-types': anyOf(navPermissions('/settings/activity-types'), ['dossiers.view']), // GET api/activity-types: activity_types.view|dossiers.view
    '/settings/notifications': navPermissions('/settings/notifications'), // GET api/notification-rules: notification_rules.view
    '/settings/escalations': navPermissions('/settings/escalations'), // escalations.manage
    '/settings/portal-announcements': navPermissions('/settings/portal-announcements'), // portal_announcements.manage
    '/settings/portal-messages': navPermissions('/settings/portal-messages'), // GET api/portal-messages: portal_messages.view|send
    '/master-data/eenheden': navPermissions('/master-data/eenheden'), // GET api/unit-types/settings: unit_types.*|tariffs.*
    '/master-data/services': navPermissions('/master-data/services'), // GET api/service-options: tariffs.view|manage
    // '/master-data/:resource' is guarded per lookup inside LookupPage (registry view|manage right).
  }
}
