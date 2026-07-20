import { Badge } from '../../../components/ui/Badge'
import { InlineSelect } from '../../../components/ui/InlineSelect'
import {
  ORDER_PRIORITIES,
  ORDER_PRIORITY_LABELS,
  ORDER_STATUS_LABELS,
  type OrderPriority,
  type TransportOrderStatus,
} from '../../transport-orders/types'
import { ATTENTION_BADGE_LABELS, DRAG_MIME, encodeDragPayload, type UnplannedOrder } from '../types'

export interface UnplannedFilters {
  search: string
  status: TransportOrderStatus | ''
  priority: OrderPriority | ''
  fromDate: string
  toDate: string
  onlyWithAttention: boolean
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
}

/** Left zone: the unplanned work pool. Rows are draggable onto the board. */
export function UnplannedPanel({
  orders, totalCount, page, pageSize, isLoading, filters, selectedOrderId,
  onFiltersChange, onPageChange, onSelect, onPlanRequest, onPriorityChange,
}: UnplannedPanelProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <section className="pc-panel pc-unplanned" aria-label="Ongepland werk">
      <header className="pc-panel-header">
        <h2>Ongepland</h2>
        <span className="pc-count">{totalCount}</span>
      </header>
      <div className="pc-filters">
        <input
          type="search"
          placeholder="Zoek opdracht, klant, referentie…"
          value={filters.search}
          onChange={(event) => onFiltersChange({ ...filters, search: event.target.value })}
          aria-label="Zoeken in ongeplande opdrachten"
        />
        <div className="pc-filter-row">
          <select
            value={filters.status}
            onChange={(event) => onFiltersChange({ ...filters, status: event.target.value as TransportOrderStatus | '' })}
            aria-label="Status"
          >
            <option value="">Alle statussen</option>
            <option value="Confirmed">{ORDER_STATUS_LABELS.Confirmed}</option>
            <option value="Submitted">{ORDER_STATUS_LABELS.Submitted}</option>
          </select>
          <select
            value={filters.priority}
            onChange={(event) => onFiltersChange({ ...filters, priority: event.target.value as OrderPriority | '' })}
            aria-label="Prioriteit"
          >
            <option value="">Alle prioriteiten</option>
            {ORDER_PRIORITIES.map((priority) => (
              <option key={priority} value={priority}>{ORDER_PRIORITY_LABELS[priority]}</option>
            ))}
          </select>
        </div>
        <div className="pc-filter-row">
          <input
            type="date"
            value={filters.fromDate}
            onChange={(event) => onFiltersChange({ ...filters, fromDate: event.target.value })}
            aria-label="Vanaf datum"
          />
          <input
            type="date"
            value={filters.toDate}
            onChange={(event) => onFiltersChange({ ...filters, toDate: event.target.value })}
            aria-label="Tot datum"
          />
        </div>
        <label className="pc-check">
          <input
            type="checkbox"
            checked={filters.onlyWithAttention}
            onChange={(event) => onFiltersChange({ ...filters, onlyWithAttention: event.target.checked })}
          />
          Alleen aandachtspunten
        </label>
      </div>
      <div className="pc-unplanned-list" role="list">
        {isLoading && <p className="pc-muted">Laden…</p>}
        {!isLoading && orders.length === 0 && <p className="pc-muted">Geen ongeplande opdrachten.</p>}
        {orders.map((order) => (
          <article
            key={order.id}
            role="listitem"
            tabIndex={0}
            className={`pc-order-card${selectedOrderId === order.id ? ' pc-selected' : ''}`}
            draggable
            onDragStart={(event) => {
              event.dataTransfer.setData(DRAG_MIME, encodeDragPayload({
                kind: 'order', id: order.id, label: order.orderNumber,
              }))
              event.dataTransfer.effectAllowed = 'move'
            }}
            onClick={() => onSelect(order)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault()
                onPlanRequest(order)
              }
            }}
            aria-label={`Opdracht ${order.orderNumber} van ${order.customerName}. Enter om in te plannen.`}
          >
            <div className="pc-order-head">
              <strong>{order.orderNumber}</strong>
              <InlineSelect
                value={order.priority}
                options={ORDER_PRIORITIES}
                labels={ORDER_PRIORITY_LABELS}
                ariaLabel={`Prioriteit van ${order.orderNumber}`}
                onChange={(priority) => onPriorityChange(order, priority)}
              />
            </div>
            <p className="pc-order-customer">{order.customerName}</p>
            <p className="pc-order-route">
              {order.firstLoadingCity ?? '?'} → {order.lastUnloadingCity ?? '?'}
            </p>
            <p className="pc-order-meta">
              {order.packageCount > 0 && <span>{order.packageCount} colli</span>}
              {order.totalWeightKg !== null && <span>{order.totalWeightKg.toLocaleString('nl-BE')} kg</span>}
              {order.totalVolumeM3 !== null && <span>{order.totalVolumeM3.toLocaleString('nl-BE')} m³</span>}
              {order.adrRequired && <span className="pc-flag">ADR</span>}
              {order.craneRequired && <span className="pc-flag">Kraan</span>}
            </p>
            {order.attentionBadges.length > 0 && (
              <div className="pc-order-badges">
                {order.attentionBadges.map((badge) => (
                  <Badge key={badge} tone="warning">{ATTENTION_BADGE_LABELS[badge] ?? badge}</Badge>
                ))}
              </div>
            )}
            <button
              type="button"
              className="pc-plan-button"
              onClick={(event) => {
                event.stopPropagation()
                onPlanRequest(order)
              }}
            >
              Plan in…
            </button>
          </article>
        ))}
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
