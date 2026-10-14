import { Link } from 'react-router-dom'
import { useLocale } from '../../i18n/localeContext'
import './Breadcrumbs.css'

export interface Crumb {
  label: string
  to?: string
  /**
   * An identifier (DOS-0022, ORD-0001, an invoice number) is never truncated. Absent → detected:
   * a short label without spaces is treated as an identifier.
   */
  identifier?: boolean
}

/** A label up to this length is short enough to never need a tooltip. */
const TOOLTIP_FROM_LENGTH = 24
const IDENTIFIER_MAX_LENGTH = 24

function isIdentifier(item: Crumb): boolean {
  if (item.identifier !== undefined) return item.identifier
  return item.label.length <= IDENTIFIER_MAX_LENGTH && !/\s/.test(item.label)
}

/**
 * Simple breadcrumb trail on ONE compact line (B54). The last crumb is rendered as the current
 * (non-link) page. A long crumb (a 90-character customer name) is clipped with an ellipsis — the
 * last one shrinks first — while the DOM text stays complete (screen readers, find-in-page) and a
 * `title` shows the full value on hover. Identifier crumbs never shrink.
 */
export function Breadcrumbs({ items }: { items: Crumb[] }) {
  const { t } = useLocale()
  if (items.length === 0) return null

  return (
    <nav className="ui-breadcrumbs" aria-label={t('ui.nav.breadcrumbs')}>
      <ol>
        {items.map((item, index) => {
          const isLast = index === items.length - 1
          const identifier = isIdentifier(item)
          const className = [
            'ui-breadcrumbs-item',
            identifier ? 'ui-breadcrumbs-item--id' : null,
            isLast ? 'ui-breadcrumbs-item--current' : null,
          ].filter(Boolean).join(' ')
          const title = !identifier && item.label.length > TOOLTIP_FROM_LENGTH ? item.label : undefined
          return (
            <li key={`${item.label}-${index}`} className={className}>
              {item.to && !isLast ? (
                <Link to={item.to} className="ui-breadcrumbs-label" title={title}>{item.label}</Link>
              ) : (
                <span className="ui-breadcrumbs-label" title={title} aria-current={isLast ? 'page' : undefined}>
                  {item.label}
                </span>
              )}
              {!isLast && <span className="ui-breadcrumbs-sep" aria-hidden="true">/</span>}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
