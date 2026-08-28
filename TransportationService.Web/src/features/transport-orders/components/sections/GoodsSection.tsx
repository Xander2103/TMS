import { Button } from '../../../../components/ui/Button'
import { FormField } from '../../../../components/ui/FormField'
import { useLocale } from '../../../../i18n/localeContext'
import { UNIT_TYPE_LABELS, type PackageUnitType } from '../../../packages/types'
import { computeVolumeM3 } from '../../../../utils/volume'
import { formatQuantity } from '../../../../utils/numbers'
import type { CustomerPreferredUnit } from '../../../tarification/api/pricingApi'
import { UnitSelect, type UnitOptionItem } from '../UnitSelect'
import { numberOrNullFrom, type CargoFormRow, type CargoSummary, type StopFormRow } from './orderFormState'

interface GoodsSectionProps {
  goodsDescription: string
  setGoodsDescription: (value: string) => void
  quantity: string
  setQuantity: (value: string) => void
  /** Legacy free-text unit: preserved (still submitted) but no longer editable. */
  quantityUnit: string
  quantityUnitCode: string | null
  setQuantityUnitCode: (value: string | null) => void
  weightKg: string
  setWeightKg: (value: string) => void
  volumeM3: string
  setVolumeM3: (value: string) => void
  palletCount: string
  setPalletCount: (value: string) => void
  /** Wave 3 §1: geplande afstand (km) en laadmeters — optioneel, voeden PerKm/PerLdm-prijzen. */
  distanceKm: string
  setDistanceKm: (value: string) => void
  loadingMeters: string
  setLoadingMeters: (value: string) => void
  adrRequired: boolean
  setAdrRequired: (value: boolean) => void
  craneRequired: boolean
  setCraneRequired: (value: boolean) => void
  /** P6: uitrusting/beweging-prijsdimensies (naast ADR/kraan). */
  plateauRequired: boolean
  setPlateauRequired: (value: boolean) => void
  moffettRequired: boolean
  setMoffettRequired: (value: boolean) => void
  isReturnMovement: boolean
  setIsReturnMovement: (value: boolean) => void
  /** True once any cargo line exists: the header inputs make way for the derived summary. */
  derivedFromCargo: boolean
  cargoSummary: CargoSummary | null
  cargoItems: CargoFormRow[]
  stops: StopFormRow[]
  unitOptions: UnitOptionItem[]
  preferredUnits: CustomerPreferredUnit[]
  setCargo: (key: string, patch: Partial<CargoFormRow>) => void
  onAddCargoRow: () => void
  onAddCargoRowFromHeader: () => void
  onRemoveCargoRow: (key: string) => void
  /** Selecting a unit auto-fills physical defaults from the unit master (composer owns the master data). */
  applyCargoUnit: (key: string, code: string | null) => void
  /** Fixed dimensions come from the unit definition and are not editable per order line. */
  cargoDimensionsFixed: (cargo: CargoFormRow) => boolean
  saving: boolean
  /** First validation message per field path (inline errors + aria-invalid). */
  errors: Record<string, string>
}

