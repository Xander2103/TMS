import { useLocale } from '../../../i18n/localeContext'
import { activityReference, issueActivity } from '../activityDisplay'
import type { DossierActivity, ReadinessIssue, ReadinessSection, ReadinessSeverity } from '../types'
import './attention-panel.css'

const SEVERITY_ICON: Record<ReadinessSeverity, string> = {
  Blocking: '⛔',
  Warning: '⚠',
  Info: 'ℹ',
}

/** Vertaalsleutels per severity — renderen als t(SEVERITY_LABEL_KEYS[severity]). */
const SEVERITY_LABEL_KEYS: Record<ReadinessSeverity, string> = {
  Blocking: 'dossiers.attention.severity.Blocking',
  Warning: 'dossiers.attention.severity.Warning',
  Info: 'dossiers.attention.severity.Info',
}

/** Vertaalsleutels per sectie — renderen als t(SECTION_LABEL_KEYS[section]). */
const SECTION_LABEL_KEYS: Record<ReadinessSection, string> = {
  algemeen: 'dossiers.attention.section.algemeen',
  activiteiten: 'dossiers.attention.section.activiteiten',
  route: 'dossiers.attention.section.route',
  goederen: 'dossiers.attention.section.goederen',
  prijs: 'dossiers.attention.section.prijs',
}

/**
 * "0006: verkoopprijs ontbreekt." — every warning names the activity/order it is about. The backend
 * puts the number in the message; for an issue that only carries the id, the number is looked up
 * and prefixed here (never twice).
 */
function issueMessage(issue: ReadinessIssue, activities: DossierActivity[]): string {
  const activity = issueActivity(issue, activities)
  if (!activity) return issue.message
  // The backend names an order by its number and a standalone activity by its type name.
  const named = [activity.linkedOrderNumber, activity.label, activity.activityTypeName].some(
    (name) => name != null && name !== '' && issue.message.includes(name),
  )
  return named ? issue.message : `${activityReference(activity)}: ${issue.message}`
}

interface AttentionPanelProps {
  issues: ReadinessIssue[]
  /** The dossier's activities, to name the activity/order an issue points at by id. */
  activities?: DossierActivity[]
  /**
   * Jumps to the section AND the field that resolves the issue (`field` is the backend's
   * readiness field key, e.g. "stops.unloading", "stops.plannedFrom", "price"; null = section).
   * The full issue travels along so the host can select the order/activity it is about first.
   */
  onNavigate: (section: ReadinessSection, field: string | null, issue: ReadinessIssue) => void
}

/**
 * Aandacht as a compact horizontal strip (redesign 2026-09-11): one label, then every
 * readiness issue inline — icon + text (never colour-only) + its [Ga naar …] jump — separated
 * by dividers and wrapping onto a second line when needed. Hidden when there is nothing to say.
 * The warning text itself is clickable too: it performs the same jump (tab + activity + field).
 */
export function AttentionPanel({ issues, activities = [], onNavigate }: AttentionPanelProps) {
  const { t } = useLocale()
  if (issues.length === 0) return null
  const worst = issues.some((i) => i.severity === 'Blocking') ? 'blocking' : issues.some((i) => i.severity === 'Warning') ? 'warning' : 'info'
  return (
    <section className={`dossier-attention dossier-attention-strip is-${worst}`} aria-label={t('dossiers.attention.title')}>
      <h2>{t('dossiers.attention.title')}:</h2>
      <ul>
        {issues.map((issue) => (
          <li key={`${issue.code}-${issue.activityId ?? issue.transportOrderId ?? ''}-${issue.message}`} className={`dossier-attention-${issue.severity.toLowerCase()}`}>
            <span className="dossier-attention-icon" role="img" aria-label={t(SEVERITY_LABEL_KEYS[issue.severity])}>
              {SEVERITY_ICON[issue.severity]}
            </span>
            <button
              type="button"
              className="dossier-attention-message dossier-attention-message-button"
              onClick={() => onNavigate(issue.section, issue.field ?? null, issue)}
            >
              {issueMessage(issue, activities)}
            </button>
            <button type="button" className="link-button" onClick={() => onNavigate(issue.section, issue.field ?? null, issue)}>
              {t('dossiers.attention.goTo', {
                section: SECTION_LABEL_KEYS[issue.section] ? t(SECTION_LABEL_KEYS[issue.section]) : issue.section,
              })}
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
