import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import { useToast } from '../../../components/ui/toastContext'
import { formatCurrency, formatQuantity } from '../../../utils/numbers'
import { UnitSelect, type UnitOptionItem } from '../components/UnitSelect'
import { PRICING_COVERAGE_LABELS, PRICING_COVERAGE_TONE } from '../types'
import { CargoLinkSelect } from './CargoLinkSelect'
import type { OrderPricingEditor } from './useOrderPricingEditor'
import './order-pricing.css'

interface OrderPricingDialogsProps {
  editor: OrderPricingEditor
  /** Managed unit types (the host already loads them for its own labels). */
  unitTypes: UnitOptionItem[]
}

/**
 * The dialogs of the order price line editor (edit / remove / add line, calculation details,
 * recalculate, confirm and reopen price) — lifted unchanged out of `TransportOrderDetailPage`
 * together with `useOrderPricingEditor`, so the order page and the dossier price tab share them.
 */
export function OrderPricingDialogs({ editor, unitTypes }: OrderPricingDialogsProps) {
  const { t } = useLocale()
  const { showError } = useToast()
  const {
    order, pricingBusy, coverage, unpricedCoverage, canOverrideIncomplete,
    editLine, removeLine, addLineOpen, addMode, calcDetailsOpen, recalcConfirmOpen, confirmPriceOpen, reopenPriceOpen,
  } = editor
  if (!order) return null

  const editAmountIsComputed = editor.computedEditAmount !== null
  const computedEditAmountDisplay = editor.computedEditAmount !== null ? formatCurrency(editor.computedEditAmount) : '—'
  const computedAddTotalDisplay = editor.computedAddTotal !== null ? formatCurrency(editor.computedAddTotal) : '—'

  return (
    <>
      {editLine && (
        <Modal
          title="Prijsregel aanpassen"
          onClose={editor.closeEditLine}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={editor.closeEditLine} disabled={pricingBusy}>
                Terug
              </Button>
              <Button onClick={() => void editor.handleSaveEditLine()} disabled={pricingBusy}>
                {pricingBusy ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <p className="customer-form-muted">{editLine.label}</p>
          <div className="tof-row">
            <FormField label="Aantal" htmlFor="price-line-qty">
              <input
                id="price-line-qty"
                type="number"
                step="0.01"
                value={editor.editQuantity}
                onChange={(e) => editor.setEditQuantity(e.target.value)}
                disabled={pricingBusy}
              />
            </FormField>
            <FormField label="Eenheidsprijs (€)" htmlFor="price-line-unit-price">
              <input
                id="price-line-unit-price"
                type="number"
                step="0.01"
                value={editor.editUnitPrice}
                onChange={(e) => editor.setEditUnitPrice(e.target.value)}
                disabled={pricingBusy}
              />
            </FormField>
          </div>
          <FormField
            label="Bedrag (€)"
            htmlFor="price-line-amount"
            hint={editAmountIsComputed ? 'Berekend als aantal × eenheidsprijs.' : 'Leeg = aantal × eenheidsprijs.'}
          >
            {editAmountIsComputed ? (
              <input id="price-line-amount" readOnly value={computedEditAmountDisplay} />
            ) : (
              <input
                id="price-line-amount"
                type="number"
                step="0.01"
                value={editor.editAmount}
                onChange={(e) => editor.setEditAmount(e.target.value)}
                disabled={pricingBusy}
              />
            )}
          </FormField>
          <FormField
            label="Reden"
            htmlFor="price-line-reason"
            required={editor.editReasonRequired}
            hint={editor.editReasonRequired ? 'Verplicht bij een aanpassing.' : t('dossierPricing.goodsLink.noReasonNeeded')}
          >
            <input
              id="price-line-reason"
              value={editor.editReason}
              onChange={(e) => editor.setEditReason(e.target.value)}
              disabled={pricingBusy}
              maxLength={500}
              autoFocus
            />
          </FormField>
          <CargoLinkSelect
            idPrefix="price-line-cargo"
            cargoItems={order.cargoItems}
            units={unitTypes}
            value={editor.editCargoItemIds}
            onChange={editor.setEditCargoItemIds}
            disabled={pricingBusy}
          />
        </Modal>
      )}

      {removeLine && (
        <Modal
          title="Prijsregel verwijderen"
          onClose={editor.closeRemoveLine}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={editor.closeRemoveLine} disabled={pricingBusy}>
                Terug
              </Button>
              <Button variant="danger" onClick={() => void editor.handleRemoveLine()} disabled={pricingBusy}>
                {pricingBusy ? 'Verwijderen…' : 'Verwijderen'}
              </Button>
            </>
          }
        >
          <p>
            Weet je zeker dat je de regel <strong>{removeLine.label}</strong> wilt verwijderen?
          </p>
          {removeLine.kind !== 'Manual' && (
            <FormField label="Reden" htmlFor="price-line-remove-reason" required hint="Verplicht bij het verwijderen van een berekende regel.">
              <input
                id="price-line-remove-reason"
                value={editor.removeReason}
                onChange={(e) => editor.setRemoveReason(e.target.value)}
                disabled={pricingBusy}
                maxLength={500}
                autoFocus
              />
            </FormField>
          )}
        </Modal>
      )}

      {addLineOpen && (
        <Modal
          title="Vrije prijsregel toevoegen"
          onClose={editor.closeAddLine}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={editor.closeAddLine} disabled={pricingBusy}>
                Terug
              </Button>
              <Button onClick={() => void editor.handleAddLine()} disabled={pricingBusy}>
                {pricingBusy ? 'Toevoegen…' : 'Toevoegen'}
              </Button>
            </>
          }
        >
          <FormField label="Omschrijving" htmlFor="add-line-label" required>
            <input id="add-line-label" value={editor.addLabel} onChange={(e) => editor.setAddLabel(e.target.value)} disabled={pricingBusy} maxLength={300} autoFocus />
          </FormField>
          <FormField label="Berekeningswijze" htmlFor="add-line-mode">
            <div role="radiogroup" className="tof-radio-row" id="add-line-mode">
              <label>
                <input
                  type="radio"
                  name="add-line-mode"
                  checked={addMode === 'perUnit'}
                  onChange={() => editor.setAddMode('perUnit')}
                  disabled={pricingBusy}
                />{' '}
                Berekenen op basis van aantal en eenheidsprijs
              </label>
              <label>
                <input
                  type="radio"
                  name="add-line-mode"
                  checked={addMode === 'fixed'}
                  onChange={() => editor.setAddMode('fixed')}
                  disabled={pricingBusy}
                />{' '}
                Vast bedrag
              </label>
            </div>
          </FormField>
          {addMode === 'perUnit' && (
            <>
              <div className="tof-row">
                <FormField label="Aantal" htmlFor="add-line-qty">
                  <input
                    id="add-line-qty"
                    type="number"
                    min="0.01"
                    step="any"
                    value={editor.addQuantity}
                    onChange={(e) => editor.setAddQuantity(e.target.value)}
                    disabled={pricingBusy}
                  />
                </FormField>
                <FormField label="Eenheid" htmlFor="add-line-unit" hint="Optioneel.">
                  <UnitSelect
                    id="add-line-unit"
                    value={editor.addUnit}
                    onChange={editor.setAddUnit}
                    units={unitTypes}
                    preferredUnits={[]}
                    disabled={pricingBusy}
                  />
                </FormField>
              </div>
              <div className="tof-row">
                <FormField label="Eenheidsprijs (€)" htmlFor="add-line-price">
                  <input
                    id="add-line-price"
                    type="number"
                    step="any"
                    value={editor.addUnitPrice}
                    onChange={(e) => editor.setAddUnitPrice(e.target.value)}
                    disabled={pricingBusy}
                  />
                </FormField>
                <FormField label="Totaalbedrag" htmlFor="add-line-total">
                  <input id="add-line-total" readOnly value={computedAddTotalDisplay} />
                </FormField>
              </div>
            </>
          )}
          {addMode === 'fixed' && (
            <FormField label="Totaalbedrag (€)" htmlFor="add-line-amount">
              <input
                id="add-line-amount"
                type="number"
                step="any"
                value={editor.addAmount}
                onChange={(e) => editor.setAddAmount(e.target.value)}
                disabled={pricingBusy}
              />
            </FormField>
          )}
          <FormField label="Reden" htmlFor="add-line-reason" hint="Optioneel.">
            <input id="add-line-reason" value={editor.addReason} onChange={(e) => editor.setAddReason(e.target.value)} disabled={pricingBusy} maxLength={500} />
          </FormField>
          <CargoLinkSelect
            idPrefix="add-line-cargo"
            cargoItems={order.cargoItems}
            units={unitTypes}
            value={editor.addCargoItemIds}
            onChange={editor.setAddCargoItemIds}
            disabled={pricingBusy}
          />
        </Modal>
      )}

      {calcDetailsOpen && order.pricingSnapshot && (
        <Modal title="Berekeningsdetails" onClose={editor.closeCalcDetails}>
          <p className="customer-form-muted">
            Tariefdatum: {order.pricingSnapshot.tariffDate}
            {order.pricingSnapshot.zoneName ? ` · Zone: ${order.pricingSnapshot.zoneName} (${order.pricingSnapshot.zoneCode})` : ''}
            {order.pricingSnapshot.agreementNames ? ` · Tarief: ${order.pricingSnapshot.agreementNames}` : ''}
          </p>
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
          <pre className="to-calc-explanation">{order.pricingSnapshot.explanation}</pre>
        </Modal>
      )}

      {recalcConfirmOpen && (
        <ConfirmDialog
          title="Prijs herberekenen"
          message="De prijs is al gecontroleerd. Toch herberekenen?"
          confirmLabel="Herberekenen"
          busy={pricingBusy}
          onConfirm={() => void editor.handleRecalculate()}
          onCancel={editor.closeRecalcConfirm}
        />
      )}

      {confirmPriceOpen && (
        <Modal
          title="Prijs bevestigen"
          onClose={editor.closeConfirmPrice}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={editor.closeConfirmPrice} disabled={pricingBusy}>
                {canOverrideIncomplete ? 'Annuleren' : 'Sluiten'}
              </Button>
              {canOverrideIncomplete && (
                <Button
                  onClick={() => {
                    if (!editor.confirmPriceReason.trim()) {
                      showError('Geef een reden op om te bevestigen terwijl niet alle goederen geprijsd zijn.')
                      return
                    }
                    void editor.handleConfirmPrice(editor.confirmPriceReason.trim())
                  }}
                  disabled={pricingBusy}
                >
                  Toch bevestigen
                </Button>
              )}
            </>
          }
        >
          <p>
            <strong>De prijs kan niet worden bevestigd.</strong>
          </p>
          <ul>
            {unpricedCoverage.map((c, index) => (
              <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                {formatQuantity(c.quantity)} {c.unitLabel}:{' '}
                {(c.reason ?? 'geen passend basistarief').toLowerCase()}
              </li>
            ))}
          </ul>
          {canOverrideIncomplete ? (
            <FormField
              label="Reden"
              htmlFor="confirm-price-reason"
              hint="Verplicht — de waarschuwing blijft zichtbaar bij de bevestigde prijs."
              required
            >
              <input
                id="confirm-price-reason"
                value={editor.confirmPriceReason}
                onChange={(e) => editor.setConfirmPriceReason(e.target.value)}
                disabled={pricingBusy}
                maxLength={500}
                autoFocus
              />
            </FormField>
          ) : (
            <p className="customer-form-muted">
              Prijs de goederen (basistarief of goederenlijn corrigeren) of vraag iemand met de juiste rechten
              om toch te bevestigen.
            </p>
          )}
        </Modal>
      )}

      {reopenPriceOpen && (
        <Modal
          title="Prijs aanpassen"
          onClose={editor.closeReopenPrice}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={editor.closeReopenPrice} disabled={pricingBusy}>
                Annuleren
              </Button>
              <Button onClick={() => void editor.handleReopenPrice()} disabled={pricingBusy}>
                Prijs aanpassen
              </Button>
            </>
          }
        >
          <p>
            De prijs gaat terug naar <strong>Nog te bevestigen</strong>; de huidige totaalprijs en bevestiging
            blijven bewaard in de historiek. Bevestig de prijs opnieuw na het aanpassen.
          </p>
          <FormField label="Reden" htmlFor="reopen-price-reason" required>
            <input
              id="reopen-price-reason"
              value={editor.reopenPriceReason}
              onChange={(e) => editor.setReopenPriceReason(e.target.value)}
              disabled={pricingBusy}
              maxLength={500}
              autoFocus
            />
          </FormField>
        </Modal>
      )}
    </>
  )
}
