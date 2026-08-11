import { Badge } from '../../../../components/ui/Badge'
import { Button } from '../../../../components/ui/Button'
import { FormField } from '../../../../components/ui/FormField'
import { LocationSelect } from '../../../locations/components/LocationSelect'
import { CountryCombobox } from '../../../reference/components/CountryCombobox'
import type { LocationOpeningInterval, LocationOption } from '../../../locations/types'
import { openingHoursWarning } from '../../utils/openingHours'
import { STOP_TYPE_LABELS, type StopInput } from '../../types'
import { timeRequirementBadge, type StopFormRow } from './orderFormState'

interface RouteSectionProps {
  stops: StopFormRow[]
  customerId: string
  saving: boolean
  /** Structured opening hours per referenced location (client-side, non-blocking hint). */
  locationHours: Record<string, LocationOpeningInterval[]>
  /** First validation message per field path (inline errors + aria-invalid). */
  errors: Record<string, string>
  onAddStop: (stopType: StopInput['stopType']) => void
  setStop: (key: string, patch: Partial<StopFormRow>) => void
  moveStop: (index: number, delta: number) => void
  onRemoveStop: (key: string) => void
  /** Opens the "Opnieuw overnemen van locatie" confirmation for one stop row. */
  onRequestRefresh: (key: string) => void
  /** Inline location creation; undefined when the user lacks the permission or no customer is set. */
  onQuickCreate?: (name: string) => Promise<LocationOption | null>
}

/** Route & stops section: stop list with per-stop planning inputs and advanced disclosure. */
export function RouteSection({
  stops,
  customerId,
  saving,
  locationHours,
  errors,
  onAddStop,
  setStop,
  moveStop,
  onRemoveStop,
  onRequestRefresh,
  onQuickCreate,
}: RouteSectionProps) {
  return (
    <>
      <div className="tof-stops-header">
        <h3>Stops</h3>
        <div className="tof-stops-actions">
          <Button variant="secondary" onClick={() => onAddStop('Loading')} disabled={saving}>
            + Laadstop
          </Button>
          <Button variant="secondary" onClick={() => onAddStop('Unloading')} disabled={saving}>
            + Losstop
          </Button>
        </div>
      </div>

      <div className="tof-stops-grid">
        {stops.map((stop, index) => (
          <StopRow
            key={stop.key}
            stop={stop}
            index={index}
            stopCount={stops.length}
            customerId={customerId}
            saving={saving}
            locationHours={locationHours}
            errors={errors}
            setStop={setStop}
            moveStop={moveStop}
            onRemoveStop={onRemoveStop}
            onRequestRefresh={onRequestRefresh}
            onQuickCreate={onQuickCreate}
          />
        ))}
      </div>
    </>
  )
}

interface StopRowProps {
  stop: StopFormRow
  index: number
  stopCount: number
  customerId: string
  saving: boolean
  locationHours: Record<string, LocationOpeningInterval[]>
  errors: Record<string, string>
  setStop: (key: string, patch: Partial<StopFormRow>) => void
  moveStop: (index: number, delta: number) => void
  onRemoveStop: (key: string) => void
  onRequestRefresh: (key: string) => void
  onQuickCreate?: (name: string) => Promise<LocationOption | null>
}

