import type { BoardDensity, BoardViewDays } from './boardMath'

export const LAYOUT_STORAGE_KEY = 'ts.planningCenter.layout'

/** B08: the planner's workspace layout — density, view and which side panels are rails. Remembered per browser. */
export interface BoardLayout {
  density: BoardDensity
  leftCollapsed: boolean
  rightCollapsed: boolean
  days: BoardViewDays
}

export const DEFAULT_LAYOUT: BoardLayout = { density: 'normal', leftCollapsed: false, rightCollapsed: false, days: 3 }

/** Reads the stored layout; anything unknown or corrupt falls back to the default, field by field. */
export function parseStoredLayout(raw: string | null | undefined): BoardLayout {
  if (!raw) return DEFAULT_LAYOUT
  try {
    const parsed = JSON.parse(raw) as Partial<BoardLayout>
    return {
      density: parsed.density === 'compact' ? 'compact' : 'normal',
      leftCollapsed: parsed.leftCollapsed === true,
      rightCollapsed: parsed.rightCollapsed === true,
      days: parsed.days === 1 || parsed.days === 3 || parsed.days === 7 ? parsed.days : 3,
    }
  } catch {
    return DEFAULT_LAYOUT
  }
}

export function loadStoredLayout(): BoardLayout {
  try {
    return parseStoredLayout(localStorage.getItem(LAYOUT_STORAGE_KEY))
  } catch {
    return DEFAULT_LAYOUT
  }
}

export function storeLayout(layout: BoardLayout): void {
  try {
    localStorage.setItem(LAYOUT_STORAGE_KEY, JSON.stringify(layout))
  } catch {
    /* best-effort convenience */
  }
}
