import { createElement, type ComponentType, type Ref } from 'react'
import { NavLink } from 'react-router-dom'
import { Boxes, History, LayoutDashboard, MapPin, MessageSquare, Package, Tag } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { ORDER_TAB_LABEL_KEYS, orderTabPath, type OrderTab } from './orderSections'

const TAB_ICONS: Record<OrderTab, ComponentType<{ size?: number; 'aria-hidden'?: boolean }>> = {
  overzicht: LayoutDashboard,
  lading: Package,
  prijs: Tag,
  stops: MapPin,
  colli: Boxes,
  historiek: History,
  berichten: MessageSquare,
}

interface OrderSubnavProps {
  orderId: string
  tabs: OrderTab[]
  ref?: Ref<HTMLElement>
}

/** Navigation INSIDE one order: real links, router-marked active state, scrollable when narrow. */
export function OrderSubnav({ orderId, tabs, ref }: OrderSubnavProps) {
  const { t } = useLocale()
  return (
    <nav className="tod-subnav" aria-label={t('transportOrders.detail.nav.label')} ref={ref}>
      <ul>
        {tabs.map((tab) => (
          <li key={tab}>
            <NavLink
              to={orderTabPath(orderId, tab)}
              end={tab === 'overzicht'}
              className={({ isActive }) => (isActive ? 'tod-subnav-link is-active' : 'tod-subnav-link')}
            >
              {createElement(TAB_ICONS[tab], { size: 16, 'aria-hidden': true })}
              <span>{t(ORDER_TAB_LABEL_KEYS[tab])}</span>
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
