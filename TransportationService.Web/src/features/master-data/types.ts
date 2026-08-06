/** A lookup row as returned by the backend `LookupItemDto`. */
export interface LookupItem {
  id: string
  code: string
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
  createdAt: string
  updatedAt: string
}

/** Compact lookup shape for dropdowns (`LookupOptionDto`). */
export interface LookupOption {
  id: string
  code: string
  name: string
  /** Non-null only for contract-type lookups (HR maturity wave, task 5). */
  requiresEndDate?: boolean | null
}

/** Payload for creating/updating a lookup. */
export interface LookupInput {
  code: string
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
}
