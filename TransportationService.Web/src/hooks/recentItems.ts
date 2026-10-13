export interface RecentItem {
  category: string
  title: string
  route: string
  visitedAt: number
}

const STORAGE_KEY = 'ts.recent-items'
const MAX_ITEMS = 12

/** Recently visited records (per browser), surfaced in the empty command palette. */
export function listRecentItems(): RecentItem[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as RecentItem[]
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

export function addRecentItem(item: Omit<RecentItem, 'visitedAt'>): void {
  try {
    const items = listRecentItems().filter((existing) => existing.route !== item.route)
    items.unshift({ ...item, visitedAt: Date.now() })
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(items.slice(0, MAX_ITEMS)))
  } catch {
    // Best-effort convenience feature; storage errors are never surfaced.
  }
}
