import { createElement, type ComponentType, type Ref } from 'react'
import { NavLink } from 'react-router-dom'
import { FileText, History, LayoutDashboard, ListChecks, MapPin, Package, Tag } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { DOSSIER_TAB_LABEL_KEYS, dossierTabPath, type DossierTab } from '../dossierSections'

const TAB_ICONS: Record<DossierTab, ComponentType<{ size?: number; 'aria-hidden'?: boolean }>> = {
  overzicht: LayoutDashboard,
  activiteiten: ListChecks,
  route: MapPin,
  goederen: Package,
  prijs: Tag,
  documenten: FileText,
  historiek: History,
}

interface DossierSubnavProps {
  dossierId: string
  tabs: DossierTab[]
  ref?: Ref<HTMLElement>
}

/**
 * Navigation INSIDE one dossier: real links (deep-linkable, back/forward, middle-click), the
 * active one marked by the router (`aria-current="page"`). Horizontally scrollable on narrow
 * viewports instead of wrapping into a multi-line bar; labels stay visible on desktop.
 */
export function DossierSubnav({ dossierId, tabs, ref }: DossierSubnavProps) {
  const { t } = useLocale()
  return (
    <nav className="dossier-subnav" aria-label={t('dossiers.nav.label')} ref={ref}>
      <ul>
        {tabs.map((tab) => (
          <li key={tab}>
            <NavLink
              to={dossierTabPath(dossierId, tab)}
              end={tab === 'overzicht'}
              className={({ isActive }) => (isActive ? 'dossier-subnav-link is-active' : 'dossier-subnav-link')}
            >
              {createElement(TAB_ICONS[tab], { size: 18, 'aria-hidden': true })}
              <span>{t(DOSSIER_TAB_LABEL_KEYS[tab])}</span>
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
