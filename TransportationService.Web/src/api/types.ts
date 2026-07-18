/** Mirror of the backend `PagedResult<T>` envelope. */
export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}
