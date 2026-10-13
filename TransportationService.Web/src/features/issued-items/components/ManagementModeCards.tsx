import { useId } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import {
  MANAGEMENT_MODES,
  MANAGEMENT_MODE_DESCRIPTIONS,
  MANAGEMENT_MODE_EXAMPLES,
  MANAGEMENT_MODE_LABELS,
  type ManagementMode,
} from '../managementMode'

interface ManagementModeCardsProps {
  value: ManagementMode
  onChange: (mode: ManagementMode) => void
  disabled?: boolean
  /**
   * When set, only the current mode stays selectable and this text explains why (e.g. an item
   * that already has variants cannot leave the variants mode — a backend rule).
   */
  lockedReason?: string | null
  /** Shown under the cards after a category-based suggestion was applied. */
  suggestionHint?: string | null
}

/**
 * "Beheerwijze" as three selectable cards: ONE radio group with a visible title, description and
 * examples per option. Each radio is named by its title only (not the whole card text), so
 * screen readers announce "Individueel item, radio, 1 of 3" and tests can target the title.
 */
export function ManagementModeCards({ value, onChange, disabled = false, lockedReason, suggestionHint }: ManagementModeCardsProps) {
  const { t } = useLocale()
  const groupName = useId()

  return (
    <fieldset className="issued-items-mode-fieldset">
      <legend className="issued-items-visually-hidden">{t('issuedItems.form.sectionMode')}</legend>
      <div className="issued-items-mode-grid">
        {MANAGEMENT_MODES.map((mode) => {
          const selected = mode === value
          const isDisabled = disabled || (Boolean(lockedReason) && !selected)
          const titleId = `${groupName}-${mode}-title`
          const descriptionId = `${groupName}-${mode}-description`
          return (
            <label
              key={mode}
              className={['issued-items-mode-card', selected ? 'is-selected' : '', isDisabled ? 'is-disabled' : ''].filter(Boolean).join(' ')}
            >
              <input
                type="radio"
                className="issued-items-mode-radio"
                name={groupName}
                value={mode}
                checked={selected}
                disabled={isDisabled}
                aria-labelledby={titleId}
                aria-describedby={descriptionId}
                onChange={() => onChange(mode)}
              />
              <span className="issued-items-mode-title" id={titleId}>
                {t(MANAGEMENT_MODE_LABELS[mode])}
              </span>
              <span className="issued-items-mode-description" id={descriptionId}>
                {t(MANAGEMENT_MODE_DESCRIPTIONS[mode])}
              </span>
              <span className="issued-items-mode-examples">{t(MANAGEMENT_MODE_EXAMPLES[mode])}</span>
            </label>
          )
        })}
      </div>
      {lockedReason && <p className="issued-items-section-hint issued-items-mode-locked">{lockedReason}</p>}
      {suggestionHint && (
        <p className="issued-items-suggestion" role="status">
          {suggestionHint}
        </p>
      )}
    </fieldset>
  )
}
