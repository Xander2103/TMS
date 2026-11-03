import { useState } from 'react'

export const DEFAULT_PAGE_SIZE = 20
export const PAGE_SIZE_OPTIONS = [20, 50, 100]

export type HistorySort = 'newest' | 'oldest'

/** Case-insensitive "contains" over the parts of a row; an empty query matches everything. */
export function matchesSearch(query: string, parts: (string | null | undefined)[]): boolean {
  const needle = query.trim().toLocaleLowerCase()
  if (!needle) return true
  return parts.some((part) => part?.toLocaleLowerCase().includes(needle))
}

/**
 * Chronological order of a list. Newest first is the exact reverse of oldest first, so events with
 * the same timestamp keep their reconstructed order in both directions.
 */
export function sortByMoment<T>(items: T[], moment: (item: T) => string, sort: HistorySort): T[] {
  const oldestFirst = items
    .map((item, index) => ({ item, index, time: Date.parse(moment(item)) }))
    .sort((a, b) => a.time - b.time || a.index - b.index)
    .map(({ item }) => item)
  return sort === 'oldest' ? oldestFirst : oldestFirst.reverse()
}

/** Numbered pager, with gaps once there are too many pages to show them all. */
export function pageNumbers(page: number, totalPages: number): (number | 'gap')[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, index) => index + 1)
  const wanted = [...new Set([1, page - 1, page, page + 1, totalPages])]
    .filter((candidate) => candidate >= 1 && candidate <= totalPages)
    .sort((a, b) => a - b)
  return wanted.flatMap((current, index) => (index > 0 && current - wanted[index - 1] > 1 ? ['gap' as const, current] : [current]))
}

export interface ListPaging {
  page: number
  pageSize: number
  totalPages: number
  start: number
  end: number
  setPage: (page: number) => void
  setPageSize: (size: number) => void
}

/** Client-side paging. The page is clamped, so deleting the last row of a page lands on the previous one. */
export function useListPaging(totalCount: number): ListPaging {
  const [requestedPage, setPage] = useState(1)
  const [pageSize, setSize] = useState(DEFAULT_PAGE_SIZE)
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const page = Math.min(requestedPage, totalPages)
  return {
    page,
    pageSize,
    totalPages,
    start: totalCount === 0 ? 0 : (page - 1) * pageSize + 1,
    end: Math.min(page * pageSize, totalCount),
    setPage,
    setPageSize: (size) => {
      setSize(size)
      setPage(1)
    },
  }
}
