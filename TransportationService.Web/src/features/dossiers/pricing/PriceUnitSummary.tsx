import { createElement, type ReactNode } from 'react'
import { AlertCircle, AlertTriangle, CheckCircle2, Coins, Info, Tag } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity, DossierDetail } from '../types'
import { activityUnitKind, activityUnitName } from './activityPriceDisplay'
import { PriceStatusBadge } from './PriceStatusBadge'
import { priceAttention, priceUnitIcon } from './priceUnitDisplay'
import type { ActivityPriceStatus } from './types'
import './dossier-pricing.css'

/** "0005 — Direct transport · Opdracht · Geprijsd": which part the detail is about. */
export function PriceUnitHeader({ activity, fallbackTitle }: { activity: DossierActivity | null; fallbackTitle?: string }) {
  const { t } = useLocale()
  const kind = activity ? activityUnitKind(activity) : 'order'
  const name = activity ? activityUnitName(activity) : null
  return (
    <header className="dossier-pricing-detail-header">
      {createElement(activity ? priceUnitIcon(activity) : Tag, { size: 22, 'aria-hidden': true })}
      <h3 className="dossier-pricing-detail-name">
        {activity?.linkedOrderNumber ? `${activity.linkedOrderNumber} — ${name}` : (name ?? fallbackTitle)}
        {activity?.hasStops && !activity.linkedOrderNumber && (
          <span className="dossier-price-master-secondary"> ({t('dossierSheet.orderSwitch.noOrderYet')})</span>
        )}
      </h3>
      <span className="dossier-pricing-kind" data-kind={kind}>
        {t(kind === 'order' ? 'dossierPricing.unit.order' : 'dossierPricing.unit.activity')}
      </span>
      <PriceStatusBadge status={activity?.priceStatus} />
    </header>
  )
}

function StatusIcon({ status }: { status: ActivityPriceStatus }) {
  if (status === 'Priced') return <CheckCircle2 size={18} aria-hidden="true" />
  if (status === 'Free') return <Info size={18} aria-hidden="true" />
  return <AlertCircle size={18} aria-hidden="true" />
}

interface PriceUnitSummaryProps {
  dossier: DossierDetail
  activity: DossierActivity | null
  /** Title when no activity represents the order (legacy target). */
  fallbackTitle?: string
  /** The unit's total as the server reads it: an amount, "Gratis", or "no price" in words — never € 0,00 for a missing price. */
  total: ReactNode
  unpriced?: boolean
  /** The status in words when there is no server status (older payload, legacy target). */
  statusFallback?: ReactNode
  /** Quiet second line under the status: the order's own status and the price workflow state. */
  statusDetail?: ReactNode
  actions?: ReactNode
  /** Makes another open unit the selected one. */
  onSelectUnit?: (activityId: string) => void
  /** Unsaved edits: another unit cannot be selected. */
  selectLocked?: boolean
}

/**
 * The strong summary of the SELECTED unit: what it is, its total, its status, what still needs
 * attention on this dossier, and the actions on this price.
 */
export function PriceUnitSummary({
  dossier, activity, fallbackTitle, total, unpriced = false, statusFallback, statusDetail, actions, onSelectUnit, selectLocked = false,
}: PriceUnitSummaryProps) {
  const { t } = useLocale()
  const billable = dossier.activities.filter((a) => a.isBillable !== false && a.countsForPricing !== false)
  const attention = priceAttention(dossier, billable)
  const nextOpen = attention.openUnits.find((unit) => unit.id !== activity?.id)
  const status = activity?.priceStatus ?? null
  const attentionText = t('dossierPricing.panel.attention', { count: attention.count })

  return (
    <section className="dossier-price-unit" aria-label={t('dossierPricing.detail.title')}>
      <PriceUnitHeader activity={activity} fallbackTitle={fallbackTitle} />
      <div className="dossier-price-unit-body">
        <div className="dossier-price-unit-total">
          <span className="dossier-price-unit-total-icon" aria-hidden="true">
            <Coins size={24} />
          </span>
          <div>
            <span className="dossier-price-unit-label">{t('dossierPricing.panel.total')}</span>
            <strong className={`dossier-price-unit-amount${unpriced ? ' is-unpriced' : ''}`} data-testid="unit-total">{total}</strong>
          </div>
        </div>
        <div className="dossier-price-unit-tile">
          <span className="dossier-price-unit-label">
            <Tag size={16} aria-hidden="true" /> {t('dossierPricing.panel.priceStatus')}
          </span>
          {status ? (
            <span className="dossier-price-unit-status" data-status={status}>
              <StatusIcon status={status} /> {t(`dossierPricing.status.${status}`)}
            </span>
          ) : (
            <span className="dossier-price-unit-status">{statusFallback}</span>
          )}
          {statusDetail && <span className="dossier-price-unit-detail">{statusDetail}</span>}
        </div>
        <div className="dossier-price-unit-tile is-attention">
          <span className="dossier-price-unit-label">
            <AlertTriangle size={16} aria-hidden="true" /> {t('dossierPricing.panel.attentionLabel')}
          </span>
          {attention.count === 0 ? (
            <span className="dossier-price-unit-detail">{t('dossierPricing.panel.attentionNone')}</span>
          ) : nextOpen && onSelectUnit ? (
            <button type="button" className="link-button dossier-price-unit-next" disabled={selectLocked} onClick={() => onSelectUnit(nextOpen.id)}>
              {attentionText}
            </button>
          ) : (
            <span className="dossier-price-unit-detail">{attentionText}</span>
          )}
        </div>
        {actions && <div className="dossier-price-unit-actions">{actions}</div>}
      </div>
    </section>
  )
}
