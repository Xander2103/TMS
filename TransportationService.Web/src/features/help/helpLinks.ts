/** Which article the top-bar Help link opens on which screen. The first matching prefix wins. */
const PATH_ARTICLES: [string, string][] = [
  ['/dossiers/', 'dossier-openen'],
  ['/dossiers', 'dossier-zoeken'],
  ['/transport-orders/new', 'opdracht-nieuw'],
  ['/transport-orders', 'opdracht-status'],
  ['/planning-center', 'planbord'],
  ['/planning', 'rit-pagina'],
  ['/operations', 'live-opvolging'],
  ['/incidents', 'incident-opvolgen'],
  ['/exceptions', 'incident-opvolgen'],
  ['/invoice-control', 'facturatiecontrole'],
  ['/invoices/new', 'factuur-maken'],
  ['/invoices/', 'factuur-definitief'],
  ['/invoices', 'factuur-maken'],
  ['/inventory/issues', 'magazijn-uitgeven'],
  ['/inventory', 'magazijn-voorraad'],
  ['/employees/', 'offboarding'],
  ['/employees', 'medewerker'],
  ['/fleet', 'inzetbaarheid'],
  ['/vehicles', 'inzetbaarheid'],
  ['/trailers', 'inzetbaarheid'],
  ['/users', 'gebruikers'],
  ['/roles', 'rollen-beheer'],
  ['/settings/system', 'systeem-gezondheid'],
  ['/peppol', 'peppol-status'],
  ['/dashboard', 'dashboard'],
]

export function helpPathFor(pathname: string): string {
  const match = PATH_ARTICLES.find(([prefix]) => pathname === prefix || pathname.startsWith(prefix.endsWith('/') ? prefix : `${prefix}/`))
  return match ? `/help/${match[1]}` : '/help'
}
