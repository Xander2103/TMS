import { useLocale } from '../../../i18n/localeContext'
import { euro } from '../../invoices/types'
import type { DossierActivity, DossierDetail } from '../types'
import { activityUnitName, readActivityPrice } from './activityPriceDisplay'
import { PriceStatusBadge } from './PriceStatusBadge'
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
 * Master sprint 2026-09-21 (spec §15): every commercial activity is selectable on the price tab —
 * ALWAYS shown, also with a single unit (pricing "Plateau" must not depend on a second activity
 * existing). One compact row per activity: "ORD-0005 — Direct transport · € 102,37" or
 * "Plateau · Nog niet geprijsd" plus the server's price-status badge. Toggle buttons
 * (aria-pressed) in a named group, like the route's order switcher.
 */
export function ActivityPricePicker({ dossier, activities, selectedActivityId, onSelect, locked, lockedHint }: ActivityPricePickerProps) {
  const { t } = useLocale()
  const caption = t('dossierSheet.orderSwitch.unitLabel')
  if (activities.length === 0) return null

  return (
    <div className="dossier-price-picker">
      <div role="group" aria-label={caption}>
        <ul className="dossier-price-picker-list">
          {activities.map((activity) => {
            const selected = activity.id === selectedActivityId
            const name = activityUnitName(activity)
            const price = readActivityPrice(dossier, activity)
            return (
              <li key={activity.id}>
                <button
                  type="button"
                  className={`dossier-price-picker-item${selected ? ' is-selected' : ''}`}
                  aria-pressed={selected}
                  disabled={locked && !selected}
                  onClick={() => {
                    if (!selected) onSelect(activity.id)
                  }}
                >
                  <span className="dossier-price-picker-name">
                    {activity.linkedOrderNumber ? t('dossierPricing.picker.itemWithOrder', { number: activity.linkedOrderNumber, name }) : name}
                    {activity.hasStops && !activity.linkedOrderNumber && (
                      <span className="dossier-price-picker-secondary"> ({t('dossierSheet.orderSwitch.noOrderYet')})</span>
                    )}
                  </span>
                  <span className="dossier-price-picker-price">
                    {' · '}
                    {price.kind === 'amount' ? euro(price.amount) : price.kind === 'free' ? t('dossierPricing.free') : t('dossierPricing.notPricedYet')}
                  </span>
                  <PriceStatusBadge status={activity.priceStatus} />
                </button>
              </li>
            )
          })}
        </ul>
      </div>
      {locked && lockedHint && <p className="dossier-order-switch-hint">{lockedHint}</p>}
    </div>
  )
}
