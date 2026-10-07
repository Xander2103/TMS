import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'

/** Option lists of the lookup filters of the dossier list; empty until loaded (the selects stay usable). */
export interface DossierFilterLookups {
  customers: SearchableSelectOption[]
  drivers: SearchableSelectOption[]
  vehicles: SearchableSelectOption[]
  trailers: SearchableSelectOption[]
  /** Activity types whose work runs through an order (stops). */
  transportTypes: SearchableSelectOption[]
  activityTypes: SearchableSelectOption[]
}

export const EMPTY_DOSSIER_FILTER_LOOKUPS: DossierFilterLookups = {
  customers: [],
  drivers: [],
  vehicles: [],
  trailers: [],
  transportTypes: [],
  activityTypes: [],
}
