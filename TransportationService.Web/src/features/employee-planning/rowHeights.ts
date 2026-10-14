import type { ListDensity } from '../../components/ui/listDensity'
import type { ScheduleRow } from './types'

const NORMAL = { base: 50, chip: 26, addButton: 22 }
/** Compact: the two-line employee cell sets the floor; one-line chips (≈18px + gap) stack above it. */
const COMPACT = { employeeCell: 40, cellPadding: 8, chip: 20 }

/**
 * B49: height guess for a planning grid row before it is measured, per density. Compact keeps
 * every chip (one line: state + time) and drops the reserved "+ shift" line — the add button
 * floats in the cell corner instead — so a typical row roughly halves.
 */
export function estimatePlanningRowHeight(row: ScheduleRow | undefined, density: ListDensity, canManage: boolean): number {
  const maxEntries = row ? Math.max(1, ...row.days.map((d) => d.entries.length)) : 1
  if (density === 'compact') return Math.max(COMPACT.employeeCell, COMPACT.cellPadding + maxEntries * COMPACT.chip)
  return NORMAL.base + Math.max(0, maxEntries - 1) * NORMAL.chip + (canManage ? NORMAL.addButton : 0)
}
