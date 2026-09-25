import { Badge } from '../../../../components/ui/Badge'
import { Button } from '../../../../components/ui/Button'
import { FormField } from '../../../../components/ui/FormField'
import { TimeInput } from '../../../../components/ui/TimeInput'
import { useLocale } from '../../../../i18n/localeContext'
import type { LocationOpeningInterval, LocationOption } from '../../../locations/types'
import { openingHoursWarning } from '../../utils/openingHours'
import { STOP_TYPE_LABELS, type StopInput } from '../../types'
import { timeRequirementBadge, type StopFormRow } from './orderFormState'
import { StopAddressFields } from './StopAddressFields'
// The stop editor carries its own layout: the intake page never loads the order-form chunk.
import '../transport-order-form.css'
import './route-section.css'

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
  /** Opens the "Adres opnieuw overnemen" confirmation for one stop row. */
  onRequestRefresh: (key: string) => void
  /** Inline location creation; undefined when the user lacks the permission or no customer is set. */
  onQuickCreate?: (name: string) => Promise<LocationOption | null>
  /**
   * Intake mode (dossier quick intake): hides the per-stop toolbar (move/collapse), the per-stop
   * reference and the instructions row — a 1-3 stop intake needs none of that chrome. The
   * advanced disclosure stays fully functional.
   */
  compact?: boolean
  /** Hides the +Laadstop/+Losstop header (the intake supplies its own add action). */
  hideHeader?: boolean
  /** compact only: whether this stop offers a remove action (e.g. extra unload addresses). */
  canRemoveStop?: (stop: StopFormRow, index: number) => boolean
  /**
   * Dossier work-sheet mode (UX-sprint 2026-09-09): every stop is always expanded, the toolbar
   * offers move up/down + remove only, the reference stays visible next to date/time and the
   * customer-visible instructions move into the advanced disclosure. Location, date and time
   * window are the core operational fields a planner fills in directly on the dossier.
   */
  sheet?: boolean
  /**
   * D3: offer "Adres ook bewaren in klantenbestand" on a new/deviating address. Needs
   * locations.create and a known customer; without it the option is simply not shown.
   */
  canSaveToAddressBook?: boolean
  /**
   * D2: planned duration (decimal hours) of the on-site job — only used to explain the automatic
   * end of a SITE stop. The arithmetic itself lives in the host's `setStop` (`applyStopPatch`).
   */
  siteDurationHours?: number | null
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
  compact = false,
  hideHeader = false,
  canRemoveStop,
  sheet = false,
  canSaveToAddressBook = false,
  siteDurationHours = null,
}: RouteSectionProps) {
  const { t } = useLocale()
  // D2: an on-site job is ONE work-site stop — no loading/unloading stops can be added to it.
  const siteJob = stops.some((stop) => stop.stopType === 'Site')
  return (
    <>
      {!hideHeader && !siteJob && (
        <div className="tof-stops-header">
          <h3>{t('transportOrders.route.stopsTitle')}</h3>
          <div className="tof-stops-actions">
            <Button variant="secondary" onClick={() => onAddStop('Loading')} disabled={saving}>
              {t('transportOrders.route.addLoading')}
            </Button>
            <Button variant="secondary" onClick={() => onAddStop('Unloading')} disabled={saving}>
              {t('transportOrders.route.addUnloading')}
            </Button>
          </div>
        </div>
      )}

      <div className={siteJob ? 'tof-stops-grid tof-stops-grid-site' : 'tof-stops-grid'}>
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
            compact={compact}
            sheet={sheet}
            removable={!compact || (canRemoveStop?.(stop, index) ?? false)}
            canSaveToAddressBook={canSaveToAddressBook}
            siteDurationHours={siteDurationHours}
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
  compact: boolean
  sheet: boolean
  removable: boolean
  canSaveToAddressBook: boolean
  siteDurationHours: number | null
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
  compact,
  sheet,
  removable,
  canSaveToAddressBook,
  siteDurationHours,
}: StopRowProps) {
  const { t } = useLocale()
  const isUnloading = stop.stopType === 'Unloading'
  // D2: the work site of an on-site lifting job — one stop, no place in a route, no time requirement.
  const isSite = stop.stopType === 'Site'
  const requirementBadge = timeRequirementBadge(stop)
  // Phase 7: immediate advisory hint when a planned time falls outside the selected
  // location's structured hours (client mirror; backend warnings are authoritative).
  const hoursHint =
    !isSite && stop.locationId && stop.date
      ? ([stop.fromTime, stop.toTime]
          .filter(Boolean)
          .map((time) =>
            openingHoursWarning(
              locationHours[stop.locationId],
              stop.date,
              time,
              isUnloading ? 'unloading' : 'loading',
              stop.snapshotName || t('transportOrders.openingHours.thisLocation'),
            ),
          )
          .find((warning) => warning !== null) ?? null)
      : null

  return (
    <fieldset className="tof-stop">
      <legend>
        {isSite ? t('stopEditor.site.legend') : `${index + 1}. ${t(STOP_TYPE_LABELS[stop.stopType])}`}
        {requirementBadge && (
          <>
            {' '}
            <Badge tone="info">{requirementBadge}</Badge>
          </>
        )}
        {stop.appointmentRequired && (
          <>
            {' '}
            <Badge tone="warning">{t('transportOrders.badges.appointment')}</Badge>
          </>
        )}
      </legend>
      {isSite ? null : sheet ? (
        <div className="tof-stop-toolbar">
          <button
            type="button"
            className="tof-link"
            onClick={() => moveStop(index, -1)}
            disabled={saving || index === 0}
            aria-label={t('transportOrders.route.moveUp', { number: index + 1 })}
          >
            ↑
          </button>
          <button
            type="button"
            className="tof-link"
            onClick={() => moveStop(index, 1)}
            disabled={saving || index === stopCount - 1}
            aria-label={t('transportOrders.route.moveDown', { number: index + 1 })}
          >
            ↓
          </button>
          <button
            type="button"
            className="tof-link tof-link-danger"
            onClick={() => onRemoveStop(stop.key)}
            disabled={saving}
            aria-label={t('transportOrders.route.removeStop', { number: index + 1 })}
          >
            {t('ui.actions.delete')}
          </button>
        </div>
      ) : compact ? (
        removable && (
          <div className="tof-stop-toolbar">
            <button
              type="button"
              className="tof-link tof-link-danger"
              onClick={() => onRemoveStop(stop.key)}
              disabled={saving}
            >
              {t('ui.actions.delete')}
            </button>
          </div>
        )
      ) : (
        <div className="tof-stop-toolbar">
          {/* Wave 1 §12: the stop type is set by the +Laadstop/+Losstop button — a static chip
              replaces the old select (the type still round-trips in the payload). */}
          <Badge tone={isUnloading ? 'neutral' : 'info'}>{t(STOP_TYPE_LABELS[stop.stopType])}</Badge>
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
            {stop.collapsed ? t('transportOrders.route.expand') : t('transportOrders.route.collapse')}
          </button>
          <button
            type="button"
            className="tof-link tof-link-danger"
            onClick={() => onRemoveStop(stop.key)}
            disabled={saving}
          >
            {t('ui.actions.delete')}
          </button>
        </div>
      )}
      {!compact && !sheet && stop.collapsed ? (
        <p className="tof-stop-summary">
          {stop.locationId
            ? `${stop.snapshotName || t('transportOrders.route.masterLocationFallback')}${stop.snapshotAddress ? ` — ${stop.snapshotAddress}` : ''}`
            : stop.city || stop.locationName || t('transportOrders.route.noAddress')}
          {stop.date ? ` · ${stop.date}` : ''}
          {stop.fromTime || stop.toTime
            ? ` ${stop.fromTime || '…'}${stop.toTime ? ` – ${stop.toTime}` : ''}`
            : ''}
        </p>
      ) : (
        <>
          <StopAddressFields
            stop={stop}
            index={index}
            customerId={customerId}
            saving={saving}
            errors={errors}
            setStop={setStop}
            onRequestRefresh={onRequestRefresh}
            onQuickCreate={onQuickCreate}
            canSaveToAddressBook={canSaveToAddressBook}
          />
          <div className="tof-row">
            <FormField
              label={
                isSite
                  ? t('stopEditor.site.date')
                  : stop.stopType === 'Loading' ? t('transportOrders.route.loadDate') : t('transportOrders.route.unloadDate')
              }
              htmlFor={`st-date-${stop.key}`}
            >
              <input id={`st-date-${stop.key}`} type="date" value={stop.date} onChange={(e) => setStop(stop.key, { date: e.target.value })} disabled={saving} />
            </FormField>
            {!compact && !isSite && (
              <FormField label={t('transportOrders.route.reference')} htmlFor={`st-ref-${stop.key}`}>
                <input id={`st-ref-${stop.key}`} value={stop.reference} onChange={(e) => setStop(stop.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
            )}
            {isSite && (stop.plannedToIsManual || (stop.toDate !== '' && stop.toDate !== stop.date)) && (
              <FormField label={t('stopEditor.site.endDate')} htmlFor={`st-todate-${stop.key}`}>
                <input
                  id={`st-todate-${stop.key}`}
                  type="date"
                  value={stop.toDate || stop.date}
                  onChange={(e) => setStop(stop.key, { toDate: e.target.value })}
                  disabled={saving}
                />
              </FormField>
            )}
          </div>
          {/* Van/Tot: always two equal columns with the label above — also on a phone, where two
              24h fields fit side by side. Both stay optional. */}
          <div className="tof-time-pair">
            <FormField label={t('transportOrders.route.from')} htmlFor={`st-fromtime-${stop.key}`} hint={t('transportOrders.route.optional')}>
              <TimeInput id={`st-fromtime-${stop.key}`} value={stop.fromTime} onChange={(value) => setStop(stop.key, { fromTime: value })} disabled={saving} />
            </FormField>
            <FormField label={t('transportOrders.route.to')} htmlFor={`st-totime-${stop.key}`} hint={t('transportOrders.route.optional')}>
              <TimeInput id={`st-totime-${stop.key}`} value={stop.toTime} onChange={(value) => setStop(stop.key, { toTime: value })} disabled={saving} />
            </FormField>
          </div>
          {isSite && (
            <p className="tof-site-end" role="note">
              {stop.plannedToIsManual ? (
                <>
                  {t('stopEditor.site.endManual')}{' '}
                  <button type="button" className="tof-link" onClick={() => setStop(stop.key, { plannedToIsManual: false })} disabled={saving}>
                    {t('stopEditor.site.endAuto')}
                  </button>
                </>
              ) : siteDurationHours !== null && siteDurationHours > 0 ? (
                t('stopEditor.site.endFollows', { hours: String(siteDurationHours).replace('.', ',') })
              ) : (
                t('stopEditor.site.endNoDuration')
              )}
              {stop.toTime && stop.toDate !== '' && stop.toDate !== stop.date
                ? ` ${t('stopEditor.site.endNextDay', { date: stop.toDate.split('-').reverse().join('-') })}`
                : ''}
            </p>
          )}
          {hoursHint && (
            <p className="tof-hours-warning" role="note">
              ⚠ {hoursHint}
            </p>
          )}
          {!isSite && (
          <div className="tof-row tof-row-4">
            <FormField label={t('transportOrders.route.timeReq')} htmlFor={`st-timereq-${stop.key}`}>
              <select
                id={`st-timereq-${stop.key}`}
                value={stop.timeRequirement}
                onChange={(e) => setStop(stop.key, { timeRequirement: e.target.value as StopFormRow['timeRequirement'] })}
                disabled={saving}
              >
                <option value="">{t('transportOrders.route.noTimeReq')}</option>
                <option value="Before">
                  {isUnloading ? t('transportOrders.route.deliverBefore') : t('transportOrders.route.loadBefore')}
                </option>
                <option value="After">
                  {isUnloading ? t('transportOrders.route.notDeliverBefore') : t('transportOrders.route.notLoadBefore')}
                </option>
                <option value="Window">
                  {isUnloading ? t('transportOrders.route.exactDeliveryWindow') : t('transportOrders.route.exactLoadingWindow')}
                </option>
              </select>
            </FormField>
            {(stop.timeRequirement === 'After' || stop.timeRequirement === 'Window') && (
              <FormField
                label={stop.timeRequirement === 'Window' ? t('transportOrders.route.windowFrom') : t('transportOrders.route.notBefore')}
                htmlFor={`st-timereqfrom-${stop.key}`}
                error={errors[`stops[${index}].timeReqFrom`]}
              >
                <TimeInput
                  id={`st-timereqfrom-${stop.key}`}
                  value={stop.timeReqFrom}
                  onChange={(value) => setStop(stop.key, { timeReqFrom: value })}
                  disabled={saving}
                  aria-invalid={errors[`stops[${index}].timeReqFrom`] ? true : undefined}
                />
              </FormField>
            )}
            {(stop.timeRequirement === 'Before' || stop.timeRequirement === 'Window') && (
              <FormField
                label={stop.timeRequirement === 'Window' ? t('transportOrders.route.windowTo') : t('transportOrders.route.before')}
                htmlFor={`st-timereqto-${stop.key}`}
                error={errors[`stops[${index}].timeReqTo`]}
              >
                <TimeInput
                  id={`st-timereqto-${stop.key}`}
                  value={stop.timeReqTo}
                  onChange={(value) => setStop(stop.key, { timeReqTo: value })}
                  disabled={saving}
                  aria-invalid={errors[`stops[${index}].timeReqTo`] ? true : undefined}
                />
              </FormField>
            )}
          </div>
          )}
          {!compact && !sheet && (
            <div className="tof-row">
              {/* Wave 1 fix B (B4): this column is a SHARED write surface — the portal writes it at
                  intake and a planner edits the same value here, and PortalStopDto echoes it back to
                  the customer. The hint says so, because "internal remark typed into a
                  customer-visible field" is the failure mode; internal handling notes belong in the
                  access/loading/unloading instructions, which are never exposed. */}
              <FormField
                label={t('transportOrders.route.instructions')}
                htmlFor={`st-instr-${stop.key}`}
                hint={t('transportOrders.route.instructionsHint')}
              >
                <input id={`st-instr-${stop.key}`} value={stop.instructions} onChange={(e) => setStop(stop.key, { instructions: e.target.value })} disabled={saving} maxLength={2000} />
              </FormField>
            </div>
          )}
          {/* Wave 1 §12: everything beyond the 7 default controls lives behind "Geavanceerd" —
              hidden from the default flow, fully functional for round-trip. Auto-open when any
              advanced field carries a value so existing data never disappears silently. */}
          <details
            className="tof-stop-extended"
            open={Boolean(
              stop.requestedFrom || stop.requestedTo || stop.confirmedFrom || stop.confirmedTo ||
              stop.earliestAllowed || stop.latestAllowed ||
              // Sheet mode: the AFSPRAAK badge already surfaces the flag; only a reference opens the disclosure.
              (!sheet && stop.appointmentRequired) || stop.appointmentReference ||
              stop.includedTimeMinutesOverride || stop.refreshSnapshot ||
              stop.accessInstructions || stop.loadingInstructions || stop.unloadingInstructions ||
              (sheet && stop.instructions),
            )}
          >
            <summary>{t('transportOrders.route.advanced')}</summary>
            <div className="tof-row tof-row-4">
              <FormField label={t('transportOrders.route.requestedFrom')} htmlFor={`st-reqfrom-${stop.key}`} hint={t('transportOrders.route.requestedFromHint')}>
                <input id={`st-reqfrom-${stop.key}`} type="datetime-local" value={stop.requestedFrom} onChange={(e) => setStop(stop.key, { requestedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label={t('transportOrders.route.requestedTo')} htmlFor={`st-reqto-${stop.key}`}>
                <input id={`st-reqto-${stop.key}`} type="datetime-local" value={stop.requestedTo} onChange={(e) => setStop(stop.key, { requestedTo: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label={t('transportOrders.route.confirmedFrom')} htmlFor={`st-conffrom-${stop.key}`} hint={t('transportOrders.route.confirmedFromHint')}>
                <input id={`st-conffrom-${stop.key}`} type="datetime-local" value={stop.confirmedFrom} onChange={(e) => setStop(stop.key, { confirmedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label={t('transportOrders.route.confirmedTo')} htmlFor={`st-confto-${stop.key}`}>
                <input id={`st-confto-${stop.key}`} type="datetime-local" value={stop.confirmedTo} onChange={(e) => setStop(stop.key, { confirmedTo: e.target.value })} disabled={saving} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField label={t('transportOrders.route.earliest')} htmlFor={`st-earliest-${stop.key}`}>
                <input id={`st-earliest-${stop.key}`} type="datetime-local" value={stop.earliestAllowed} onChange={(e) => setStop(stop.key, { earliestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label={t('transportOrders.route.latest')} htmlFor={`st-latest-${stop.key}`} hint={t('transportOrders.route.latestHint')}>
                <input id={`st-latest-${stop.key}`} type="datetime-local" value={stop.latestAllowed} onChange={(e) => setStop(stop.key, { latestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <FormField
                label={t('transportOrders.route.includedTime')}
                htmlFor={`st-inclmin-${stop.key}`}
                hint={t('transportOrders.route.includedTimeHint')}
              >
                <input id={`st-inclmin-${stop.key}`} type="number" min={0} value={stop.includedTimeMinutesOverride} onChange={(e) => setStop(stop.key, { includedTimeMinutesOverride: e.target.value })} disabled={saving} />
              </FormField>
            </div>
            <div className="tof-row">
              <label className="tof-checkbox">
                <input type="checkbox" checked={stop.appointmentRequired} onChange={(e) => setStop(stop.key, { appointmentRequired: e.target.checked })} disabled={saving} />
                {t('transportOrders.route.appointmentRequired')}
              </label>
              <FormField label={t('transportOrders.route.appointmentRef')} htmlFor={`st-appref-${stop.key}`}>
                <input id={`st-appref-${stop.key}`} value={stop.appointmentReference} onChange={(e) => setStop(stop.key, { appointmentReference: e.target.value })} disabled={saving} maxLength={100} placeholder={t('transportOrders.route.appointmentPlaceholder')} />
              </FormField>
            </div>
            {sheet && (
              <div className="tof-row">
                <FormField
                  label={t('transportOrders.route.instructions')}
                  htmlFor={`st-instr-${stop.key}`}
                  hint={t('transportOrders.route.instructionsHint')}
                >
                  <input id={`st-instr-${stop.key}`} value={stop.instructions} onChange={(e) => setStop(stop.key, { instructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              </div>
            )}
            <div className="tof-row">
              <FormField label={t('transportOrders.route.accessInstr')} htmlFor={`st-access-${stop.key}`}>
                <input id={`st-access-${stop.key}`} value={stop.accessInstructions} onChange={(e) => setStop(stop.key, { accessInstructions: e.target.value })} disabled={saving} maxLength={2000} />
              </FormField>
              {isSite ? null : stop.stopType === 'Loading' ? (
                <FormField label={t('transportOrders.route.loadInstr')} htmlFor={`st-loadinstr-${stop.key}`}>
                  <input id={`st-loadinstr-${stop.key}`} value={stop.loadingInstructions} onChange={(e) => setStop(stop.key, { loadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              ) : (
                <FormField label={t('transportOrders.route.unloadInstr')} htmlFor={`st-unloadinstr-${stop.key}`}>
                  <input id={`st-unloadinstr-${stop.key}`} value={stop.unloadingInstructions} onChange={(e) => setStop(stop.key, { unloadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              )}
            </div>
            {/* A deviating address offers the same reset right under its hint (StopAddressFields). */}
            {stop.locationId !== '' && stop.id && !stop.refreshSnapshot && !stop.addressOverridden && (
              <div className="tof-stop-toolbar">
                <button
                  type="button"
                  className="tof-link"
                  onClick={() => onRequestRefresh(stop.key)}
                  disabled={saving}
                >
                  {t('transportOrders.route.refreshFromLocation')}
                </button>
              </div>
            )}
          </details>
        </>
      )}
    </fieldset>
  )
}
