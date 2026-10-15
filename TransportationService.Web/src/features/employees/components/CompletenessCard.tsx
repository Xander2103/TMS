import { useLocale } from '../../../i18n/localeContext'
import type { EmployeeCompleteness, EmployeeCompletenessItem } from '../types/employee'
import './CompletenessCard.css'

interface CompletenessCardProps {
  completeness: EmployeeCompleteness
  /**
   * Navigates to the block/section that owns a missing item. Omit for read-only viewers:
   * missing items then render as plain text instead of actions.
   */
  onNavigate?: (section: string, item: EmployeeCompletenessItem) => void
  /**
   * Per-section gate: when it returns false for a missing item's section, that item has no action
   * even though `onNavigate` is set — e.g. a "documenten" item when the viewer lacks
   * `employee_documents.view` (that section wouldn't exist for them to land on).
   */
  canNavigate?: (section: string) => boolean
}

/**
 * Missing items that stop the employee from being deployed (a driver without a driving-licence
 * document). Only these get warning emphasis; every other gap is neutral HR housekeeping.
 */
const DEPLOYMENT_CRITICAL = new Set(['driving_licence_document'])

/**
 * B15 dossier completeness: "7 gegevens ontbreken", then one line per gap with the action that
 * fills it (Aanvullen → the overview card in edit mode; Bekijken → the Documenten section).
 * Neutral by default — no orange chips for every employee — with warning emphasis only for gaps
 * that block deployment. A complete dossier shows nothing (the list page still shows the %).
 */
export function CompletenessCard({ completeness, onNavigate, canNavigate }: CompletenessCardProps) {
  const { t } = useLocale()
  const { percentage, isComplete, missingItems } = completeness
  if (isComplete || missingItems.length === 0) return null

  const critical = missingItems.some((item) => DEPLOYMENT_CRITICAL.has(item.code))

  return (
    <section
      className={critical ? 'completeness-card is-attention' : 'completeness-card'}
      aria-label={t('employees.completeness.ariaLabel')}
    >
      <div className="completeness-card-header">
        <h3 className="completeness-card-title">{t('employees.completeness.missing', { count: missingItems.length })}</h3>
        <span className="completeness-card-percent">{t('employees.completeness.percent', { percentage })}</span>
        <div
          className="completeness-card-bar"
          role="progressbar"
          aria-label={t('employees.completeness.percent', { percentage })}
          aria-valuenow={percentage}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <div className="completeness-card-bar-fill" style={{ width: `${percentage}%` }} />
        </div>
      </div>
      <ul className="completeness-card-missing">
        {missingItems.map((item) => {
          const navigable = Boolean(onNavigate) && (canNavigate ? canNavigate(item.section) : true)
          const isCritical = DEPLOYMENT_CRITICAL.has(item.code)
          return (
            <li key={item.code} className={isCritical ? 'is-critical' : undefined}>
              {isCritical && (
                <span role="img" aria-label={t('ui.attention.severity.warning')}>
                  ⚠
                </span>
              )}
              <span className="completeness-card-label">{item.label}</span>
              {navigable && (
                <button
                  type="button"
                  className="completeness-card-action"
                  aria-label={`${item.label}: ${item.section === 'documenten' ? t('employees.completeness.view') : t('employees.completeness.fill')}`}
                  onClick={() => onNavigate!(item.section, item)}
                >
                  {item.section === 'documenten' ? t('employees.completeness.view') : t('employees.completeness.fill')}
                </button>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
