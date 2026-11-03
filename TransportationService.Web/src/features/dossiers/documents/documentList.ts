import type { TranslateFn } from '../../../i18n/localeContext'
import { ORDER_DOCUMENT_TYPE_LABELS } from '../../transport-orders/api/orderDocumentsApi'
import type { DossierDocument } from '../api/dossierDocumentsApi'

export type DocumentPortalFilter = 'all' | 'visible' | 'internal'
export type DocumentSort = 'newest' | 'oldest' | 'title' | 'type'

export const DOCUMENT_SORTS: DocumentSort[] = ['newest', 'oldest', 'title', 'type']
export const DOCUMENT_PAGE_SIZES = [20, 50, 100]
export const DEFAULT_DOCUMENT_PAGE_SIZE = DOCUMENT_PAGE_SIZES[0]

export interface DocumentListFilters {
  search: string
  /** A type label from `documentTypeLabel`; '' = every type. */
  type: string
  portal: DocumentPortalFilter
  sort: DocumentSort
}

/** The custom name of an "Other" document, else the translated type. */
export function documentTypeLabel(document: DossierDocument, t: TranslateFn): string {
  return document.documentType === 'Other' && document.customTypeName
    ? document.customTypeName
    : t(ORDER_DOCUMENT_TYPE_LABELS[document.documentType] ?? 'transportOrders.documentType.Other')
}

/** "PDF" from the stored file name; null without a file or extension. */
export function documentFileExtension(document: DossierDocument): string | null {
  const match = document.hasFile ? document.fileName?.match(/\.([a-z0-9]+)$/i) : null
  return match ? match[1].toUpperCase() : null
}

/** Client-side on purpose: the dossier endpoint returns the whole list, every level derives from it. */
export function filterAndSortDocuments(documents: DossierDocument[], filters: DocumentListFilters, t: TranslateFn, locale: string): DossierDocument[] {
  const search = filters.search.trim().toLocaleLowerCase(locale)
  const filtered = documents.filter((document) => {
    const type = documentTypeLabel(document, t)
    if (filters.type && type !== filters.type) return false
    if (filters.portal === 'visible' && !document.customerVisible) return false
    if (filters.portal === 'internal' && document.customerVisible) return false
    if (!search) return true
    return [document.title, document.fileName, document.createdByName, type].some((value) => value?.toLocaleLowerCase(locale).includes(search))
  })
  const byDate = (a: DossierDocument, b: DossierDocument) => a.createdAt.localeCompare(b.createdAt)
  const byTitle = (a: DossierDocument, b: DossierDocument) => a.title.localeCompare(b.title, locale)
  return filtered.sort((a, b) => {
    switch (filters.sort) {
      case 'oldest':
        return byDate(a, b)
      case 'title':
        return byTitle(a, b)
      case 'type':
        return documentTypeLabel(a, t).localeCompare(documentTypeLabel(b, t), locale) || byTitle(a, b)
      default:
        return byDate(b, a)
    }
  })
}