/** Goederen section: header block (inputs or derived summary) + the cargo-line repeater. */
export function GoodsSection({
  goodsDescription,
  setGoodsDescription,
  quantity,
  setQuantity,
  quantityUnit,
  quantityUnitCode,
  setQuantityUnitCode,
  weightKg,
  setWeightKg,
  volumeM3,
  setVolumeM3,
  palletCount,
  setPalletCount,
  distanceKm,
  setDistanceKm,
  loadingMeters,
  setLoadingMeters,
  adrRequired,
  setAdrRequired,
  craneRequired,
  setCraneRequired,
  plateauRequired,
  setPlateauRequired,
  moffettRequired,
  setMoffettRequired,
  isReturnMovement,
  setIsReturnMovement,
  derivedFromCargo,
  cargoSummary,
  cargoItems,
  stops,
  unitOptions,
  preferredUnits,
  setCargo,
  onAddCargoRow,
  onAddCargoRowFromHeader,
  onRemoveCargoRow,
  applyCargoUnit,
  cargoDimensionsFixed,
  saving,
  errors,
}: GoodsSectionProps) {
  const { t } = useLocale()
  return (
    <>
      <FormField
        label={t('transportOrders.goods.description')}
        htmlFor="to-goods"
        hint={t('transportOrders.goods.descriptionHint')}
        error={errors.goodsDescription}
      >
        <textarea
          id="to-goods"
          rows={2}
          value={goodsDescription}
          onChange={(e) => setGoodsDescription(e.target.value)}
          disabled={saving}
          maxLength={1000}
          aria-invalid={errors.goodsDescription ? true : undefined}
        />
      </FormField>

      {!derivedFromCargo && (
        <>
          <div className="tof-row tof-row-4">
            <FormField label={t('transportOrders.goods.quantity')} htmlFor="to-qty">
              <input id="to-qty" type="number" min={0} step="0.01" value={quantity} onChange={(e) => setQuantity(e.target.value)} disabled={saving} />
            </FormField>
            <FormField
              label={t('transportOrders.goods.unit')}
              htmlFor="to-unit"
              hint={!quantityUnitCode && quantityUnit ? t('transportOrders.goods.legacyValue', { value: quantityUnit }) : undefined}
            >
              {/* Customer units first (favourites ★, customer label); the full active list stays reachable. */}
              <UnitSelect
                id="to-unit"
                value={quantityUnitCode}
                onChange={setQuantityUnitCode}
                units={unitOptions}
                preferredUnits={preferredUnits}
                disabled={saving}
              />
            </FormField>
            <FormField label={t('transportOrders.goods.weight')} htmlFor="to-weight">
              <input id="to-weight" type="number" min={0} step="0.01" value={weightKg} onChange={(e) => setWeightKg(e.target.value)} disabled={saving} />
            </FormField>
            <FormField label={t('transportOrders.goods.volume')} htmlFor="to-volume">
              <input id="to-volume" type="number" min={0} step="0.01" value={volumeM3} onChange={(e) => setVolumeM3(e.target.value)} disabled={saving} />
            </FormField>
          </div>

          <div className="tof-row tof-row-4">
            <FormField label={t('transportOrders.goods.pallets')} htmlFor="to-pallets">
              <input id="to-pallets" type="number" min={0} value={palletCount} onChange={(e) => setPalletCount(e.target.value)} disabled={saving} />
            </FormField>
            <FormField label={t('transportOrders.goods.ldm')} htmlFor="to-ldm" hint={t('transportOrders.goods.ldmHint')}>
              <input id="to-ldm" type="number" min={0} step="0.01" value={loadingMeters} onChange={(e) => setLoadingMeters(e.target.value)} disabled={saving} />
            </FormField>
            <FormField label={t('transportOrders.goods.distance')} htmlFor="to-distance" hint={t('transportOrders.goods.distanceHint')}>
              <input id="to-distance" type="number" min={0} step="0.01" value={distanceKm} onChange={(e) => setDistanceKm(e.target.value)} disabled={saving} />
            </FormField>
          </div>
        </>
      )}

      {derivedFromCargo && cargoSummary && (
        <div className="tof-derived-summary">
          <h4>{t('transportOrders.goods.ladingTitle')}</h4>
          <ul className="tof-lading-list">
            {cargoSummary.units.map(([label, qty]) => (
              <li key={label}>
                {formatQuantity(qty)} {label}
              </li>
            ))}
          </ul>
          {/* Wave 1 §12: all five derived header fields (aantal + eenheid in the list above,
              gewicht/volume/paletten here) render read-only; missing values show as "—". */}
          <p className="tof-cargo-hint">
            {[
              t('transportOrders.goods.summaryWeight', {
                value: cargoSummary.weight !== null ? `${formatQuantity(cargoSummary.weight)} kg` : '—',
              }),
              t('transportOrders.goods.summaryVolume', {
                value: cargoSummary.volume !== null ? `${formatQuantity(cargoSummary.volume)} m³` : '—',
              }),
              t('transportOrders.goods.summaryPallets', {
                value: cargoSummary.pallets !== null ? formatQuantity(cargoSummary.pallets) : '—',
              }),
            ].join(' · ')}
          </p>
          <p className="tof-cargo-hint">
            {t('transportOrders.goods.derivedNote')}
          </p>
        </div>
      )}

      <div className="tof-row tof-row-4">
        <label className="tof-checkbox">
          <input type="checkbox" checked={adrRequired} onChange={(e) => setAdrRequired(e.target.checked)} disabled={saving} />
          {t('transportOrders.goods.adr')}
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={craneRequired} onChange={(e) => setCraneRequired(e.target.checked)} disabled={saving} />
          {t('transportOrders.goods.crane')}
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={plateauRequired} onChange={(e) => setPlateauRequired(e.target.checked)} disabled={saving} />
          {t('transportOrders.goods.plateau')}
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={moffettRequired} onChange={(e) => setMoffettRequired(e.target.checked)} disabled={saving} />
          {t('transportOrders.goods.moffett')}
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={isReturnMovement} onChange={(e) => setIsReturnMovement(e.target.checked)} disabled={saving} />
          {t('transportOrders.goods.returnMovement')}
        </label>
      </div>

      <div className="tof-stops-header">
        <h3>{t('transportOrders.goods.linesTitle')}</h3>
        <div className="tof-stops-actions">
          {!derivedFromCargo && (quantity !== '' || weightKg !== '' || volumeM3 !== '' || palletCount !== '') && (
            <Button variant="secondary" onClick={onAddCargoRowFromHeader} disabled={saving}>
              {t('transportOrders.goods.convertHeader')}
            </Button>
          )}
          <Button variant="secondary" onClick={onAddCargoRow} disabled={saving}>
            {t('transportOrders.goods.addLine')}
          </Button>
        </div>
      </div>
      <p className="tof-cargo-hint">
        {t('transportOrders.goods.commercialHint')}
      </p>
      {cargoItems.length === 0 && (
        <p className="tof-cargo-hint">
          {t('transportOrders.goods.noLinesHint')}
        </p>
      )}
      {cargoItems.map((cargo, index) => {
        // Wave 1 §12: pinning is contextual — only offered when more than one stop of that
        // side exists (with a single pair the backend links the line automatically).
        const loadingStopCount = stops.filter((s) => s.stopType === 'Loading').length
        const unloadingStopCount = stops.filter((s) => s.stopType === 'Unloading').length
        // "Meer details" opens automatically when any advanced field carries a value, so
        // existing data never disappears behind a collapsed disclosure.
        const hasDetailContent = Boolean(
          cargo.barcode || cargo.unitType || cargo.weightPerUnitKg || cargo.palletCount ||
          cargo.reference || cargo.notes || cargo.lengthMeters || cargo.widthMeters ||
          cargo.heightMeters || (cargo.volumeIsManual && cargo.volumeM3) || !cargo.stackable ||
          (cargo.adrRequired && cargo.adrDetails),
        )
        return (
        <fieldset key={cargo.key} className="tof-stop">
          <legend>{t('transportOrders.goods.lineLegend', { number: index + 1 })}</legend>
          <div className="tof-row tof-row-4">
            <FormField label={t('transportOrders.goods.lineDescription')} htmlFor={`cg-desc-${cargo.key}`} hint={t('transportOrders.goods.lineDescriptionHint')}>
              <input id={`cg-desc-${cargo.key}`} value={cargo.description} onChange={(e) => setCargo(cargo.key, { description: e.target.value })} disabled={saving} maxLength={300} />
            </FormField>
            <FormField
              label={t('transportOrders.goods.expectedQty')}
              htmlFor={`cg-qty-${cargo.key}`}
              required
              error={errors[`cargoItems[${index}].expectedQuantity`]}
            >
              <input
                id={`cg-qty-${cargo.key}`}
                type="number"
                min={0.01}
                step="0.01"
                value={cargo.expectedQuantity}
                onChange={(e) => setCargo(cargo.key, { expectedQuantity: e.target.value })}
                disabled={saving}
                aria-invalid={errors[`cargoItems[${index}].expectedQuantity`] ? true : undefined}
              />
            </FormField>
            <FormField
              label={t('transportOrders.goods.unit')}
              htmlFor={`cg-unit-${cargo.key}`}
              hint={
                !cargo.quantityUnitCode && cargo.quantityUnit
                  ? t('transportOrders.goods.legacyValue', { value: cargo.quantityUnit })
                  : undefined
              }
            >
              <UnitSelect
                id={`cg-unit-${cargo.key}`}
                value={cargo.quantityUnitCode}
                onChange={(code) => applyCargoUnit(cargo.key, code)}
                units={unitOptions}
                preferredUnits={preferredUnits}
                disabled={saving}
              />
            </FormField>
            {/* Wave 1 §12: total weight feeds the weight tariffs — a default-view field. */}
            <FormField label={t('transportOrders.goods.totalWeight')} htmlFor={`cg-weight-${cargo.key}`}>
              <input id={`cg-weight-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.totalWeightKg} onChange={(e) => setCargo(cargo.key, { totalWeightKg: e.target.value })} disabled={saving} />
            </FormField>
          </div>
          {(loadingStopCount > 1 || unloadingStopCount > 1) && (
            <div className="tof-row">
              {loadingStopCount > 1 && (
                <FormField label={t('transportOrders.goods.loadingStop')} htmlFor={`cg-load-${cargo.key}`} hint={t('transportOrders.goods.loadingStopHint')}>
                  <select
                    id={`cg-load-${cargo.key}`}
                    value={cargo.loadingStopIndex}
                    onChange={(e) => setCargo(cargo.key, { loadingStopIndex: e.target.value })}
                    disabled={saving}
                  >
                    <option value="">{t('transportOrders.goods.automatic')}</option>
                    {stops.map((stop, stopIndex) =>
                      stop.stopType === 'Loading' ? (
                        <option key={stop.key} value={stopIndex}>
                          {t('transportOrders.goods.loadingOption', {
                            index: stopIndex + 1,
                            name: stop.city || stop.locationName || t('transportOrders.goods.stopFallback'),
                          })}
                        </option>
                      ) : null,
                    )}
                  </select>
                </FormField>
              )}
              {unloadingStopCount > 1 && (
                <FormField label={t('transportOrders.goods.unloadingStop')} htmlFor={`cg-unload-${cargo.key}`}>
                  <select
                    id={`cg-unload-${cargo.key}`}
                    value={cargo.unloadingStopIndex}
                    onChange={(e) => setCargo(cargo.key, { unloadingStopIndex: e.target.value })}
                    disabled={saving}
                  >
                    <option value="">{t('transportOrders.goods.automatic')}</option>
                    {stops.map((stop, stopIndex) =>
                      stop.stopType === 'Unloading' ? (
                        <option key={stop.key} value={stopIndex}>
                          {t('transportOrders.goods.unloadingOption', {
                            index: stopIndex + 1,
                            name: stop.city || stop.locationName || t('transportOrders.goods.stopFallback'),
                          })}
                        </option>
                      ) : null,
                    )}
                  </select>
                </FormField>
              )}
            </div>
          )}
          <div className="tof-row">
            <label className="tof-checkbox">
              <input type="checkbox" checked={cargo.adrRequired} onChange={(e) => setCargo(cargo.key, { adrRequired: e.target.checked })} disabled={saving} />
              {t('transportOrders.goods.adrGoods')}
            </label>
          </div>
          <details className="tof-stop-details" open={hasDetailContent}>
            <summary>{t('transportOrders.goods.moreDetails')}</summary>
            <div className="tof-row tof-row-4">
              <FormField label={t('transportOrders.goods.barcode')} htmlFor={`cg-bc-${cargo.key}`}>
                <input id={`cg-bc-${cargo.key}`} value={cargo.barcode} onChange={(e) => setCargo(cargo.key, { barcode: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label={t('transportOrders.goods.weightPerUnit')} htmlFor={`cg-unitweight-${cargo.key}`}>
                <input id={`cg-unitweight-${cargo.key}`} type="number" min={0} step="0.001" value={cargo.weightPerUnitKg} onChange={(e) => setCargo(cargo.key, { weightPerUnitKg: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label={t('transportOrders.goods.linePallets')} htmlFor={`cg-pallets-${cargo.key}`} hint={t('transportOrders.goods.linePalletsHint')}>
                <input id={`cg-pallets-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.palletCount} onChange={(e) => setCargo(cargo.key, { palletCount: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label={t('transportOrders.goods.reference')} htmlFor={`cg-ref-${cargo.key}`}>
                <input id={`cg-ref-${cargo.key}`} value={cargo.reference} onChange={(e) => setCargo(cargo.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="tof-row">
              <FormField label={t('transportOrders.goods.packagingType')} htmlFor={`cg-type-${cargo.key}`}>
                <select
                  id={`cg-type-${cargo.key}`}
                  value={cargo.unitType}
                  onChange={(e) => setCargo(cargo.key, { unitType: e.target.value as PackageUnitType | '' })}
                  disabled={saving}
                >
                  <option value="">{t('transportOrders.goods.notSpecified')}</option>
                  {Object.entries(UNIT_TYPE_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              {cargo.unitType === 'Other' && (
                <FormField label={t('transportOrders.goods.customTypeName')} htmlFor={`cg-typelabel-${cargo.key}`}>
                  <input id={`cg-typelabel-${cargo.key}`} value={cargo.unitTypeLabel} onChange={(e) => setCargo(cargo.key, { unitTypeLabel: e.target.value })} disabled={saving} maxLength={50} />
                </FormField>
              )}
              <FormField label={t('transportOrders.goods.remarks')} htmlFor={`cg-notes-${cargo.key}`}>
                <input id={`cg-notes-${cargo.key}`} value={cargo.notes} onChange={(e) => setCargo(cargo.key, { notes: e.target.value })} disabled={saving} maxLength={500} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField
                label={t('transportOrders.goods.length')}
                htmlFor={`cg-length-${cargo.key}`}
                hint={cargoDimensionsFixed(cargo) ? t('transportOrders.goods.lengthFixedHint') : undefined}
              >
                <input id={`cg-length-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.lengthMeters} onChange={(e) => setCargo(cargo.key, { lengthMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField label={t('transportOrders.goods.width')} htmlFor={`cg-width-${cargo.key}`}>
                <input id={`cg-width-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.widthMeters} onChange={(e) => setCargo(cargo.key, { widthMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField label={t('transportOrders.goods.height')} htmlFor={`cg-height-${cargo.key}`}>
                <input id={`cg-height-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.heightMeters} onChange={(e) => setCargo(cargo.key, { heightMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField
                label={t('transportOrders.goods.volumePerUnit')}
                htmlFor={`cg-volume-${cargo.key}`}
                hint={cargo.volumeIsManual ? t('transportOrders.goods.volumeManualHint') : t('transportOrders.goods.volumeAutoHint')}
              >
                <input
                  id={`cg-volume-${cargo.key}`}
                  type="number"
                  min={0}
                  step="0.001"
                  value={
                    cargo.volumeIsManual
                      ? cargo.volumeM3
                      : (computeVolumeM3(
                          numberOrNullFrom(cargo.lengthMeters),
                          numberOrNullFrom(cargo.widthMeters),
                          numberOrNullFrom(cargo.heightMeters),
                        ) ?? '')
                  }
                  onChange={(e) => setCargo(cargo.key, { volumeM3: e.target.value })}
                  disabled={saving || !cargo.volumeIsManual}
                />
                <label className="tof-checkbox">
                  <input
                    type="checkbox"
                    checked={cargo.volumeIsManual}
                    onChange={(e) => setCargo(cargo.key, { volumeIsManual: e.target.checked })}
                    disabled={saving}
                  />
                  {t('transportOrders.goods.manual')}
                </label>
              </FormField>
            </div>
            <div className="tof-row">
              {cargo.adrRequired && (
                <FormField label={t('transportOrders.goods.adrDetails')} htmlFor={`cg-adr-${cargo.key}`} hint={t('transportOrders.goods.adrDetailsHint')}>
                  <input id={`cg-adr-${cargo.key}`} value={cargo.adrDetails} onChange={(e) => setCargo(cargo.key, { adrDetails: e.target.value })} disabled={saving} maxLength={500} />
                </FormField>
              )}
              <label className="tof-checkbox">
                <input type="checkbox" checked={cargo.stackable} onChange={(e) => setCargo(cargo.key, { stackable: e.target.checked })} disabled={saving} />
                {t('transportOrders.goods.stackable')}
              </label>
            </div>
          </details>
          <div className="tof-stop-toolbar">
            <button
              type="button"
              className="tof-link tof-link-danger"
              onClick={() => onRemoveCargoRow(cargo.key)}
              disabled={saving}
            >
              {t('ui.actions.delete')}
            </button>
          </div>
        </fieldset>
        )
      })}
    </>
  )
}
