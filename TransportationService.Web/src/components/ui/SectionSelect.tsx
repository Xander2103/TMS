import type { SectionNavItem } from './SectionNav'

interface SectionSelectProps {
  items: SectionNavItem[]
  activeId: string
  onActiveChange: (id: string) => void
}

/** Mobile section switcher for {@link SectionedForm}: a native `<select>` mirroring the tablist. */
export function SectionSelect({ items, activeId, onActiveChange }: SectionSelectProps) {
  return (
    <label className="ui-section-select">
      <span className="ui-section-select-label">Sectie</span>
      <select
        aria-label="Sectie"
        value={activeId}
        onChange={(e) => onActiveChange(e.target.value)}
      >
        {items.map((item) => (
          <option key={item.id} value={item.id}>
            {item.label}
            {item.hasError ? ' (!)' : ''}
          </option>
        ))}
      </select>
    </label>
  )
}
