import { useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import type { NavItem } from './navConfig'
import { moduleHasUnread, type VisibleModule } from './navState'

interface NavItemRowProps {
  item: NavItem
  depth: number
  unreadCount: number
  onNavigate?: () => void
}

/** One row. With children it becomes a locally-collapsible submenu (nested-ready). */
function NavItemRow({ item, depth, unreadCount, onNavigate }: NavItemRowProps) {
  const [open, setOpen] = useState(false)
  const Icon = item.icon
  const badgeCount = item.badge === 'notifications' ? unreadCount : 0
  const indent = { paddingLeft: `${12 + depth * 14}px` }

  if (item.children && item.children.length > 0) {
    return (
      <li>
        <button
          type="button"
          className="nav-subitem-toggle"
          style={indent}
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
        >
          {Icon && <Icon className="nav-item-icon" size={16} aria-hidden />}
          <span className="nav-item-label">{item.label}</span>
          <ChevronDown className={`nav-chevron${open ? ' nav-chevron-open' : ''}`} size={14} aria-hidden />
        </button>
        {open && (
          <ul className="nav-subitems">
            {item.children.map((child) => (
              <NavItemRow key={child.to} item={child} depth={depth + 1} unreadCount={unreadCount} onNavigate={onNavigate} />
            ))}
          </ul>
        )}
      </li>
    )
  }

  return (
    <li>
      <NavLink
        to={item.to}
        style={indent}
        className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
        onClick={onNavigate}
      >
        {Icon && <Icon className="nav-item-icon" size={16} aria-hidden />}
        <span className="nav-item-label">{item.label}</span>
        {badgeCount > 0 && <span className="nav-badge">{badgeCount > 99 ? '99+' : badgeCount}</span>}
      </NavLink>
    </li>
  )
}

export interface NavModuleProps {
  vm: VisibleModule
  expanded: boolean
  active: boolean
  unreadCount: number
  onToggle: (id: string) => void
  onNavigate?: () => void
}

export function NavModule({ vm, expanded, active, unreadCount, onToggle, onNavigate }: NavModuleProps) {
  const { module } = vm
  const Icon = module.icon
  const regionId = `navmod-${module.id}`
  const collapsedDot = !expanded && moduleHasUnread(vm, unreadCount)

  return (
    <li className={`nav-module${active ? ' nav-module-active' : ''}`}>
      <button
        type="button"
        className="nav-module-header"
        aria-expanded={expanded}
        aria-controls={regionId}
        onClick={() => onToggle(module.id)}
      >
        <Icon className="nav-module-icon" size={18} aria-hidden />
        <span className="nav-module-title">{module.label}</span>
        {collapsedDot && <span className="nav-module-dot" aria-hidden />}
        <ChevronDown className={`nav-chevron${expanded ? ' nav-chevron-open' : ''}`} size={16} aria-hidden />
      </button>
      <div
        id={regionId}
        className="nav-module-region"
        role="region"
        aria-label={module.label}
        data-expanded={expanded}
        inert={!expanded ? true : undefined}
      >
        <div className="nav-module-region-inner">
          <ul className="nav-module-items">
            {vm.items.map((item) => (
              <NavItemRow key={item.to} item={item} depth={0} unreadCount={unreadCount} onNavigate={onNavigate} />
            ))}
            {vm.subgroups.map((sg) => (
              <li key={sg.label} className="nav-subgroup">
                <div className="nav-subgroup-label">{sg.label}</div>
                <ul className="nav-subitems">
                  {sg.items.map((item) => (
                    <NavItemRow key={item.to} item={item} depth={0} unreadCount={unreadCount} onNavigate={onNavigate} />
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </li>
  )
}
