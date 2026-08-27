import { useLocale } from '../../../i18n/localeContext'
import type { ReadinessIssue, ReadinessSection, ReadinessSeverity } from '../types'

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

interface AttentionPanelProps {
  issues: ReadinessIssue[]
  /** Scrolls to + opens the named section on the dossier page. */
  onNavigate: (section: ReadinessSection) => void
}

/**
 * §11 attention panel: one actionable row per readiness issue (icon + text, never
 * colour-only) with a [Ga naar …] jump. Hidden entirely when there is nothing to say.
 */
export function AttentionPanel({ issues, onNavigate }: AttentionPanelProps) {
  const { t } = useLocale()
  if (issues.length === 0) return null
  return (
    <section className="dossier-attention" aria-label={t('dossiers.attention.title')}>
      <h2>{t('dossiers.attention.title')}</h2>
      <ul>
        {issues.map((issue) => (
          <li key={`${issue.code}-${issue.message}`} className={`dossier-attention-${issue.severity.toLowerCase()}`}>
            <span className="dossier-attention-icon" role="img" aria-label={t(SEVERITY_LABEL_KEYS[issue.severity])}>
              {SEVERITY_ICON[issue.severity]}
            </span>
            <span className="dossier-attention-message">{issue.message}</span>
            <button type="button" className="link-button" onClick={() => onNavigate(issue.section)}>
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
