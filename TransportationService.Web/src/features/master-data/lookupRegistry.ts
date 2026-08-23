export type LookupGroup = 'organisatie' | 'categorieen' | 'referentie'

export interface LookupResourceConfig {
  /** URL segment under /master-data. */
  slug: string
  /** TRANSLATION KEY for the plural page title (navigation.lookups.<slug>) — render via t(). */
  title: string
  /** TRANSLATION KEY for the singular noun (masterData.singular.<slug>) — render via t(). */
  singular: string
  /** Backend base path, e.g. /api/departments. */
  basePath: string
  group: LookupGroup
  /** Permission required to see this lookup (sidebar + page). */
  viewPermission: string
  /** Permission required for create/edit/delete actions. */
  managePermission: string
  /** Optional TRANSLATION KEY for a hint under the code field. */
  codeHint?: string
}

/** Vertaalsleutels — renderen als t(LOOKUP_GROUP_LABELS[group]). */
export const LOOKUP_GROUP_LABELS: Record<LookupGroup, string> = {
  organisatie: 'masterData.groups.organisatie',
  categorieen: 'masterData.groups.categorieen',
  referentie: 'masterData.groups.referentie',
}

export const LOOKUP_RESOURCES: LookupResourceConfig[] = [
  { slug: 'departments', title: 'navigation.lookups.departments', singular: 'masterData.singular.departments', basePath: '/api/departments', group: 'organisatie', viewPermission: 'departments.view', managePermission: 'departments.manage' },
  { slug: 'job-functions', title: 'navigation.lookups.job-functions', singular: 'masterData.singular.job-functions', basePath: '/api/job-functions', group: 'organisatie', viewPermission: 'job_functions.view', managePermission: 'job_functions.manage' },
  { slug: 'vehicle-categories', title: 'navigation.lookups.vehicle-categories', singular: 'masterData.singular.vehicle-categories', basePath: '/api/vehicle-categories', group: 'categorieen', viewPermission: 'vehicle_categories.view', managePermission: 'vehicle_categories.manage' },
  { slug: 'trailer-categories', title: 'navigation.lookups.trailer-categories', singular: 'masterData.singular.trailer-categories', basePath: '/api/trailer-categories', group: 'categorieen', viewPermission: 'trailer_categories.view', managePermission: 'trailer_categories.manage' },
  { slug: 'driver-categories', title: 'navigation.lookups.driver-categories', singular: 'masterData.singular.driver-categories', basePath: '/api/driver-categories', group: 'categorieen', viewPermission: 'driver_categories.view', managePermission: 'driver_categories.manage' },
  { slug: 'customer-categories', title: 'navigation.lookups.customer-categories', singular: 'masterData.singular.customer-categories', basePath: '/api/customer-categories', group: 'categorieen', viewPermission: 'customer_categories.view', managePermission: 'customer_categories.manage' },
  { slug: 'issued-item-categories', title: 'navigation.lookups.issued-item-categories', singular: 'masterData.singular.issued-item-categories', basePath: '/api/issued-item-categories', group: 'categorieen', viewPermission: 'issued_items.view', managePermission: 'inventory.manage' },
  { slug: 'task-categories', title: 'navigation.lookups.task-categories', singular: 'masterData.singular.task-categories', basePath: '/api/task-categories', group: 'categorieen', viewPermission: 'tasks.view_own', managePermission: 'tasks.manage_categories' },
  // Countries are deliberately absent: they are global ISO reference data (seeded, read-only),
  // not tenant master data. Country selection happens through the CountryCombobox everywhere.
  { slug: 'languages', title: 'navigation.lookups.languages', singular: 'masterData.singular.languages', basePath: '/api/languages', group: 'referentie', viewPermission: 'reference_data.view', managePermission: 'reference_data.manage', codeHint: 'masterData.hints.languages' },
  { slug: 'nationalities', title: 'navigation.lookups.nationalities', singular: 'masterData.singular.nationalities', basePath: '/api/nationalities', group: 'referentie', viewPermission: 'reference_data.view', managePermission: 'reference_data.manage' },
  { slug: 'contract-types', title: 'navigation.lookups.contract-types', singular: 'masterData.singular.contract-types', basePath: '/api/contract-types', group: 'referentie', viewPermission: 'reference_data.view', managePermission: 'reference_data.manage' },
]

const RESOURCE_BY_SLUG = new Map(LOOKUP_RESOURCES.map((resource) => [resource.slug, resource]))

export function findLookupResource(slug: string | undefined): LookupResourceConfig | undefined {
  return slug ? RESOURCE_BY_SLUG.get(slug) : undefined
}
