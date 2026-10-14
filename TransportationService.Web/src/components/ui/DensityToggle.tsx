import { useLocale } from '../../i18n/localeContext'
import type { ListDensity } from './listDensity'
import './DensityToggle.css'

interface DensityToggleProps {
  value: ListDensity
  onChange: (value: ListDensity) => void
}

/** B14: Compact / Normaal for an operational list — the same control on every list that offers it. */
export function DensityToggle({ value, onChange }: DensityToggleProps) {
  const { t } = useLocale()
  return (
    <div className="ui-density" role="group" aria-label={t('ui.density.label')}>
      {(['compact', 'normal'] as ListDensity[]).map((option) => (
        <button key={option} type="button" aria-pressed={value === option} onClick={() => onChange(option)}>
          {option === 'compact' ? t('ui.density.compact') : t('ui.density.normal')}
        </button>
      ))}
    </div>
  )
}
