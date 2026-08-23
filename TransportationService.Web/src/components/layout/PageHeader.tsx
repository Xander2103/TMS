import { useEffect, type ReactNode } from 'react'
import './PageHeader.css'

interface PageHeaderProps {
  title: string
  subtitle?: ReactNode
  action?: ReactNode
}

const APP_NAME = 'Transportation Service'

/**
 * Documenttitel (i18n-wave §41): elke pagina rendert een PageHeader met de al vertaalde
 * titel, dus dit is HET centrale punt voor de browsertab — geen aparte titelregistry.
 * Bij unmount valt de tab terug op de appnaam.
 */
export function usePageTitle(title: string | null | undefined): void {
  useEffect(() => {
    if (typeof document === 'undefined') return
    document.title = title ? `${title} · ${APP_NAME}` : APP_NAME
    return () => {
      document.title = APP_NAME
    }
  }, [title])
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
