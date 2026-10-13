import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate, formatTime } from '../../../utils/dates'
import { formatCurrency, formatQuantity } from '../../../utils/numbers'
import {
  ORDER_PRICE_LINE_KIND_TONE,
  ORDER_STATUS_LABELS,
  ORDER_STATUS_TONE,
  PRICING_COVERAGE_LABELS,
  PRICING_COVERAGE_TONE,
  lineBadge,
  type OrderPricingLine,
} from '../types'
import { CargoCommercialCoverageTable } from './CargoCommercialCoverageTable'
import { describeCargoItem, type UnitLabelResolver } from './cargoLabels'
import type { OrderPricingWorkspace } from './useOrderPricingEditor'
import '../pages/transport-orders.css'
import '../detail/order-detail.css'
import './order-pricing.css'

/**
 * "04/08/2026 om 20:59" — the confirmation stamp next to the prominent total (§9). C-03: the
 * hour comes from formatTime (tenant zone), not from the browser's getHours().
 */
function formatConfirmedStamp(isoUtc: string): string {
  const time = formatTime(isoUtc)
  return time ? `${formatDate(isoUtc)} om ${time}` : ''
}

/**
 * Label for the Berekening column: quantity x unit x unit price when known AND consistent with
 * the shown Bedrag, else a flat-amount or unknown fallback. An amount-only adjustment (e.g.
 * Aantal cleared, Bedrag typed directly) legitimately leaves a stale quantity/unitPrice on the
 * stored line — showing the stale formula next to the real amount would contradict it, so the
 * formula is only rendered when it actually reproduces the amount (rounding-tolerant).
 */
function calculationLabel(line: OrderPricingLine): string {
  if (line.quantity != null && line.unitPrice != null) {
    const computed = Math.round(line.quantity * line.unitPrice * 100) / 100
    if (computed === line.amount) {
      const unit = line.unit ? ` ${line.unit}` : ''
      return `${formatQuantity(line.quantity)}${unit} × ${formatCurrency(line.unitPrice)}`
    }
  }
  return line.kind === 'Manual' ? 'Vast bedrag' : '—'
}

interface OrderPricingPanelProps {
  ws: OrderPricingWorkspace
  unitLabel: UnitLabelResolver
  /**
   * Price provenance as the HOST knows it from the server (the dossier payload). `false` makes the
   * summary read "no sales price yet" instead of an amount — a missing price is never € 0,00.
   * Omitted (the order page): the total is shown as before.
   */
  isPriced?: boolean
  /** Caption of the add-line action; the dossier speaks of "verkooplijnen". */
  addLineLabel?: string
  /** DOM id of the section root (the order page anchors its "go to price" jumps on it). */
  sectionId?: string
}

/**
 * Prijs workspace: the prominent total with its status, the price workflow actions grouped in
 * one toolbar, the coverage warning/list and the price lines table — every handler and rule
 * unchanged from before the redesign (they live in `useOrderPricingEditor`). Shared by the order
 * detail page and the dossier price tab (master sprint 2026-09-21, D5).
 */
