import type { ReactNode } from 'react'
import './PageHeader.css'

interface PageHeaderProps {
  title: string
  action?: ReactNode
}

export function PageHeader({ title, action }: PageHeaderProps) {
  return (
    <div className="page-header">
      <h2>{title}</h2>
      {action && <div className="page-header-action">{action}</div>}
    </div>
  )
}
