import { Search } from 'lucide-react'

export function NavFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <div className="nav-filter">
      <Search className="nav-filter-icon" size={16} aria-hidden />
      <input
        type="search"
        className="nav-filter-input"
        placeholder="Filter menu…"
        aria-label="Filter menu"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <kbd className="nav-filter-kbd" aria-hidden>⌘K</kbd>
    </div>
  )
}
