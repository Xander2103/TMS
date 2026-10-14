import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { InlineSelect } from '../../../components/ui/InlineSelect'
import { useLocale } from '../../../i18n/localeContext'
import { formatDecimal } from '../../../utils/numbers'
import {
  ORDER_PRIORITIES,
  ORDER_PRIORITY_LABELS,
  ORDER_STATUS_LABELS,
  type OrderPriority,
  type TransportOrderStatus,
} from '../../transport-orders/types'
import { ATTENTION_BADGE_LABELS, DRAG_MIME, encodeDragPayload, isPlannable, type UnplannedOrder } from '../types'
import { formatRoute, formatWeightShort } from '../utils/boardFormat'
import { ConflictHint } from './ConflictHint'

export interface UnplannedFilters {
  search: string
  status: TransportOrderStatus | ''
  priority: OrderPriority | ''
  fromDate: string
  toDate: string
  onlyWithAttention: boolean
  /** B09: Draft work shown subdued (visibility, never plannable). */
  includeDrafts: boolean
}

interface UnplannedPanelProps {
  orders: UnplannedOrder[]
  totalCount: number
  page: number
  pageSize: number
  isLoading: boolean
  filters: UnplannedFilters
  selectedOrderId: string | null
  onFiltersChange: (filters: UnplannedFilters) => void
  onPageChange: (page: number) => void
  onSelect: (order: UnplannedOrder) => void
  /** Accessible alternative for drag-and-drop: plan the selected order via a picker. */
  onPlanRequest: (order: UnplannedOrder) => void
  /** Safe inline priority edit; backend-validated and audited. */
  onPriorityChange: (order: UnplannedOrder, priority: OrderPriority) => Promise<void>
  /** B08: the panel collapses to a rail. */
  onCollapse?: () => void
}

/**
 * Left zone: the unplanned work pool. B09: ONE line per order — number · customer · route ·
 * weight · ⚠ n — so a laptop shows twenty orders instead of six; the selected row unfolds its
 * details (meta, priority, "Plan in…"). Draft and Submitted work is listed subdued and is never
 * draggable: the lifecycle stays intact, the pipeline stays visible.
 */
