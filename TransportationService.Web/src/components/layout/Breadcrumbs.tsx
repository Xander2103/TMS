import { Link } from 'react-router-dom'
import './Breadcrumbs.css'

export interface Crumb {
  label: string
  to?: string
}

/** Simple breadcrumb trail. The last crumb is rendered as the current (non-link) page. */
export function Breadcrumbs({ items }: { items: Crumb[] }) {
  if (items.length === 0) return null

  return (
    <nav className="ui-breadcrumbs" aria-label="Kruimelpad">
      <ol>
        {items.map((item, index) => {
          const isLast = index === items.length - 1
          return (
            <li key={`${item.label}-${index}`}>
              {item.to && !isLast ? <Link to={item.to}>{item.label}</Link> : <span aria-current={isLast ? 'page' : undefined}>{item.label}</span>}
              {!isLast && <span className="ui-breadcrumbs-sep" aria-hidden="true">/</span>}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
