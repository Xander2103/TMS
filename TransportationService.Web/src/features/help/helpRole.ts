import type { ManualArticle } from './manualParser'

export type HelpRole = 'admin' | 'management' | 'planner' | 'dispatcher' | 'hr' | 'fleet' | 'accounting' | 'warehouse' | 'driver' | 'customer'

/**
 * Which help comes first for this user. Presentation only: it follows the same order as the role
 * landings (routes/portalRouting.tsx) and decides nothing about access. The API stays the enforcement.
 */
export function helpRoleOf(hasPermission: (code: string) => boolean, isCustomer: boolean): HelpRole | null {
  if (isCustomer) return 'customer'
  const has = (...codes: string[]) => codes.every(hasPermission)
  if (has('roles.manage_permissions')) return 'admin'
  if (has('planning.edit', 'orders.create')) return 'planner'
  if (has('planning.edit', 'operations.view')) return 'dispatcher'
  if (has('invoices.change_status')) return 'accounting'
  if (has('vehicles.edit')) return 'fleet'
  if (has('employees.edit')) return 'hr'
  if (has('inventory.issue')) return 'warehouse'
  if (has('operations.view', 'dashboard.view')) return 'management'
  if (has('driver_workflow.execute')) return 'driver'
  return null
}

export const PRIMARY_CATEGORIES: Record<HelpRole, string[]> = {
  admin: ['Beheer', 'Rollen & rechten', 'Dossiers'],
  management: ['Aan de slag', 'Live opvolging', 'Boekhouding'],
  planner: ['Planning', 'Opdrachten', 'Dossiers'],
  dispatcher: ['Live opvolging', 'Incidenten & Afwijkingen', 'Planning'],
  hr: ['Personeel', 'Magazijn'],
  fleet: ['Wagenpark', 'Planning'],
  accounting: ['Boekhouding', 'Documenten'],
  warehouse: ['Magazijn'],
  driver: ['Chauffeur'],
  customer: ['Klantportaal'],
}

/** A customer only gets customer help; only those it is written for see the operator manual. */
export function isVisibleTo(article: ManualArticle, role: HelpRole | null): boolean {
  if (article.roles.includes('all')) return true
  if (role === 'customer') return article.roles.includes('customer')
  if (article.quickstart) return role === 'admin' || (role !== null && article.roles.includes(role))
  if (article.category === 'Beheer') return role !== null && article.roles.includes(role)
  if (article.roles.length === 1 && article.roles[0] === 'customer') return false
  return true
}

/** Primary categories first, then the rest in the manual's order. */
export function orderCategories(categories: string[], role: HelpRole | null): string[] {
  const primary = role ? PRIMARY_CATEGORIES[role] : []
  return [...primary.filter((c) => categories.includes(c)), ...categories.filter((c) => !primary.includes(c))]
}

/** Primary articles for the "Voor jou" block: written for this role and in one of its first topics. */
export function primaryArticles(articles: ManualArticle[], role: HelpRole | null): ManualArticle[] {
  if (!role) return []
  const categories = PRIMARY_CATEGORIES[role]
  return articles
    .filter((a) => !a.quickstart && categories.includes(a.category) && (a.roles.includes(role) || a.roles.includes('all')))
    .sort((a, b) => categories.indexOf(a.category) - categories.indexOf(b.category))
    .slice(0, 8)
}
