import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { OPENING_DAYS, OPENING_DAY_LABEL_KEYS, computeOpeningIntervalErrors, openingIntervalsValid } from '../openingHours'
import type { LocationOpeningInterval } from '../types'
import './opening-hours-editor.css'

interface OpeningHoursEditorProps {
  value: LocationOpeningInterval[]
  /** Fired on every edit with the next full list and whether it is valid (block submit on false). */
  onChange: (value: LocationOpeningInterval[], isValid: boolean) => void
  disabled?: boolean
}

/**
 * Controlled compact weekly opening-hours grid (Ma..Zo, ISO day numbers 1–7). A day without
 * intervals shows as "Gesloten"; every edit reports the full interval list plus its validity
 * to the parent so the surrounding form can block submit on invalid hours.
 */
export function OpeningHoursEditor({ value, onChange, disabled }: OpeningHoursEditorProps) {
  const { t } = useLocale()
  const errors = computeOpeningIntervalErrors(value)

  function emit(next: LocationOpeningInterval[]) {
    onChange(next, openingIntervalsValid(next))
  }

  function addInterval(day: number) {
    emit([...value, { dayOfWeek: day, fromTime: '08:00', toTime: '17:00', note: null }])
  }

  function updateInterval(index: number, patch: Partial<LocationOpeningInterval>) {
    emit(value.map((interval, i) => (i === index ? { ...interval, ...patch } : interval)))
  }

  function removeInterval(index: number) {
    emit(value.filter((_, i) => i !== index))
  }

  function copyMondayToWeekdays() {
    const monday = value.filter((i) => i.dayOfWeek === 1)
    const untouched = value.filter((i) => i.dayOfWeek === 1 || i.dayOfWeek === 6 || i.dayOfWeek === 7)
    const copies = [2, 3, 4, 5].flatMap((day) => monday.map((i) => ({ ...i, dayOfWeek: day })))
    emit([...untouched, ...copies])
  }

  return (
    <div className="ohe">
      <div className="ohe-toolbar">
        <Button type="button" variant="secondary" onClick={copyMondayToWeekdays} disabled={disabled}>
          {t('locations.openingHours.copyMonday')}
        </Button>
        <Button type="button" variant="secondary" onClick={() => emit([])} disabled={disabled}>
          {t('locations.openingHours.clearAll')}
        </Button>
      </div>
      <div className="ohe-grid">
        {OPENING_DAYS.map((day) => {
          const label = t(OPENING_DAY_LABEL_KEYS[day - 1])
          const dayIntervals = value.map((interval, index) => ({ interval, index })).filter((x) => x.interval.dayOfWeek === day)
          return (
            <div key={day} className="ohe-day-row">
              <span className="ohe-day-label">{label}</span>
              <div className="ohe-day-body">
                {dayIntervals.length === 0 && <span className="ohe-closed">{t('locations.openingHours.closed')}</span>}
                {dayIntervals.map(({ interval, index }) => (
                  <div key={index} className="ohe-interval">
                    <div className="ohe-interval-fields">
                      <input
                        type="time"
                        aria-label={t('locations.openingHours.fromAria', { day: label })}
                        value={interval.fromTime}
                        onChange={(e) => updateInterval(index, { fromTime: e.target.value })}
                        disabled={disabled}
                      />
                      <span aria-hidden="true">–</span>
                      <input
                        type="time"
                        aria-label={t('locations.openingHours.toAria', { day: label })}
                        value={interval.toTime}
                        onChange={(e) => updateInterval(index, { toTime: e.target.value })}
                        disabled={disabled}
                      />
                      <input
                        type="text"
                        className="ohe-note"
                        aria-label={t('locations.openingHours.noteAria', { day: label })}
                        placeholder={t('locations.openingHours.notePlaceholder')}
                        maxLength={200}
                        value={interval.note ?? ''}
                        onChange={(e) => updateInterval(index, { note: e.target.value || null })}
                        disabled={disabled}
                      />
                      <button
                        type="button"
                        className="ohe-remove"
                        aria-label={t('locations.openingHours.removeAria', { day: label })}
                        onClick={() => removeInterval(index)}
                        disabled={disabled}
                      >
                        ✕
                      </button>
                    </div>
                    {errors[index] && (
                      <p className="ohe-error" role="alert">
                        {t(errors[index])}
                      </p>
                    )}
                  </div>
                ))}
                <button
                  type="button"
                  className="ohe-add"
                  aria-label={t('locations.openingHours.addAria', { day: label })}
                  onClick={() => addInterval(day)}
                  disabled={disabled}
                >
                  {t('locations.openingHours.add')}
                </button>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
