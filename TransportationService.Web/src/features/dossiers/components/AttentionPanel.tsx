import type { ReadinessIssue, ReadinessSection, ReadinessSeverity } from '../types'

const SEVERITY_ICON: Record<ReadinessSeverity, string> = {
  Blocking: '⛔',
  Warning: '⚠',
  Info: 'ℹ',
}

const SEVERITY_LABEL: Record<ReadinessSeverity, string> = {
  Blocking: 'Blokkerend',
  Warning: 'Waarschuwing',
  Info: 'Info',
}

const SECTION_LABEL: Record<ReadinessSection, string> = {
  algemeen: 'algemeen',
  activiteiten: 'activiteiten',
  route: 'route',
  goederen: 'goederen',
  prijs: 'prijs',
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
  if (issues.length === 0) return null
  return (
    <section className="dossier-attention" aria-label="Aandacht">
      <h2>Aandacht</h2>
      <ul>
        {issues.map((issue) => (
          <li key={`${issue.code}-${issue.message}`} className={`dossier-attention-${issue.severity.toLowerCase()}`}>
            <span className="dossier-attention-icon" role="img" aria-label={SEVERITY_LABEL[issue.severity]}>
              {SEVERITY_ICON[issue.severity]}
            </span>
            <span className="dossier-attention-message">{issue.message}</span>
            <button type="button" className="link-button" onClick={() => onNavigate(issue.section)}>
              Ga naar {SECTION_LABEL[issue.section] ?? issue.section}
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
