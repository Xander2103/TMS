import { type ReactNode } from 'react'
import { usePageTitle } from './usePageTitle'
import './PageHeader.css'

interface PageHeaderProps {
  title: string
  subtitle?: ReactNode
  action?: ReactNode
}

export function PageHeader({ title, subtitle, action }: PageHeaderProps) {
  usePageTitle(title)
  return (
    <div className="page-header">
      <div>
        <h2>{title}</h2>
        {subtitle && <p className="page-header-subtitle">{subtitle}</p>}
      </div>
      {action && <div className="page-header-action">{action}</div>}
    </div>
  )
}
