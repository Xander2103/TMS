import type { ReactNode } from 'react'
import { AlertTriangle, CalendarDays, CircleAlert, Info, MapPin, OctagonAlert, Tag, Truck, type LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { activityReference, issueActivity } from '../activityDisplay'
import type { DossierActivity, DossierOpenException, ReadinessIssue, ReadinessSection, ReadinessSeverity } from '../types'
import './attention-panel.css'

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

const SEVERITY_RANK: Record<ReadinessSeverity, number> = { Blocking: 0, Warning: 1, Info: 2 }

interface IssueKind {
  key: string
  icon: LucideIcon
  /** Translation key of the title; absent = the backend's own message. */
  titleKey?: string
  /** Translation key of the action; absent = "Naar {section}". */
  actionKey?: string
}

/** Which issues mean the same thing to the planner, whichever order or activity they are about. */
function issueKind(code: string): IssueKind {
  switch (code) {
    case 'pricing.missing':
    case 'pricing.none':
      return { key: 'priceMissing', icon: Tag, titleKey: 'dossiers.attention.kind.priceMissing' }
    case 'pricing.zero':
      return { key: 'priceZero', icon: Tag, titleKey: 'dossiers.attention.kind.priceZero' }
    case 'pricing.incomplete':
      return { key: 'priceIncomplete', icon: Tag, titleKey: 'dossiers.attention.kind.priceIncomplete' }
    case 'pricing.stale':
      return { key: 'priceStale', icon: Tag, titleKey: 'dossiers.attention.kind.priceStale' }
    case 'route.date_missing':
      return { key: 'planning', icon: CalendarDays, titleKey: 'dossiers.attention.kind.planningMissing', actionKey: 'dossiers.attention.toPlanning' }
    case 'order.confirm.stops':
      return { key: 'stops', icon: MapPin }
    case 'route.order_missing':
      return { key: 'orderMissing', icon: Truck, titleKey: 'dossiers.attention.kind.orderMissing' }
    default:
      return { key: code.startsWith('pricing.coverage') ? 'priceIncomplete' : code, icon: code.startsWith('pricing.') ? Tag : CircleAlert,
        titleKey: code.startsWith('pricing.coverage') ? 'dossiers.attention.kind.priceIncomplete' : undefined }
  }
}

/** The stop issues name what is unknown in their text; one group says it for all of them. */
function stopsTitleKey(issues: ReadinessIssue[]): string {
  const onlyUnloading = issues.every((i) => i.field === 'stops.unloading')
  if (onlyUnloading) return 'dossiers.attention.kind.stopsUnloading'
  const onlyLoading = issues.every((i) => i.field === 'stops.loading' && /laadlocatie/i.test(i.message))
  return onlyLoading ? 'dossiers.attention.kind.stopsLoading' : 'dossiers.attention.kind.stopsBoth'
}

interface IssueGroup {
  kind: IssueKind
  issues: ReadinessIssue[]
  severity: ReadinessSeverity
}

function groupIssues(issues: ReadinessIssue[]): IssueGroup[] {
  const groups = new Map<string, IssueGroup>()
  for (const issue of issues) {
    const kind = issueKind(issue.code)
    const group = groups.get(kind.key)
    if (group) {
      group.issues.push(issue)
      if (SEVERITY_RANK[issue.severity] < SEVERITY_RANK[group.severity]) group.severity = issue.severity
    } else {
      groups.set(kind.key, { kind, issues: [issue], severity: issue.severity })
    }
  }
  return [...groups.values()].sort((a, b) => SEVERITY_RANK[a.severity] - SEVERITY_RANK[b.severity])
}

/** "2 activiteiten · 0006, Plateau"; null for a dossier-level issue that names no activity or order. */
function groupScope(t: TranslateFn, issues: ReadinessIssue[], activities: DossierActivity[]): string | null {
  const names = new Set<string>()
  let scoped = 0
  for (const issue of issues) {
    const activity = issueActivity(issue, activities)
    if (activity) names.add(activityReference(activity))
    else if (issue.activityId || issue.transportOrderId) scoped += 1
  }
  const count = names.size + scoped
  if (count === 0) return null
  const label = t('dossiers.attention.activityCount', { count })
  return names.size > 0 ? `${label} · ${[...names].join(', ')}` : label
}

function SeverityIcon({ severity, icon: Icon }: { severity: ReadinessSeverity; icon: LucideIcon }) {
  const { t } = useLocale()
  return (
    <span className="dossier-attention-icon" role="img" aria-label={t(SEVERITY_LABEL_KEYS[severity])}>
      <Icon size={17} aria-hidden="true" />
    </span>
  )
}

function AttentionItem({ severity, icon, title, sub, detail, action }: {
  severity: ReadinessSeverity
  icon: LucideIcon
  title: ReactNode
  sub?: string | null
  /** Every underlying message, for whoever wants to read them without leaving the page. */
  detail?: string
  action?: ReactNode
}) {
  return (
    <li className={`dossier-attention-item is-${severity.toLowerCase()}`} title={detail}>
      <SeverityIcon severity={severity} icon={icon} />
      <span className="dossier-attention-text">
        <strong>{title}</strong>
        {(sub || action) && (
          <span className="dossier-attention-sub">
            {sub && <span className="dossier-attention-scope">{sub}</span>}
            {action}
          </span>
        )}
      </span>
    </li>
  )
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
  /** M12: OPEN incidents of the dossier — they block confirmation, so they belong in the strip. */
  openIncidents?: { id: string; title: string }[]
  /** UX-010: open afwijkingen, serious ones (they hold closing) shown as blocking. */
  openExceptions?: DossierOpenException[]
  /** Whether the afwijking detail is open to this user; the text is shown either way. */
  canOpenExceptions?: boolean
}

/**
 * Aandacht inside the dossier header (redesign 2026-11-03): one row of compact items, one per
 * KIND of problem however many activities share it, each with at most one action. Icon + text,
 * never colour alone. The action lands on the first activity of its group. Takes no room when
 * there is nothing to say.
 */
export function AttentionPanel({ issues, activities = [], onNavigate, openIncidents = [], openExceptions = [], canOpenExceptions = false }: AttentionPanelProps) {
  const { t } = useLocale()
  if (issues.length === 0 && openIncidents.length === 0 && openExceptions.length === 0) return null

  const holding = openExceptions.filter((e) => e.holds)
  const notHolding = openExceptions.filter((e) => !e.holds)
  const worst = issues.some((i) => i.severity === 'Blocking') || holding.length > 0 ? 'blocking'
    : issues.some((i) => i.severity === 'Warning') || openIncidents.length > 0 || openExceptions.length > 0 ? 'warning' : 'info'

  function exceptionItem(group: DossierOpenException[], severity: ReadinessSeverity) {
    if (group.length === 0) return null
    const single = group.length === 1 ? group[0] : null
    const text = single
      ? t('dossiers.attention.openException', { type: single.typeLabel, severity: single.severityLabel })
      : t('dossiers.attention.openExceptions', { count: group.length })
    const trip = single?.tripNumber ? t('dossiers.attention.openExceptionTrip', { tripNumber: single.tripNumber }) : null
    return (
      <AttentionItem
        key={`exceptions-${severity}`}
        severity={severity}
        icon={severity === 'Blocking' ? OctagonAlert : AlertTriangle}
        title={canOpenExceptions ? <Link className="ui-id-link" to={single ? `/exceptions/${single.id}` : '/exceptions'}>{text}</Link> : text}
        sub={trip}
      />
    )
  }

  return (
    <section className={`dossier-attention-bar is-${worst}`} aria-label={t('dossiers.attention.title')}>
      <h2>
        <span className="dossier-attention-bar-icon" aria-hidden="true">
          {worst === 'blocking' ? <OctagonAlert size={18} /> : worst === 'warning' ? <AlertTriangle size={18} /> : <Info size={18} />}
        </span>
        {t('dossiers.attention.title')}
      </h2>
      <ul>
        {openIncidents.length > 0 && (
          <AttentionItem
            severity="Warning"
            icon={CircleAlert}
            title={t('dossiers.attention.openIncidents', { count: openIncidents.length })}
            detail={[t('incidents.related.dossierAttention', { count: openIncidents.length }), ...openIncidents.map((i) => i.title)].join('\n')}
            action={openIncidents.length === 1
              ? <Link className="link-button" to={`/incidents/${openIncidents[0].id}`}>{openIncidents[0].title}</Link>
              : <Link className="link-button" to="/incidents">{t('dossiers.attention.viewAll')}</Link>}
          />
        )}
        {exceptionItem(holding, 'Blocking')}
        {exceptionItem(notHolding, 'Warning')}
        {groupIssues(issues).map(({ kind, issues: group, severity }) => {
          const first = group[0]
          const titleKey = kind.key === 'stops' ? stopsTitleKey(group) : kind.titleKey
          return (
            <AttentionItem
              key={kind.key}
              severity={severity}
              icon={kind.icon}
              title={titleKey ? t(titleKey) : first.message}
              sub={groupScope(t, group, activities)}
              detail={group.map((i) => i.message).join('\n')}
              action={
                <button type="button" className="link-button" onClick={() => onNavigate(first.section, first.field ?? null, first)}>
                  {kind.actionKey ? t(kind.actionKey) : t('dossiers.attention.goTo', {
                    section: SECTION_LABEL_KEYS[first.section] ? t(SECTION_LABEL_KEYS[first.section]) : first.section,
                  })}
                </button>
              }
            />
          )
        })}
      </ul>
    </section>
  )
}
