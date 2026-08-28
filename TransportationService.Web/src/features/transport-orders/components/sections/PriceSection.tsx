import { Badge } from '../../../../components/ui/Badge'
import { formatCurrency, formatQuantity } from '../../../../utils/numbers'
import { formatDate } from '../../../../utils/dates'
import { FormField } from '../../../../components/ui/FormField'
import type { PriceCalculationResult } from '../../../tarification/api/pricingApi'

interface PriceSectionProps {
  saving: boolean
  orderDate: string
  preview: PriceCalculationResult | null
  canOverridePrice: boolean
  pricingSource: 'Contract' | 'OneOff'
  setPricingSource: (value: 'Contract' | 'OneOff') => void
  oneOffFixedAmount: string
  setOneOffFixedAmount: (value: string) => void
  oneOffTimeMode: 'none' | 'separate' | 'combined'
  setOneOffTimeMode: (value: 'none' | 'separate' | 'combined') => void
  oneOffIncludedLoadingMinutes: string
  setOneOffIncludedLoadingMinutes: (value: string) => void
  oneOffIncludedUnloadingMinutes: string
  setOneOffIncludedUnloadingMinutes: (value: string) => void
  oneOffIncludedCombinedMinutes: string
  setOneOffIncludedCombinedMinutes: (value: string) => void
  oneOffExtraHourlyRate: string
  setOneOffExtraHourlyRate: (value: string) => void
  oneOffNotes: string
  setOneOffNotes: (value: string) => void
  priceIsManual: boolean
  setPriceIsManual: (value: boolean) => void
  priceOverrideReason: string
  setPriceOverrideReason: (value: string) => void
  agreedPrice: string
  setAgreedPrice: (value: string) => void
  /** First validation message per field path (inline errors + aria-invalid). */
  errors: Record<string, string>
}

