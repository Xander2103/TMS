/**
 * FL-M10: the any-of permissions guarding the fleet routes. Each entry mirrors the backend
 * controller's [RequirePermission] on the page's main load/submit, so a role without fleet rights
 * gets the "Geen toegang" screen instead of a page shell that fails to load.
 */
export function fleetRoutePermissions(): Record<string, string[]> {
  return {
    '/fleet': ['vehicles.view'], //                                   GET api/fleet/dashboard
    '/vehicles': ['vehicles.view'], //                                GET api/vehicles
    '/vehicles/:id': ['vehicles.view'], //                            GET api/vehicles/{id}
    '/vehicles/new': ['vehicles.create'], //                          POST api/vehicles
    '/trailers': ['trailers.view'], //                                GET api/trailers
    '/trailers/:id': ['trailers.view'],
    '/trailers/new': ['trailers.create'], //                          POST api/trailers
    '/maintenance-policies': ['maintenance_policies.view', 'maintenance_policies.manage'],
    '/tank-cards': ['tank_cards.view'], //                            GET api/tank-cards
  }
}
