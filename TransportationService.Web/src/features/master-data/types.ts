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
}

/** Payload for creating/updating a lookup. */
export interface LookupInput {
  code: string
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
}
