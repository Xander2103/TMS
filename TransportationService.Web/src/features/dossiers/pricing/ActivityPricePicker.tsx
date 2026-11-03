import { useId, useRef, type KeyboardEvent } from 'react'
import { ChevronRight } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import type { DossierActivity, DossierDetail } from '../types'
import { activityUnitKind, activityUnitName, readActivityPrice } from './activityPriceDisplay'
import { PriceStatusBadge } from './PriceStatusBadge'
import { priceUnitIcon } from './priceUnitDisplay'
import './dossier-pricing.css'

interface ActivityPricePickerProps {
  dossier: DossierDetail
  /** Every commercial (billable) activity in dossier order — transport AND standalone ones. */
  activities: DossierActivity[]
  selectedActivityId: string
  onSelect: (activityId: string) => void
  /** Unsaved route or price edits: switching is locked so they can never land on another activity. */
  locked: boolean
  /** Why switching is locked (shown under the list). */
  lockedHint?: string
}

/**
 * Pricing UX sprint 2026-10-08: the MASTER list of the price tab — "Prijsbare onderdelen (N)".
 * One selectable card per billable unit: its icon, reference (order number), name, the KIND of unit
 * ("Opdracht" = priced through its order, "Activiteit" = own price record — decided by the type's
 * `hasStops` capability, never by a name), the amount and ONE status badge (Niet geprijsd /
 * Gedeeltelijk / Geprijsd / Controle vereist / Gratis). ALWAYS shown, also with a single unit, so
 * the planner sees at a glance how many separately priced parts the dossier has and which one the
 * detail panel is editing. A listbox with roving tabindex: arrows move AND select.
 */
export function ActivityPricePicker({ dossier, activities, selectedActivityId, onSelect, locked, lockedHint }: ActivityPricePickerProps) {
  const { t } = useLocale()
  const headingId = useId()
  const refs = useRef<Record<string, HTMLButtonElement | null>>({})
  if (activities.length === 0) return null

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    let next: number
    if (event.key === 'ArrowDown') next = Math.min(index + 1, activities.length - 1)
    else if (event.key === 'ArrowUp') next = Math.max(index - 1, 0)
    else if (event.key === 'Home') next = 0
    else if (event.key === 'End') next = activities.length - 1
    else return
    event.preventDefault()
    const target = activities[next]
    refs.current[target.id]?.focus()
    if (!locked && target.id !== selectedActivityId) onSelect(target.id)
  }

  return (
    <div className="dossier-price-master">
      <h3 id={headingId} className="dossier-price-master-title">
        {t('dossierPricing.master.title', { count: activities.length })}
      </h3>
      <p className="dossier-price-master-hint">{t('dossierPricing.master.hint')}</p>
      <div role="listbox" aria-labelledby={headingId} aria-orientation="vertical" className="dossier-price-master-list">
        {activities.map((activity, index) => {
          const selected = activity.id === selectedActivityId
          const kind = activityUnitKind(activity)
          const price = readActivityPrice(dossier, activity)
          const amount = price.kind === 'amount' ? price.amount : price.kind === 'free' ? 0 : null
          const Icon = priceUnitIcon(activity)
          return (
            <button
              key={activity.id}
              ref={(element) => {
                refs.current[activity.id] = element
              }}
              type="button"
              role="option"
              aria-selected={selected}
              tabIndex={selected ? 0 : -1}
              className={`dossier-price-master-item${selected ? ' is-selected' : ''}`}
              data-kind={kind}
              disabled={locked && !selected}
              onClick={() => {
                if (!selected) onSelect(activity.id)
              }}
              onKeyDown={(event) => onKeyDown(event, index)}
            >
              <Icon className="dossier-price-master-icon" size={22} aria-hidden="true" />
              <span className="dossier-price-master-main">
                <span className="dossier-price-master-title-row">
                  {activity.linkedOrderNumber && <span className="dossier-price-master-ref">{activity.linkedOrderNumber}</span>}
                  <span className="dossier-price-master-name">{activityUnitName(activity)}</span>
                </span>
                {activity.hasStops && !activity.linkedOrderNumber && (
                  <span className="dossier-price-master-secondary">{t('dossierSheet.orderSwitch.noOrderYet')}</span>
                )}
                <span className="dossier-price-master-kind">{t(kind === 'order' ? 'dossierPricing.unit.order' : 'dossierPricing.unit.activity')}</span>
              </span>
              <span className="dossier-price-master-state">
                {amount !== null && <span className="dossier-price-master-amount">{euro(amount)}</span>}
                {activity.priceStatus ? (
                  <PriceStatusBadge status={activity.priceStatus} />
                ) : (
                  // Older payload without a server status: the reading in words, never a derived badge.
                  <span className="dossier-price-master-secondary">
                    {price.kind === 'free' ? t('dossierPricing.free') : price.kind === 'none' ? t('dossierPricing.notPricedYet') : null}
                  </span>
                )}
              </span>
              <ChevronRight className="dossier-price-master-chevron" size={18} aria-hidden="true" />
            </button>
          )
        })}
      </div>
      {locked && lockedHint && <p className="dossier-order-switch-hint">{lockedHint}</p>}
    </div>
  )
}
