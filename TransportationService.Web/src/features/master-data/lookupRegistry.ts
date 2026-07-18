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
  /** Optional hint shown under the code field. */
  codeHint?: string
}

export const LOOKUP_GROUP_LABELS: Record<LookupGroup, string> = {
  organisatie: 'Organisatie',
  categorieen: 'Categorieën',
  referentie: 'Referentiegegevens',
}

export const LOOKUP_RESOURCES: LookupResourceConfig[] = [
  { slug: 'departments', title: 'Afdelingen', singular: 'afdeling', basePath: '/api/departments', group: 'organisatie' },
  { slug: 'job-functions', title: 'Functies', singular: 'functie', basePath: '/api/job-functions', group: 'organisatie' },
  { slug: 'vehicle-categories', title: 'Voertuigcategorieën', singular: 'voertuigcategorie', basePath: '/api/vehicle-categories', group: 'categorieen' },
  { slug: 'trailer-categories', title: 'Opleggercategorieën', singular: 'opleggercategorie', basePath: '/api/trailer-categories', group: 'categorieen' },
  { slug: 'driver-categories', title: 'Chauffeurcategorieën', singular: 'chauffeurcategorie', basePath: '/api/driver-categories', group: 'categorieen' },
  { slug: 'customer-categories', title: 'Klantcategorieën', singular: 'klantcategorie', basePath: '/api/customer-categories', group: 'categorieen' },
  { slug: 'countries', title: 'Landen', singular: 'land', basePath: '/api/countries', group: 'referentie', codeHint: 'ISO 3166-1 alpha-2, bv. BE' },
  { slug: 'languages', title: 'Talen', singular: 'taal', basePath: '/api/languages', group: 'referentie', codeHint: 'ISO 639-1, bv. nl' },
  { slug: 'nationalities', title: 'Nationaliteiten', singular: 'nationaliteit', basePath: '/api/nationalities', group: 'referentie' },
  { slug: 'contract-types', title: 'Contracttypes', singular: 'contracttype', basePath: '/api/contract-types', group: 'referentie' },
]

const RESOURCE_BY_SLUG = new Map(LOOKUP_RESOURCES.map((resource) => [resource.slug, resource]))

export function findLookupResource(slug: string | undefined): LookupResourceConfig | undefined {
  return slug ? RESOURCE_BY_SLUG.get(slug) : undefined
}
