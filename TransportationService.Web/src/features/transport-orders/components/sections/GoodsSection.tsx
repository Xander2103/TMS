import { Button } from '../../../../components/ui/Button'
import { FormField } from '../../../../components/ui/FormField'
import { UNIT_TYPE_LABELS, type PackageUnitType } from '../../../packages/types'
import { computeVolumeM3 } from '../../../../utils/volume'
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
  adrRequired: boolean
  setAdrRequired: (value: boolean) => void
  craneRequired: boolean
  setCraneRequired: (value: boolean) => void
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
  adrRequired,
  setAdrRequired,
  craneRequired,
  setCraneRequired,
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
  return (
    <>
      <FormField
        label="Omschrijving goederen"
        htmlFor="to-goods"
        hint="Optioneel wanneer de goederen hieronder per lijn worden beschreven."
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
            <FormField label="Aantal" htmlFor="to-qty">
              <input id="to-qty" type="number" min={0} step="0.01" value={quantity} onChange={(e) => setQuantity(e.target.value)} disabled={saving} />
            </FormField>
            <FormField
              label="Eenheid"
              htmlFor="to-unit"
              hint={!quantityUnitCode && quantityUnit ? `Bestaande waarde: ${quantityUnit}` : undefined}
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
            <FormField label="Gewicht (kg)" htmlFor="to-weight">
              <input id="to-weight" type="number" min={0} step="0.01" value={weightKg} onChange={(e) => setWeightKg(e.target.value)} disabled={saving} />
            </FormField>
            <FormField label="Volume (m³)" htmlFor="to-volume">
              <input id="to-volume" type="number" min={0} step="0.01" value={volumeM3} onChange={(e) => setVolumeM3(e.target.value)} disabled={saving} />
            </FormField>
          </div>

          <div className="tof-row tof-row-4">
            <FormField label="Paletten" htmlFor="to-pallets">
              <input id="to-pallets" type="number" min={0} value={palletCount} onChange={(e) => setPalletCount(e.target.value)} disabled={saving} />
            </FormField>
          </div>
        </>
      )}

      {derivedFromCargo && cargoSummary && (
        <div className="tof-derived-summary">
          <h4>Lading</h4>
          <ul className="tof-lading-list">
            {cargoSummary.units.map(([label, qty]) => (
              <li key={label}>
                {qty.toLocaleString('nl-BE')} {label}
              </li>
            ))}
          </ul>
          {/* Wave 1 §12: all five derived header fields (aantal + eenheid in the list above,
              gewicht/volume/paletten here) render read-only; missing values show as "—". */}
          <p className="tof-cargo-hint">
            {[
              `Totaal gewicht: ${cargoSummary.weight !== null ? `${cargoSummary.weight.toLocaleString('nl-BE')} kg` : '—'}`,
              `Volume: ${cargoSummary.volume !== null ? `${cargoSummary.volume.toLocaleString('nl-BE')} m³` : '—'}`,
              `Paletten: ${cargoSummary.pallets !== null ? cargoSummary.pallets.toLocaleString('nl-BE') : '—'}`,
            ].join(' · ')}
          </p>
          <p className="tof-cargo-hint">
            De samenvatting wordt automatisch afgeleid van de goederenlijnen hieronder.
          </p>
        </div>
      )}

      <div className="tof-row tof-row-4">
        <label className="tof-checkbox">
          <input type="checkbox" checked={adrRequired} onChange={(e) => setAdrRequired(e.target.checked)} disabled={saving} />
          ADR-transport
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={craneRequired} onChange={(e) => setCraneRequired(e.target.checked)} disabled={saving} />
          Kraan vereist
        </label>
      </div>

      <div className="tof-stops-header">
        <h3>Goederenlijnen</h3>
        <div className="tof-stops-actions">
          {!derivedFromCargo && (quantity !== '' || weightKg !== '' || volumeM3 !== '' || palletCount !== '') && (
            <Button variant="secondary" onClick={onAddCargoRowFromHeader} disabled={saving}>
              Zet samenvatting om naar goederenlijn
            </Button>
          )}
          <Button variant="secondary" onClick={onAddCargoRow} disabled={saving}>
            + Goederenlijn
          </Button>
        </div>
      </div>
      <p className="tof-cargo-hint">
        Commerciële hoeveelheden voor inhoud en prijs. Scanbare colli worden bij bevestiging per lijn gegenereerd en
        zijn een apart begrip.
      </p>
      {cargoItems.length === 0 && (
        <p className="tof-cargo-hint">
          Zonder goederenlijnen kan de chauffeur niet scannen; de opdracht blijft wel gewoon uitvoerbaar.
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
          <legend>Lijn {index + 1}</legend>
          <div className="tof-row tof-row-4">
            <FormField label="Omschrijving" htmlFor={`cg-desc-${cargo.key}`} hint="Optioneel als de algemene omschrijving is ingevuld.">
              <input id={`cg-desc-${cargo.key}`} value={cargo.description} onChange={(e) => setCargo(cargo.key, { description: e.target.value })} disabled={saving} maxLength={300} />
            </FormField>
            <FormField
              label="Verwacht aantal"
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
              label="Eenheid"
              htmlFor={`cg-unit-${cargo.key}`}
              hint={!cargo.quantityUnitCode && cargo.quantityUnit ? `Bestaande waarde: ${cargo.quantityUnit}` : undefined}
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
            <FormField label="Totaal gewicht (kg)" htmlFor={`cg-weight-${cargo.key}`}>
              <input id={`cg-weight-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.totalWeightKg} onChange={(e) => setCargo(cargo.key, { totalWeightKg: e.target.value })} disabled={saving} />
            </FormField>
          </div>
          {(loadingStopCount > 1 || unloadingStopCount > 1) && (
            <div className="tof-row">
              {loadingStopCount > 1 && (
                <FormField label="Laadstop" htmlFor={`cg-load-${cargo.key}`} hint="Automatisch bij één laad- en losstop.">
                  <select
                    id={`cg-load-${cargo.key}`}
                    value={cargo.loadingStopIndex}
                    onChange={(e) => setCargo(cargo.key, { loadingStopIndex: e.target.value })}
                    disabled={saving}
                  >
                    <option value="">— Automatisch —</option>
                    {stops.map((stop, stopIndex) =>
                      stop.stopType === 'Loading' ? (
                        <option key={stop.key} value={stopIndex}>
                          {stopIndex + 1}. Laden — {stop.city || stop.locationName || 'stop'}
                        </option>
                      ) : null,
                    )}
                  </select>
                </FormField>
              )}
              {unloadingStopCount > 1 && (
                <FormField label="Losstop" htmlFor={`cg-unload-${cargo.key}`}>
                  <select
                    id={`cg-unload-${cargo.key}`}
                    value={cargo.unloadingStopIndex}
                    onChange={(e) => setCargo(cargo.key, { unloadingStopIndex: e.target.value })}
                    disabled={saving}
                  >
                    <option value="">— Automatisch —</option>
                    {stops.map((stop, stopIndex) =>
                      stop.stopType === 'Unloading' ? (
                        <option key={stop.key} value={stopIndex}>
                          {stopIndex + 1}. Lossen — {stop.city || stop.locationName || 'stop'}
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
              ADR-goederen
            </label>
          </div>
          <details className="tof-stop-details" open={hasDetailContent}>
            <summary>Meer details</summary>
            <div className="tof-row tof-row-4">
              <FormField label="Barcode" htmlFor={`cg-bc-${cargo.key}`}>
                <input id={`cg-bc-${cargo.key}`} value={cargo.barcode} onChange={(e) => setCargo(cargo.key, { barcode: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Gewicht per stuk (kg)" htmlFor={`cg-unitweight-${cargo.key}`}>
                <input id={`cg-unitweight-${cargo.key}`} type="number" min={0} step="0.001" value={cargo.weightPerUnitKg} onChange={(e) => setCargo(cargo.key, { weightPerUnitKg: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Paletten" htmlFor={`cg-pallets-${cargo.key}`} hint="Optioneel; commercieel aantal, los van scanbare colli.">
                <input id={`cg-pallets-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.palletCount} onChange={(e) => setCargo(cargo.key, { palletCount: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Referentie" htmlFor={`cg-ref-${cargo.key}`}>
                <input id={`cg-ref-${cargo.key}`} value={cargo.reference} onChange={(e) => setCargo(cargo.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="tof-row">
              <FormField label="Verpakkingstype" htmlFor={`cg-type-${cargo.key}`}>
                <select
                  id={`cg-type-${cargo.key}`}
                  value={cargo.unitType}
                  onChange={(e) => setCargo(cargo.key, { unitType: e.target.value as PackageUnitType | '' })}
                  disabled={saving}
                >
                  <option value="">— Niet opgegeven —</option>
                  {Object.entries(UNIT_TYPE_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              {cargo.unitType === 'Other' && (
                <FormField label="Eigen typenaam" htmlFor={`cg-typelabel-${cargo.key}`}>
                  <input id={`cg-typelabel-${cargo.key}`} value={cargo.unitTypeLabel} onChange={(e) => setCargo(cargo.key, { unitTypeLabel: e.target.value })} disabled={saving} maxLength={50} />
                </FormField>
              )}
              <FormField label="Opmerkingen" htmlFor={`cg-notes-${cargo.key}`}>
                <input id={`cg-notes-${cargo.key}`} value={cargo.notes} onChange={(e) => setCargo(cargo.key, { notes: e.target.value })} disabled={saving} maxLength={500} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField
                label="Lengte (m)"
                htmlFor={`cg-length-${cargo.key}`}
                hint={cargoDimensionsFixed(cargo) ? 'Vast volgens de eenheid.' : undefined}
              >
                <input id={`cg-length-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.lengthMeters} onChange={(e) => setCargo(cargo.key, { lengthMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField label="Breedte (m)" htmlFor={`cg-width-${cargo.key}`}>
                <input id={`cg-width-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.widthMeters} onChange={(e) => setCargo(cargo.key, { widthMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField label="Hoogte (m)" htmlFor={`cg-height-${cargo.key}`}>
                <input id={`cg-height-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.heightMeters} onChange={(e) => setCargo(cargo.key, { heightMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField
                label="Volume per stuk (m³)"
                htmlFor={`cg-volume-${cargo.key}`}
                hint={cargo.volumeIsManual ? 'Handmatige waarde.' : 'Automatisch uit L × B × H.'}
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
                  Handmatig
                </label>
              </FormField>
            </div>
            <div className="tof-row">
              {cargo.adrRequired && (
                <FormField label="ADR-details" htmlFor={`cg-adr-${cargo.key}`} hint="UN-nummer, klasse, verpakkingsgroep…">
                  <input id={`cg-adr-${cargo.key}`} value={cargo.adrDetails} onChange={(e) => setCargo(cargo.key, { adrDetails: e.target.value })} disabled={saving} maxLength={500} />
                </FormField>
              )}
              <label className="tof-checkbox">
                <input type="checkbox" checked={cargo.stackable} onChange={(e) => setCargo(cargo.key, { stackable: e.target.checked })} disabled={saving} />
                Stapelbaar
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
              Verwijderen
            </button>
          </div>
        </fieldset>
        )
      })}
    </>
  )
}
