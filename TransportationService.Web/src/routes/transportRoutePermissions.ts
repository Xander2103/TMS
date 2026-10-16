import { navPermissions } from './routePermissions'

/**
 * M14: the any-of permissions guarding the transport routes. List and detail routes reuse the
 * sidebar entry's rights (navPermissions — single source of truth), so a page opens for exactly
 * the roles that see it in the menu: Dispatcher and Boekhouding still open dossiers and orders
 * read-only, a Chauffeur keeps /my-trips and /driver. Create screens require the right their POST
 * endpoint requires, so a read-only role never fills in a whole wizard for a 403.
 * Each entry mirrors the backend controller's [RequirePermission] on the page's main load/submit.
 */
export function transportRoutePermissions(): Record<string, string[]> {
  return {
    '/dossiers': navPermissions('/dossiers'), //                 GET api/dossiers: dossiers.view|manage
    '/dossiers/:id': navPermissions('/dossiers'),
    '/dossiers/new': ['dossiers.manage'], //                     POST api/dossiers
    '/transport-orders': navPermissions('/transport-orders'), // GET api/transport-orders: orders.view|manage
    '/transport-orders/:id': navPermissions('/transport-orders'),
    '/transport-orders/new': ['orders.create', 'orders.manage'], // POST api/transport-orders
    '/planning': navPermissions('/planning'), //                 GET api/trips: planning.view
    '/planning/:id': navPermissions('/planning'),
    '/incidents': navPermissions('/incidents'), //               GET api/incidents: incidents.view|manage
    '/incidents/:id': navPermissions('/incidents'),
    '/incidents/new': ['incidents.manage'], //                   POST api/incidents
    '/exceptions': navPermissions('/exceptions'), //             GET api/exceptions: exceptions.view
    '/exceptions/:id': navPermissions('/exceptions'),
    '/invoices': navPermissions('/invoices'), //                 GET api/invoices: invoices.view
    '/invoices/:id': navPermissions('/invoices'),
    '/invoices/new': ['invoices.create'], //                     POST api/invoices
    '/invoice-control': navPermissions('/invoice-control'), //   invoices.view
    '/customers': navPermissions('/customers'), //               GET api/customers: customers.view
    '/customers/:id': navPermissions('/customers'),
    '/customers/new': ['customers.create'], //                   POST api/customers
  }
}
