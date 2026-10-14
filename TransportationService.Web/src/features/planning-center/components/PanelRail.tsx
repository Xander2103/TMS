interface PanelRailProps {
  side: 'left' | 'right'
  /** Vertical caption of the collapsed panel ("Ongepland", "Middelen"). */
  label: string
  /** Optional count shown under the caption (unplanned orders). */
  count?: number
  expandLabel: string
  onExpand: () => void
}

/**
 * B08: a collapsed side panel becomes a narrow rail with its name, its count and one button to
 * bring it back — the workspace goes to the timeline without the panel disappearing.
 */
export function PanelRail({ side, label, count, expandLabel, onExpand }: PanelRailProps) {
  return (
    <aside className="pc-rail" data-side={side} aria-label={label}>
      <button type="button" className="pc-rail-toggle" onClick={onExpand} aria-label={expandLabel} title={expandLabel}>
        {side === 'left' ? '»' : '«'}
      </button>
      <span className="pc-rail-label">{label}</span>
      {count !== undefined && <span className="pc-rail-count">{count}</span>}
    </aside>
  )
}
