import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { localIsoDate } from '../warehouseDay'
import '../pages/warehousing.css'

interface WarehouseDayToggleProps {
  /** The selected day as `YYYY-MM-DD`. */
  date: string
  onChange: (date: string) => void
  /** Also offer a date field for any other day (dock planning); the loading floor only needs the two. */
  withDatePicker?: boolean
}

/**
 * B47: the one Vandaag/Morgen control of the warehouse screens — a segmented pair of radio
 * buttons, optionally followed by a date field for other days. Neither segment is pressed when
 * the date field points elsewhere.
 */
export function WarehouseDayToggle({ date, onChange, withDatePicker = false }: WarehouseDayToggleProps) {
  const { t } = useLocale()
  const today = localIsoDate(0)
  const tomorrow = localIsoDate(1)
  const options: Array<[string, string]> = [
    [today, t('warehousing.day.today')],
    [tomorrow, t('warehousing.day.tomorrow')],
  ]
  return (
    <div className="wh-day-control">
      <span className="wh-day-toggle" role="radiogroup" aria-label={t('warehousing.day.aria')}>
        {options.map(([value, label]) => (
          <Button
            key={value}
            variant={date === value ? 'primary' : 'secondary'}
            role="radio"
            aria-checked={date === value}
            onClick={() => onChange(value)}
          >
            {label}
          </Button>
        ))}
      </span>
      {withDatePicker && (
        <input
          type="date"
          className="ui-filter-select"
          value={date}
          onChange={(event) => onChange(event.target.value || date)}
          aria-label={t('warehousing.day.date')}
        />
      )}
    </div>
  )
}
