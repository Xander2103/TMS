export type LookupGroup = 'organisatie' | 'categorieen' | 'referentie'

export interface LookupResourceConfig {
  /** URL segment under /master-data. */
  slug: string
  /** Plural page title. */
  title: string
  /** Singular noun used in buttons/dialogs ("Nieuwe {singular}"). */
  singular: string
  /** Backend base path, e.g. /api/departments. */
  basePath: string
  group: LookupGroup
  /** Permission required to see this lookup (sidebar + page). */
  viewPermission: string
  /** Permission required for create/edit/delete actions. */
  managePermission: string
  /** Optional hint shown under the code field. */
  codeHint?: string
}

export const LOOKUP_GROUP_LABELS: Record<LookupGroup, string> = {
  organisatie: 'Organisatie',
  categorieen: 'Categorieën',
  referentie: 'Referentiegegevens',
}

export const LOOKUP_RESOURCES: LookupResourceConfig[] = [
  { slug: 'departments', title: 'Afdelingen', singular: 'afdeling', basePath: '/api/departments', group: 'organisatie', viewPermission: 'departments.view', managePermission: 'departments.manage' },
  { slug: 'job-functions', title: 'Functies', singular: 'functie', basePath: '/api/job-functions', group: 'organisatie', viewPermission: 'job_functions.view', managePermission: 'job_functions.manage' },
  { slug: 'vehicle-categories', title: 'Voertuigcategorieën', singular: 'voertuigcategorie', basePath: '/api/vehicle-categories', group: 'categorieen', viewPermission: 'vehicle_categories.view', managePermission: 'vehicle_categories.manage' },
  { slug: 'trailer-categories', title: 'Opleggercategorieën', singular: 'opleggercategorie', basePath: '/api/trailer-categories', group: 'categorieen', viewPermission: 'trailer_categories.view', managePermission: 'trailer_categories.manage' },
  { slug: 'driver-categories', title: 'Chauffeurcategorieën', singular: 'chauffeurcategorie', basePath: '/api/driver-categories', group: 'categorieen', viewPermission: 'driver_categories.view', managePermission: 'driver_categories.manage' },
  { slug: 'customer-categories', title: 'Klantcategorieën', singular: 'klantcategorie', basePath: '/api/customer-categories', group: 'categorieen', viewPermission: 'customer_categories.view', managePermission: 'customer_categories.manage' },
  { slug: 'issued-item-categories', title: 'Bedrijfsmiddelcategorieën', singular: 'bedrijfsmiddelcategorie', basePath: '/api/issued-item-categories', group: 'categorieen', viewPermission: 'issued_items.view', managePermission: 'inventory.manage' },
  { slug: 'task-categories', title: 'Taakcategorieën', singular: 'taakcategorie', basePath: '/api/task-categories', group: 'categorieen', viewPermission: 'tasks.view_own', managePermission: 'tasks.manage_categories' },
  // Countries are deliberately absent: they are global ISO reference data (seeded, read-only),
  // not tenant master data. Country selection happens through the CountryCombobox everywhere.
  { slug: 'languages', title: 'Talen', singular: 'taal', basePath: '/api/languages', group: 'referentie', viewPermission: 'reference_data.view', managePermission: 'reference_data.manage', codeHint: 'ISO 639-1, bv. nl' },
  { slug: 'nationalities', title: 'Nationaliteiten', singular: 'nationaliteit', basePath: '/api/nationalities', group: 'referentie', viewPermission: 'reference_data.view', managePermission: 'reference_data.manage' },
  { slug: 'contract-types', title: 'Contracttypes', singular: 'contracttype', basePath: '/api/contract-types', group: 'referentie', viewPermission: 'reference_data.view', managePermission: 'reference_data.manage' },
]

const RESOURCE_BY_SLUG = new Map(LOOKUP_RESOURCES.map((resource) => [resource.slug, resource]))

export function findLookupResource(slug: string | undefined): LookupResourceConfig | undefined {
  return slug ? RESOURCE_BY_SLUG.get(slug) : undefined
}
