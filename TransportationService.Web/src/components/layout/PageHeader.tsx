import { type ReactNode } from 'react'
import { usePageTitle } from './usePageTitle'
import './PageHeader.css'

interface PageHeaderProps {
  title: string
  subtitle?: ReactNode
  action?: ReactNode
  /** Status badges shown on the title line (entity detail headers, B15–B24). */
  badges?: ReactNode
}

export function PageHeader({ title, subtitle, action, badges }: PageHeaderProps) {
  usePageTitle(title)
  return (
    <div className="page-header">
      <div>
        {badges ? (
          <div className="page-header-titleline">
            <h2>{title}</h2>
            <span className="page-header-badges">{badges}</span>
          </div>
        ) : (
          <h2>{title}</h2>
        )}
        {subtitle && <p className="page-header-subtitle">{subtitle}</p>}
      </div>
      {action && <div className="page-header-action">{action}</div>}
    </div>
  )
}
