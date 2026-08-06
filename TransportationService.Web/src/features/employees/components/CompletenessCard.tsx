import type { EmployeeCompleteness } from '../types/employee'
import './CompletenessCard.css'

interface CompletenessCardProps {
  completeness: EmployeeCompleteness
  /**
   * Navigates to the dossier section/tab that owns a missing item. Omit for read-only viewers:
   * missing items then render as plain (non-interactive) chips instead of buttons.
   */
  onNavigate?: (section: string) => void
  /**
   * Per-section gate: when it returns false for a missing item's section, that item renders as
   * a plain chip even though `onNavigate` is set — e.g. a "documenten" item when the viewer
   * lacks `employee_documents.view` (that tab wouldn't exist for them to land on). Defaults to
   * always-navigable.
   */
  canNavigate?: (section: string) => boolean
}

/**
 * Dossier-completeness summary (HR maturity wave §2.1/§2.6): a progress bar plus, while
 * incomplete, one clickable chip per missing requirement that jumps straight to the section
 * that owns it. Rendered above the detail-page tabs.
 */
export function CompletenessCard({ completeness, onNavigate, canNavigate }: CompletenessCardProps) {
  const { percentage, isComplete, missingItems } = completeness

  return (
    <section className="completeness-card" aria-label="Dossiervolledigheid">
      <div className="completeness-card-header">
        <span className="completeness-card-title">
          {isComplete ? 'Dossier compleet ✓' : `Dossier ${percentage}% compleet`}
        </span>
      </div>
      <div
        className="completeness-card-bar"
        role="progressbar"
        aria-valuenow={percentage}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div
          className={`completeness-card-bar-fill${isComplete ? ' completeness-card-bar-fill-complete' : ''}`}
          style={{ width: `${percentage}%` }}
        />
      </div>
      {!isComplete && missingItems.length > 0 && (
        <ul className="completeness-card-missing">
          {missingItems.map((item) => {
            const navigable = Boolean(onNavigate) && (canNavigate ? canNavigate(item.section) : true)
            return (
              <li key={item.code}>
                {navigable ? (
                  <button type="button" className="completeness-card-chip" onClick={() => onNavigate!(item.section)}>
                    {item.label}
                  </button>
                ) : (
                  <span className="completeness-card-chip completeness-card-chip-static">{item.label}</span>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