export function UnplannedPanel({
  orders, totalCount, page, pageSize, isLoading, filters, selectedOrderId,
  onFiltersChange, onPageChange, onSelect, onPlanRequest, onPriorityChange, onCollapse,
}: UnplannedPanelProps) {
  const { t } = useLocale()
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const priorityLabels = Object.fromEntries(
    ORDER_PRIORITIES.map((priority) => [priority, t(ORDER_PRIORITY_LABELS[priority])]),
  ) as Record<OrderPriority, string>

  /** Why a row cannot be dragged, in the planner's words. */
  function notPlannableReason(order: UnplannedOrder): string {
    return order.status === 'Draft'
      ? t('planningCenter.unplanned.notPlannableDraft')
      : t('planningCenter.unplanned.notPlannableSubmitted')
  }

  return (
    <section className="pc-panel pc-unplanned" aria-label={t('planningCenter.unplanned.label')}>
      <header className="pc-panel-header">
        <h2>{t('planningCenter.unplanned.title')}</h2>
        <span className="pc-panel-header-right">
          <span className="pc-count">{totalCount}</span>
          {onCollapse && (
            <button type="button" className="pc-collapse" onClick={onCollapse} aria-label={t('planningCenter.page.collapseLeft')} title={t('planningCenter.page.collapseLeft')}>
              «
            </button>
          )}
        </span>
      </header>
      <div className="pc-filters">
        <input
          type="search"
          placeholder={t('planningCenter.unplanned.searchPlaceholder')}
          value={filters.search}
          onChange={(event) => onFiltersChange({ ...filters, search: event.target.value })}
          aria-label={t('planningCenter.unplanned.searchLabel')}
        />
        <div className="pc-filter-row">
          <select
            value={filters.status}
            onChange={(event) => onFiltersChange({ ...filters, status: event.target.value as TransportOrderStatus | '' })}
            aria-label={t('planningCenter.unplanned.statusLabel')}
          >
            <option value="">{t('planningCenter.unplanned.allStatuses')}</option>
            <option value="Confirmed">{t(ORDER_STATUS_LABELS.Confirmed)}</option>
            <option value="Submitted">{t(ORDER_STATUS_LABELS.Submitted)}</option>
            {filters.includeDrafts && <option value="Draft">{t(ORDER_STATUS_LABELS.Draft)}</option>}
          </select>
          <select
            value={filters.priority}
            onChange={(event) => onFiltersChange({ ...filters, priority: event.target.value as OrderPriority | '' })}
            aria-label={t('planningCenter.unplanned.priorityLabel')}
          >
            <option value="">{t('planningCenter.unplanned.allPriorities')}</option>
            {ORDER_PRIORITIES.map((priority) => (
              <option key={priority} value={priority}>{priorityLabels[priority]}</option>
            ))}
          </select>
        </div>
        <div className="pc-filter-row">
          <input
            type="date"
            value={filters.fromDate}
            onChange={(event) => onFiltersChange({ ...filters, fromDate: event.target.value })}
            aria-label={t('planningCenter.unplanned.fromDateLabel')}
          />
          <input
            type="date"
            value={filters.toDate}
            onChange={(event) => onFiltersChange({ ...filters, toDate: event.target.value })}
            aria-label={t('planningCenter.unplanned.toDateLabel')}
          />
        </div>
        <div className="pc-filter-row pc-filter-checks">
          <label className="pc-check">
            <input
              type="checkbox"
              checked={filters.onlyWithAttention}
              onChange={(event) => onFiltersChange({ ...filters, onlyWithAttention: event.target.checked })}
            />
            {t('planningCenter.unplanned.onlyWithAttention')}
          </label>
          <label className="pc-check">
            <input
              type="checkbox"
              checked={filters.includeDrafts}
              onChange={(event) => onFiltersChange({
                ...filters,
                includeDrafts: event.target.checked,
                // Hiding drafts again also drops a Draft-only status filter, or the list would go empty.
                status: !event.target.checked && filters.status === 'Draft' ? '' : filters.status,
              })}
            />
            {t('planningCenter.unplanned.showDrafts')}
          </label>
        </div>
      </div>
      <div className="pc-unplanned-list" role="list">
        {isLoading && <p className="pc-muted">{t('planningCenter.unplanned.loading')}</p>}
        {!isLoading && orders.length === 0 && <p className="pc-muted">{t('planningCenter.unplanned.empty')}</p>}
        {orders.map((order) => {
          const plannable = isPlannable(order)
          const selected = selectedOrderId === order.id
          const route = formatRoute(order.firstLoadingCity, order.lastUnloadingCity)
          const reasons = order.attentionBadges.map((badge) => t(ATTENTION_BADGE_LABELS[badge] ?? badge))
          return (
            <article
              key={order.id}
              role="listitem"
              tabIndex={0}
              className={[
                'pc-order',
                selected ? 'pc-selected' : '',
                plannable ? '' : 'pc-order-locked',
                order.status === 'Draft' ? 'pc-order-draft' : '',
              ].filter(Boolean).join(' ')}
              data-priority={order.priority}
              data-status={order.status}
              draggable={plannable}
              onDragStart={(event) => {
                if (!plannable) {
                  event.preventDefault()
                  return
                }
                event.dataTransfer.setData(DRAG_MIME, encodeDragPayload({
                  kind: 'order', id: order.id, label: order.orderNumber,
                }))
                event.dataTransfer.effectAllowed = 'move'
              }}
              onClick={() => onSelect(order)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  if (plannable) onPlanRequest(order)
                  else onSelect(order)
                }
              }}
              aria-label={t('planningCenter.unplanned.orderAriaLabel', { orderNumber: order.orderNumber, customerName: order.customerName })}
            >
              {/* B09: two tight lines (≈40px): number · weight · ⚠ n, then customer · route. */}
              <div className="pc-order-line">
                <span className="pc-order-prio" aria-hidden="true" />
                <code className="pc-order-number">{order.orderNumber}</code>
                {order.totalWeightKg !== null && <span className="pc-order-weight">{formatWeightShort(order.totalWeightKg)}</span>}
                {reasons.length > 0 && (
                  <ConflictHint
                    tone="warning"
                    label={`⚠ ${reasons.length}`}
                    title={t('planningCenter.unplanned.attentionTitle')}
                    reasons={reasons}
                    ariaLabel={t('planningCenter.unplanned.attentionCount', { count: reasons.length })}
                    className="pc-order-flag"
                  />
                )}
              </div>
              <div className="pc-order-sub">
                <span className="pc-order-customer" title={order.customerName}>{order.customerName}</span>
                {plannable ? (
                  <span className="pc-order-route" title={route ?? undefined}>{route ?? t('planningCenter.unplanned.noRoute')}</span>
                ) : (
                  <span className="pc-order-route pc-order-state" title={notPlannableReason(order)}>
                    {t(ORDER_STATUS_LABELS[order.status])} · {t('planningCenter.unplanned.notPlannableShort')}
                  </span>
                )}
              </div>
              {selected && (
                <div className="pc-order-details">
                  {!plannable && (
                    <p className="pc-order-hint">
                      {notPlannableReason(order)}{' '}
                      <Link to={`/transport-orders/${order.id}`} onClick={(event) => event.stopPropagation()}>
                        {t('planningCenter.unplanned.openOrder')}
                      </Link>
                    </p>
                  )}
                  <p className="pc-order-meta">
                    {order.customerReference && <span>{t('planningCenter.unplanned.reference', { reference: order.customerReference })}</span>}
                    {order.goodsDescription && <span>{order.goodsDescription}</span>}
                    {order.stopCount > 0 && <span>{t('planningCenter.unplanned.stops', { count: order.stopCount })}</span>}
                    {order.packageCount > 0 && <span>{t('planningCenter.unplanned.packages', { count: order.packageCount })}</span>}
                    {order.totalVolumeM3 !== null && <span>{formatDecimal(order.totalVolumeM3)} m³</span>}
                    {order.adrRequired && <span className="pc-flag">ADR</span>}
                    {order.craneRequired && <span className="pc-flag">{t('planningCenter.unplanned.crane')}</span>}
                  </p>
                  {reasons.length > 0 && (
                    <div className="pc-order-badges">
                      {reasons.map((reason) => <Badge key={reason} tone="warning">{reason}</Badge>)}
                    </div>
                  )}
                  <div className="pc-order-actions">
                    <label className="pc-order-priority">
                      <span>{t('planningCenter.unplanned.priorityLabel')}</span>
                      <InlineSelect
                        value={order.priority}
                        options={ORDER_PRIORITIES}
                        labels={priorityLabels}
                        ariaLabel={t('planningCenter.unplanned.priorityOf', { orderNumber: order.orderNumber })}
                        onChange={(priority) => onPriorityChange(order, priority)}
                      />
                    </label>
                    {plannable && (
                      <button
                        type="button"
                        className="pc-plan-button"
                        onClick={(event) => {
                          event.stopPropagation()
                          onPlanRequest(order)
                        }}
                      >
                        {t('planningCenter.unplanned.planInto')}
                      </button>
                    )}
                  </div>
                </div>
              )}
            </article>
          )
        })}
      </div>
      {totalPages > 1 && (
        <footer className="pc-paging">
          <button type="button" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>‹</button>
          <span>{page} / {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>›</button>
        </footer>
      )}
    </section>
  )
}