export function OrderPricingPanel({ ws, unitLabel, isPriced, addLineLabel = '+ Vrije regel', sectionId }: OrderPricingPanelProps) {
  const { t } = useLocale()
  const {
    order, priceDisplay, totalPrice, pricingStatus, pricingLocked, pricingBusy,
    canLockPrice, canEditPricingStatus, canEditPricingLines, invoiceLines, notAppliedLines, coverage, unpricedCoverage,
  } = ws
  const hasPricing = Boolean(order.pricingSnapshot) || totalPrice !== null
  const hasCommercialCoverage = order.cargoItems.some((item) => item.commercialCoverage)
  const linkedGoods = (line: OrderPricingLine): string =>
    (line.cargoItemIds ?? [])
      .map((id) => order.cargoItems.find((item) => item.id === id))
      .filter((item) => item !== undefined)
      .map((item) => describeCargoItem(item, unitLabel))
      .join('; ')

  return (
    <div className="tod-section" id={sectionId}>
      <section className={`tod-panel tod-price-summary is-${priceDisplay.tone}`}>
        <div className="tod-price-summary-row">
          <div className="to-price-summary-main tod-price-main">
            <span className="to-price-summary-label">Totaalprijs</span>
            {isPriced === false ? (
              <span className="order-price-unpriced">{t('dossierPricing.noPriceSet')}</span>
            ) : (
              <span className="to-price-summary-amount">{formatCurrency(totalPrice ?? 0)}</span>
            )}
          </div>
          <div className="to-price-summary-status tod-price-status">
            <span>
              Opdracht: <Badge tone={ORDER_STATUS_TONE[order.status]}>{t(ORDER_STATUS_LABELS[order.status])}</Badge>
            </span>
            <span>
              Prijs: <Badge tone={priceDisplay.tone}>{t(priceDisplay.labelKey)}</Badge>
            </span>
          </div>
          <div className="to-header-actions to-price-status-actions tod-price-actions">
            {canLockPrice && (pricingStatus === 'Draft' || pricingStatus === 'Reviewed') && (
              <Button onClick={ws.handleConfirmPriceClick} disabled={pricingBusy}>
                Prijs bevestigen
              </Button>
            )}
            {canLockPrice && pricingStatus === 'Locked' && (
              <Button variant="secondary" onClick={ws.openReopenPrice} disabled={pricingBusy}>
                Prijs aanpassen
              </Button>
            )}
            {canEditPricingStatus && !pricingLocked && (
              <Button variant="secondary" onClick={ws.handleRecalculateClick} disabled={pricingBusy}>
                Herberekenen
              </Button>
            )}
            {canEditPricingLines && !pricingLocked && (
              <Button variant="secondary" onClick={ws.openAddLine} disabled={pricingBusy}>
                {addLineLabel}
              </Button>
            )}
            {order.pricingSnapshot && (
              <Button variant="ghost" onClick={ws.openCalcDetails}>
                Bekijk berekeningsdetails
              </Button>
            )}
          </div>
        </div>
        {order.pricingSnapshot?.confirmedAtUtc && (
          <p className="to-price-summary-confirmed">
            Bevestigd op {formatConfirmedStamp(order.pricingSnapshot.confirmedAtUtc)}
            {order.pricingSnapshot.confirmedByName ? ` door ${order.pricingSnapshot.confirmedByName}` : ''}.
          </p>
        )}
        {order.pricingSnapshot?.confirmedWithUnpricedGoodsReason && (
          <p className="to-price-summary-warning" role="note">
            Bevestigd terwijl niet alle goederen geprijsd zijn: {order.pricingSnapshot.confirmedWithUnpricedGoodsReason}
          </p>
        )}
        {order.pricingSnapshot?.isStale && (
          <p className="to-price-summary-warning" role="alert">
            Prijs verouderd — de goederen of voorwaarden zijn gewijzigd na de laatste berekening. Herbereken de prijs.
          </p>
        )}
        {!hasPricing && <p className="tod-muted">Nog geen prijsberekening voor deze opdracht.</p>}
      </section>

      {order.pricingLines && order.pricingLines.length > 0 && (
        <section className="tod-panel">
          <h2>
            Prijsregels <Badge tone={priceDisplay.tone}>{t(priceDisplay.labelKey)}</Badge>
          </h2>
          {unpricedCoverage.length > 0 && (
            <div className="to-coverage-warning" role="alert">
              <strong>Niet alle goederen zijn geprijsd.</strong>
              <ul>
                {unpricedCoverage.map((c, index) => (
                  <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                    {formatQuantity(c.quantity)} {c.unitLabel}:{' '}
                    {(c.reason ?? 'geen passend basistarief').toLowerCase()}
                    {c.servicesAmount > 0 && ` — alleen diensten (${formatCurrency(c.servicesAmount)}), geen transportprijs`}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {coverage.length > 0 && (
            <div className="to-coverage">
              <h3>Prijsdekking per goederenlijn</h3>
              <ul>
                {coverage.map((c, index) => (
                  <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                    <Badge tone={PRICING_COVERAGE_TONE[c.status]}>{t(PRICING_COVERAGE_LABELS[c.status])}</Badge>{' '}
                    {formatQuantity(c.quantity)} {c.unitLabel}
                    {c.status === 'Full' && ` — ${c.baseRuleName ?? 'basistarief'}: ${formatCurrency(c.baseAmount)}`}
                    {c.status !== 'Full' && c.reason ? ` — ${c.reason}` : ''}
                    {c.servicesAmount > 0 && ` · diensten ${formatCurrency(c.servicesAmount)}`}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <div className="tod-table-wrap">
            <table className="to-stops-table tod-table">
              <thead>
                <tr>
                  <th>Omschrijving</th>
                  <th>Type</th>
                  <th>Berekening</th>
                  <th className="tof-price-amount">Bedrag</th>
                  {canEditPricingLines && !pricingLocked && <th>Acties</th>}
                </tr>
              </thead>
              <tbody>
                {invoiceLines.map((line, index) => (
                  <tr
                    key={line.id ?? line.lineKey ?? index}
                    className={line.informational ? 'tof-price-informational' : undefined}
                  >
                    <td>
                      {line.label}
                      {line.kind === 'AutoAdjusted' && (
                        <div className="to-price-original">
                          <s>
                            {line.originalQuantity != null && line.originalUnitPrice != null
                              ? `${formatQuantity(line.originalQuantity)} × ${formatCurrency(line.originalUnitPrice)}`
                              : formatCurrency(line.originalAmount ?? 0)}
                          </s>
                          {' → '}
                          {line.quantity != null && line.unitPrice != null
                            ? `${formatQuantity(line.quantity)} × ${formatCurrency(line.unitPrice)}`
                            : formatCurrency(line.amount)}
                        </div>
                      )}
                      {linkedGoods(line) && (
                        <div className="order-price-line-goods">{t('dossierPricing.goodsLink.linked', { goods: linkedGoods(line) })}</div>
                      )}
                    </td>
                    <td>
                      <Badge tone={ORDER_PRICE_LINE_KIND_TONE[line.kind]}>{t(lineBadge(line))}</Badge>
                    </td>
                    <td>{calculationLabel(line)}</td>
                    <td className="tof-price-amount">{formatCurrency(line.amount)}</td>
                    {canEditPricingLines && !pricingLocked && (
                      <td className="to-price-line-actions">
                        {line.kind === 'Proposed' ? (
                          <Button variant="ghost" onClick={() => ws.handleConfirmLine(line)} disabled={pricingBusy}>
                            Bevestigen
                          </Button>
                        ) : !line.lineKey ? null : (
                          // A line without a LineKey (the engine's "geen tarief" diagnostics) cannot be
                          // addressed by the API: an edit would be sent as a NEW free line and refused.
                          <>
                            <Button variant="ghost" onClick={() => ws.openEditLine(line)} disabled={pricingBusy}>
                              Bewerken
                            </Button>
                            <Button variant="ghost" onClick={() => ws.openRemoveLine(line)} disabled={pricingBusy}>
                              Verwijderen
                            </Button>
                          </>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th>Totaal</th>
                  <th />
                  <th />
                  <th className="tof-price-amount">
                    {formatCurrency(order.pricingSnapshot?.linesTotal ?? order.agreedPrice ?? 0)}
                  </th>
                  {canEditPricingLines && !pricingLocked && <th />}
                </tr>
              </tfoot>
            </table>
          </div>
          {notAppliedLines.length > 0 && (
            <div className="to-price-not-applied">
              <h3>Niet toegepast</h3>
              <ul>
                {notAppliedLines.map((line, index) => (
                  <li key={line.id ?? line.lineKey ?? index}>{line.label}</li>
                ))}
              </ul>
            </div>
          )}
        </section>
      )}

      {hasCommercialCoverage && (
        <section className="tod-panel">
          <CargoCommercialCoverageTable cargoItems={order.cargoItems} unitLabel={unitLabel} />
        </section>
      )}
    </div>
  )
}