/** One stop card: toolbar, location-or-address, date/time, time requirement, advanced details. */
function StopRow({
  stop,
  index,
  stopCount,
  customerId,
  saving,
  locationHours,
  errors,
  setStop,
  moveStop,
  onRemoveStop,
  onRequestRefresh,
  onQuickCreate,
}: StopRowProps) {
  const isUnloading = stop.stopType === 'Unloading'
  const requirementBadge = timeRequirementBadge(stop)
  // Phase 7: immediate advisory hint when a planned time falls outside the selected
  // location's structured hours (client mirror; backend warnings are authoritative).
  const hoursHint =
    stop.locationId && stop.date
      ? ([stop.fromTime, stop.toTime]
          .filter(Boolean)
          .map((time) =>
            openingHoursWarning(
              locationHours[stop.locationId],
              stop.date,
              time,
              isUnloading ? 'lostijd' : 'laadtijd',
              stop.snapshotName || 'deze locatie',
            ),
          )
          .find((warning) => warning !== null) ?? null)
      : null

  return (
    <fieldset className="tof-stop">
      <legend>
        {index + 1}. {STOP_TYPE_LABELS[stop.stopType]}
        {requirementBadge && (
          <>
            {' '}
            <Badge tone="info">{requirementBadge}</Badge>
          </>
        )}
        {stop.appointmentRequired && (
          <>
            {' '}
            <Badge tone="warning">Afspraak</Badge>
          </>
        )}
      </legend>
      <div className="tof-stop-toolbar">
        {/* Wave 1 §12: the stop type is set by the +Laadstop/+Losstop button — a static chip
            replaces the old select (the type still round-trips in the payload). */}
        <Badge tone={isUnloading ? 'neutral' : 'info'}>{STOP_TYPE_LABELS[stop.stopType]}</Badge>
        <button type="button" className="tof-link" onClick={() => moveStop(index, -1)} disabled={saving || index === 0}>
          ↑
        </button>
        <button
          type="button"
          className="tof-link"
          onClick={() => moveStop(index, 1)}
          disabled={saving || index === stopCount - 1}
        >
          ↓
        </button>
        <button
          type="button"
          className="tof-link"
          onClick={() => setStop(stop.key, { collapsed: !stop.collapsed })}
          disabled={saving}
        >
          {stop.collapsed ? 'Uitklappen' : 'Inklappen'}
        </button>
        <button
          type="button"
          className="tof-link tof-link-danger"
          onClick={() => onRemoveStop(stop.key)}
          disabled={saving}
        >
          Verwijderen
        </button>
      </div>
      {stop.collapsed ? (
        <p className="tof-stop-summary">
          {stop.locationId
            ? `${stop.snapshotName || 'Locatie uit stamgegevens'}${stop.snapshotAddress ? ` — ${stop.snapshotAddress}` : ''}`
            : stop.city || stop.locationName || 'Nog geen adres'}
          {stop.date ? ` · ${stop.date}` : ''}
          {stop.fromTime || stop.toTime
            ? ` ${stop.fromTime || '…'}${stop.toTime ? ` – ${stop.toTime}` : ''}`
            : ''}
        </p>
      ) : (
        <>
          <div className="tof-row">
            <FormField label="Locatie (stamgegevens)" htmlFor={`st-loc-${stop.key}`}>
              <LocationSelect
                id={`st-loc-${stop.key}`}
                value={stop.locationId}
                onChange={(locationId) =>
                  // A different location invalidates the shown snapshot; the backend takes a
                  // fresh copy on save, so the pending refresh flag is also reset.
                  setStop(stop.key, { locationId, refreshSnapshot: false, snapshotName: '', snapshotAddress: '' })
                }
                customerId={customerId || undefined}
                disabled={saving}
                placeholder="Geen — adres hieronder"
                onCreateNew={onQuickCreate}
              />
            </FormField>
            <FormField label="Naam (vrij adres)" htmlFor={`st-name-${stop.key}`}>
              <input
                id={`st-name-${stop.key}`}
                value={stop.locationName}
                onChange={(e) => setStop(stop.key, { locationName: e.target.value })}
                disabled={saving || stop.locationId !== ''}
                maxLength={200}
              />
            </FormField>
          </div>
          {stop.locationId !== '' && (
            <div className="tof-snapshot-row">
              <Badge tone="info">Overgenomen van klantlocatie</Badge>
              {stop.snapshotName && (
                <span className="tof-snapshot-line">
                  {stop.snapshotName}
                  {stop.snapshotAddress ? ` — ${stop.snapshotAddress}` : ''}
                </span>
              )}
              {stop.refreshSnapshot && <Badge tone="warning">Wordt opnieuw overgenomen bij opslaan</Badge>}
            </div>
          )}
          {stop.locationId === '' && (
            <div className="tof-row tof-row-4">
              <FormField label="Adres" htmlFor={`st-addr-${stop.key}`}>
                <input id={`st-addr-${stop.key}`} value={stop.address} onChange={(e) => setStop(stop.key, { address: e.target.value })} disabled={saving} maxLength={300} />
              </FormField>
              <FormField label="Postcode" htmlFor={`st-pc-${stop.key}`}>
                <input id={`st-pc-${stop.key}`} value={stop.postalCode} onChange={(e) => setStop(stop.key, { postalCode: e.target.value })} disabled={saving} maxLength={20} />
              </FormField>
              <FormField label="Plaats" htmlFor={`st-city-${stop.key}`} required error={errors[`stops[${index}].city`]}>
                <input
                  id={`st-city-${stop.key}`}
                  value={stop.city}
                  onChange={(e) => setStop(stop.key, { city: e.target.value })}
                  disabled={saving}
                  maxLength={100}
                  aria-invalid={errors[`stops[${index}].city`] ? true : undefined}
                />
              </FormField>
              <FormField label="Land" htmlFor={`st-cc-${stop.key}`}>
                <CountryCombobox
                  id={`st-cc-${stop.key}`}
                  value={stop.countryCode || null}
                  onChange={(code) => setStop(stop.key, { countryCode: code ?? '' })}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          <div className="tof-row tof-row-4">
            <FormField label={stop.stopType === 'Loading' ? 'Laaddatum' : 'Losdatum'} htmlFor={`st-date-${stop.key}`}>
              <input id={`st-date-${stop.key}`} type="date" value={stop.date} onChange={(e) => setStop(stop.key, { date: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Van" htmlFor={`st-fromtime-${stop.key}`} hint="Optioneel.">
              <input id={`st-fromtime-${stop.key}`} type="time" value={stop.fromTime} onChange={(e) => setStop(stop.key, { fromTime: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Tot" htmlFor={`st-totime-${stop.key}`} hint="Optioneel.">
              <input id={`st-totime-${stop.key}`} type="time" value={stop.toTime} onChange={(e) => setStop(stop.key, { toTime: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Referentie" htmlFor={`st-ref-${stop.key}`}>
              <input id={`st-ref-${stop.key}`} value={stop.reference} onChange={(e) => setStop(stop.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
            </FormField>
          </div>
          {hoursHint && (
            <p className="tof-hours-warning" role="note">
              ⚠ {hoursHint}
            </p>
          )}
          <div className="tof-row tof-row-4">
            <FormField label="Tijdseis" htmlFor={`st-timereq-${stop.key}`}>
              <select
                id={`st-timereq-${stop.key}`}
                value={stop.timeRequirement}
                onChange={(e) => setStop(stop.key, { timeRequirement: e.target.value as StopFormRow['timeRequirement'] })}
                disabled={saving}
              >
                <option value="">Geen specifieke eis</option>
                <option value="Before">{isUnloading ? 'Leveren vóór' : 'Laden vóór'}</option>
                <option value="After">{isUnloading ? 'Niet leveren vóór' : 'Niet laden vóór'}</option>
                <option value="Window">{isUnloading ? 'Exact levervenster' : 'Exact laadvenster'}</option>
              </select>
            </FormField>
            {(stop.timeRequirement === 'After' || stop.timeRequirement === 'Window') && (
              <FormField
                label={stop.timeRequirement === 'Window' ? 'Venster van' : 'Niet vóór'}
                htmlFor={`st-timereqfrom-${stop.key}`}
                error={errors[`stops[${index}].timeReqFrom`]}
              >
                <input
                  id={`st-timereqfrom-${stop.key}`}
                  type="time"
                  value={stop.timeReqFrom}
                  onChange={(e) => setStop(stop.key, { timeReqFrom: e.target.value })}
                  disabled={saving}
                  aria-invalid={errors[`stops[${index}].timeReqFrom`] ? true : undefined}
                />
              </FormField>
            )}
            {(stop.timeRequirement === 'Before' || stop.timeRequirement === 'Window') && (
              <FormField
                label={stop.timeRequirement === 'Window' ? 'Venster tot' : 'Vóór'}
                htmlFor={`st-timereqto-${stop.key}`}
                error={errors[`stops[${index}].timeReqTo`]}
              >
                <input
                  id={`st-timereqto-${stop.key}`}
                  type="time"
                  value={stop.timeReqTo}
                  onChange={(e) => setStop(stop.key, { timeReqTo: e.target.value })}
                  disabled={saving}
                  aria-invalid={errors[`stops[${index}].timeReqTo`] ? true : undefined}
                />
              </FormField>
            )}
          </div>
          <div className="tof-row">
            <FormField label="Instructies" htmlFor={`st-instr-${stop.key}`}>
              <input id={`st-instr-${stop.key}`} value={stop.instructions} onChange={(e) => setStop(stop.key, { instructions: e.target.value })} disabled={saving} maxLength={2000} />
            </FormField>
          </div>
          {/* Wave 1 §12: everything beyond the 7 default controls lives behind "Geavanceerd" —
              hidden from the default flow, fully functional for round-trip. Auto-open when any
              advanced field carries a value so existing data never disappears silently. */}
          <details
            className="tof-stop-extended"
            open={Boolean(
              stop.requestedFrom || stop.requestedTo || stop.confirmedFrom || stop.confirmedTo ||
              stop.earliestAllowed || stop.latestAllowed ||
              stop.appointmentRequired || stop.appointmentReference ||
              stop.includedTimeMinutesOverride || stop.refreshSnapshot ||
              stop.accessInstructions || stop.loadingInstructions || stop.unloadingInstructions,
            )}
          >
            <summary>Geavanceerd</summary>
            <div className="tof-row tof-row-4">
              <FormField label="Gevraagd van" htmlFor={`st-reqfrom-${stop.key}`} hint="Venster gevraagd door de klant">
                <input id={`st-reqfrom-${stop.key}`} type="datetime-local" value={stop.requestedFrom} onChange={(e) => setStop(stop.key, { requestedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Gevraagd tot" htmlFor={`st-reqto-${stop.key}`}>
                <input id={`st-reqto-${stop.key}`} type="datetime-local" value={stop.requestedTo} onChange={(e) => setStop(stop.key, { requestedTo: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Bevestigd van" htmlFor={`st-conffrom-${stop.key}`} hint="Venster bevestigd aan de klant">
                <input id={`st-conffrom-${stop.key}`} type="datetime-local" value={stop.confirmedFrom} onChange={(e) => setStop(stop.key, { confirmedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Bevestigd tot" htmlFor={`st-confto-${stop.key}`}>
                <input id={`st-confto-${stop.key}`} type="datetime-local" value={stop.confirmedTo} onChange={(e) => setStop(stop.key, { confirmedTo: e.target.value })} disabled={saving} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField label="Vroegst toegelaten" htmlFor={`st-earliest-${stop.key}`}>
                <input id={`st-earliest-${stop.key}`} type="datetime-local" value={stop.earliestAllowed} onChange={(e) => setStop(stop.key, { earliestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Uiterste tijdstip" htmlFor={`st-latest-${stop.key}`} hint="Na dit tijdstip is een reden voor late aankomst verplicht">
                <input id={`st-latest-${stop.key}`} type="datetime-local" value={stop.latestAllowed} onChange={(e) => setStop(stop.key, { latestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <FormField
                label="Inbegrepen tijd (minuten)"
                htmlFor={`st-inclmin-${stop.key}`}
                hint="Afwijking voor deze stop; gaat vóór de orderafwijking en de contractwaarde."
              >
                <input id={`st-inclmin-${stop.key}`} type="number" min={0} value={stop.includedTimeMinutesOverride} onChange={(e) => setStop(stop.key, { includedTimeMinutesOverride: e.target.value })} disabled={saving} />
              </FormField>
            </div>
            <div className="tof-row">
              <label className="tof-checkbox">
                <input type="checkbox" checked={stop.appointmentRequired} onChange={(e) => setStop(stop.key, { appointmentRequired: e.target.checked })} disabled={saving} />
                Afspraak verplicht
              </label>
              <FormField label="Afspraakreferentie" htmlFor={`st-appref-${stop.key}`}>
                <input id={`st-appref-${stop.key}`} value={stop.appointmentReference} onChange={(e) => setStop(stop.key, { appointmentReference: e.target.value })} disabled={saving} maxLength={100} placeholder="bv. slotnummer" />
              </FormField>
            </div>
            <div className="tof-row">
              <FormField label="Toegangsinstructies" htmlFor={`st-access-${stop.key}`}>
                <input id={`st-access-${stop.key}`} value={stop.accessInstructions} onChange={(e) => setStop(stop.key, { accessInstructions: e.target.value })} disabled={saving} maxLength={2000} />
              </FormField>
              {stop.stopType === 'Loading' ? (
                <FormField label="Laadinstructies" htmlFor={`st-loadinstr-${stop.key}`}>
                  <input id={`st-loadinstr-${stop.key}`} value={stop.loadingInstructions} onChange={(e) => setStop(stop.key, { loadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              ) : (
                <FormField label="Losinstructies" htmlFor={`st-unloadinstr-${stop.key}`}>
                  <input id={`st-unloadinstr-${stop.key}`} value={stop.unloadingInstructions} onChange={(e) => setStop(stop.key, { unloadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              )}
            </div>
            {stop.locationId !== '' && stop.id && !stop.refreshSnapshot && (
              <div className="tof-stop-toolbar">
                <button
                  type="button"
                  className="tof-link"
                  onClick={() => onRequestRefresh(stop.key)}
                  disabled={saving}
                >
                  Opnieuw overnemen van locatie
                </button>
              </div>
            )}
          </details>
        </>
      )}
    </fieldset>
  )
}