/** Prijs section: pricing source (contract/one-off), live breakdown, manual override. */
export function PriceSection({
  saving,
  orderDate,
  preview,
  canOverridePrice,
  pricingSource,
  setPricingSource,
  oneOffFixedAmount,
  setOneOffFixedAmount,
  oneOffTimeMode,
  setOneOffTimeMode,
  oneOffIncludedLoadingMinutes,
  setOneOffIncludedLoadingMinutes,
  oneOffIncludedUnloadingMinutes,
  setOneOffIncludedUnloadingMinutes,
  oneOffIncludedCombinedMinutes,
  setOneOffIncludedCombinedMinutes,
  oneOffExtraHourlyRate,
  setOneOffExtraHourlyRate,
  oneOffNotes,
  setOneOffNotes,
  priceIsManual,
  setPriceIsManual,
  priceOverrideReason,
  setPriceOverrideReason,
  agreedPrice,
  setAgreedPrice,
  errors,
}: PriceSectionProps) {
  return (
    <>
      <div className="tof-row">
        <label className="tof-checkbox">
          <input
            type="radio"
            name="to-pricing-source"
            checked={pricingSource === 'Contract'}
            onChange={() => setPricingSource('Contract')}
            disabled={saving}
          />
          Klantcontract
        </label>
        <label className="tof-checkbox">
          <input
            type="radio"
            name="to-pricing-source"
            checked={pricingSource === 'OneOff'}
            onChange={() => setPricingSource('OneOff')}
            disabled={saving}
          />
          Eenmalige prijsafspraak
        </label>
      </div>

      {pricingSource === 'OneOff' && (
        <fieldset className="tof-stop">
          <legend>Eenmalige prijsafspraak</legend>
          <div className="tof-row">
            <FormField label="Vast bedrag (€)" htmlFor="to-oneoff-amount" required error={errors.oneOffFixedAmount}>
              <input
                id="to-oneoff-amount"
                type="number"
                min={0}
                step="0.01"
                value={oneOffFixedAmount}
                onChange={(e) => setOneOffFixedAmount(e.target.value)}
                disabled={saving}
                aria-invalid={errors.oneOffFixedAmount ? true : undefined}
              />
            </FormField>
          </div>
          <p className="customer-form-muted">Inbegrepen tijd</p>
          <div className="tof-row">
            <label className="tof-checkbox">
              <input
                type="radio"
                name="to-oneoff-time-mode"
                checked={oneOffTimeMode === 'none'}
                onChange={() => setOneOffTimeMode('none')}
                disabled={saving}
              />
              Geen
            </label>
            <label className="tof-checkbox">
              <input
                type="radio"
                name="to-oneoff-time-mode"
                checked={oneOffTimeMode === 'separate'}
                onChange={() => setOneOffTimeMode('separate')}
                disabled={saving}
              />
              Per activiteit
            </label>
            <label className="tof-checkbox">
              <input
                type="radio"
                name="to-oneoff-time-mode"
                checked={oneOffTimeMode === 'combined'}
                onChange={() => setOneOffTimeMode('combined')}
                disabled={saving}
              />
              Gecombineerd
            </label>
          </div>
          {oneOffTimeMode === 'separate' && (
            <div className="tof-row">
              <FormField label="Laden (min)" htmlFor="to-oneoff-loading">
                <input
                  id="to-oneoff-loading"
                  type="number"
                  min={0}
                  value={oneOffIncludedLoadingMinutes}
                  onChange={(e) => setOneOffIncludedLoadingMinutes(e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Lossen (min)" htmlFor="to-oneoff-unloading">
                <input
                  id="to-oneoff-unloading"
                  type="number"
                  min={0}
                  value={oneOffIncludedUnloadingMinutes}
                  onChange={(e) => setOneOffIncludedUnloadingMinutes(e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          {oneOffTimeMode === 'combined' && (
            <div className="tof-row">
              <FormField label="Totaal (min)" htmlFor="to-oneoff-combined">
                <input
                  id="to-oneoff-combined"
                  type="number"
                  min={0}
                  value={oneOffIncludedCombinedMinutes}
                  onChange={(e) => setOneOffIncludedCombinedMinutes(e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          {oneOffTimeMode !== 'none' && (
            <div className="tof-row">
              <FormField label="Uurtarief extra tijd (€/u)" htmlFor="to-oneoff-rate">
                <input
                  id="to-oneoff-rate"
                  type="number"
                  min={0}
                  step="0.01"
                  value={oneOffExtraHourlyRate}
                  onChange={(e) => setOneOffExtraHourlyRate(e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          <FormField label="Notities" htmlFor="to-oneoff-notes" hint="Bv. 'Afgesproken via telefoon met dhr. Peeters'.">
            <textarea
              id="to-oneoff-notes"
              rows={2}
              value={oneOffNotes}
              onChange={(e) => setOneOffNotes(e.target.value)}
              disabled={saving}
              maxLength={500}
            />
          </FormField>
        </fieldset>
      )}

      {preview ? (
        <div className="tof-price-breakdown">
          <p className="customer-form-muted">
            Tariefdatum: {formatDate(preview.tariffDate ?? orderDate) || '—'}
            {preview.zoneName ? ` · Zone: ${preview.zoneName} (${preview.zoneCode})` : ''}
            {(() => {
              const agreementNames = [...new Set(preview.lines.map((l) => l.agreementName).filter(Boolean))]
              return agreementNames.length > 0 ? ` · Tarief: ${agreementNames.join(', ')}` : ''
            })()}
          </p>
          {preview.configurationError && (
            <div className="issued-items-form-error" role="alert">
              {preview.configurationError}
            </div>
          )}
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Omschrijving</th>
                <th>Bron</th>
                <th className="tof-price-amount">Bedrag</th>
              </tr>
            </thead>
            <tbody>
              {preview.lines.filter((line) => !line.proposed).map((line, index) => (
                <tr key={index} className={line.informational ? 'tof-price-informational' : undefined}>
                  <td>
                    {line.label}
                    {(line.billableQuantity ?? null) !== null && (line.actualQuantity ?? null) !== null
                      && line.billableQuantity !== line.actualQuantity && (
                      <span className="customer-form-muted">
                        {' '}
                        ({line.actualQuantity} werkelijk / {line.billableQuantity} factureerbaar)
                      </span>
                    )}
                  </td>
                  <td>{line.source}</td>
                  <td className="tof-price-amount">{formatCurrency(line.amount)}</td>
                </tr>
              ))}
              {preview.lines.some((line) => line.proposed) && (
                <>
                  <tr>
                    <th colSpan={2}>Subtotaal</th>
                    <th className="tof-price-amount">{formatCurrency(preview.total)}</th>
                  </tr>
                  {preview.lines.filter((line) => line.proposed).map((line, index) => (
                    <tr key={`proposed-${index}`} className="tof-price-proposed">
                      <td>
                        {line.label} <Badge tone="warning">VOORSTEL</Badge>
                      </td>
                      <td>{line.source}</td>
                      <td className="tof-price-amount">{formatCurrency(line.amount)}</td>
                    </tr>
                  ))}
                </>
              )}
            </tbody>
            <tfoot>
              <tr>
                <th>
                  Totaal
                  {preview.zoneCode ? ` (zone ${preview.zoneCode})` : ''}
                </th>
                <th />
                <th className="tof-price-amount">{formatCurrency(preview.total)}</th>
              </tr>
              {preview.lines.some((line) => line.proposed) && (
                <tr>
                  <th>Totaal incl. voorstellen</th>
                  <th />
                  <th className="tof-price-amount">{formatCurrency(preview.totalWithProposed)}</th>
                </tr>
              )}
            </tfoot>
          </table>
          {(preview.coverage ?? []).some((c) => c.status !== 'Full') && (
            <div className="to-coverage-warning" role="alert">
              <strong>Niet alle goederen zijn geprijsd.</strong>
              <ul>
                {(preview.coverage ?? [])
                  .filter((c) => c.status !== 'Full')
                  .map((c, index) => (
                    <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                      {formatQuantity(c.quantity)} {c.unitLabel}:{' '}
                      {(c.reason ?? 'geen passend basistarief').toLowerCase()}
                      {c.servicesAmount > 0 && ` — alleen diensten (${formatCurrency(c.servicesAmount)}), geen transportprijs`}
                    </li>
                  ))}
              </ul>
            </div>
          )}
          {preview.requiresManualPrice && !preview.configurationError && (
            <div className="tof-customer-requirements" role="note">
              <p>Geen geldig tarief gevonden voor deze order — vul een handmatige prijs in of configureer tarieven.</p>
              {preview.diagnostics && preview.diagnostics.length > 0 && (
                <ul>
                  {preview.diagnostics.map((line, index) => (
                    <li key={index}>{line}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      ) : (
        <p className="placeholder-text">
          Vul klant, aantal en eenheid in om de prijs automatisch te berekenen. Zonder geconfigureerde tarieven blijft
          een handmatige prijs mogelijk.
        </p>
      )}

      {canOverridePrice && (
        <label className="tof-checkbox">
          <input type="checkbox" checked={priceIsManual} onChange={(e) => setPriceIsManual(e.target.checked)} disabled={saving} />
          Handmatige prijs (overschrijft de berekende prijs)
        </label>
      )}
      {(priceIsManual || (!preview && !canOverridePrice) || (!preview && canOverridePrice)) && (
        <div className="tof-row">
          <FormField
            label={priceIsManual ? 'Handmatige prijs (€)' : 'Afgesproken prijs (€)'}
            htmlFor="to-price"
            hint={priceIsManual ? undefined : 'Gebruikt zolang er geen berekende prijs is.'}
          >
            <input id="to-price" type="number" min={0} step="0.01" value={agreedPrice} onChange={(e) => setAgreedPrice(e.target.value)} disabled={saving} />
          </FormField>
          {priceIsManual && (
            <FormField
              label="Reden"
              htmlFor="to-price-reason"
              required
              hint="Verplicht bij het overschrijven van de berekende prijs."
              error={errors.priceOverrideReason}
            >
              <input
                id="to-price-reason"
                value={priceOverrideReason}
                onChange={(e) => setPriceOverrideReason(e.target.value)}
                disabled={saving}
                maxLength={300}
                aria-invalid={errors.priceOverrideReason ? true : undefined}
              />
            </FormField>
          )}
        </div>
      )}
    </>
  )
}
