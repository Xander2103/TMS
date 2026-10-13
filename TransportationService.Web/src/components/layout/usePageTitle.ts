import { useEffect } from 'react'

const APP_NAME = 'Transportation Service'

/**
 * Documenttitel (i18n-wave §41): elke pagina rendert een PageHeader met de al vertaalde
 * titel, dus dat is HET centrale punt voor de browsertab — geen aparte titelregistry.
 * Bij unmount valt de tab terug op de appnaam. Eigen module (react-refresh: een
 * componentbestand exporteert alleen componenten).
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
