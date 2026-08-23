import type { ReactNode } from 'react'
import { useLocale } from '../../i18n/localeContext'
import { MONTH_NAMES, addDays, mondayOf, startOfMonth } from './dateUtils'
import './calendar.css'

export type CalendarViewMode = 'month' | 'week' | 'list'

export interface CalendarToolbarView {
  id: CalendarViewMode
  label: string
}

export interface CalendarToolbarProps {
  anchor: Date
  view: CalendarViewMode
  onViewChange: (view: CalendarViewMode) => void
  /** Toolbar computes the next anchor for vorige/volgende/Vandaag and hands it back. */
  onNavigate: (next: Date) => void
  views?: CalendarToolbarView[]
  /** Step size (days) used by vorige/volgende/Vandaag while the "list" view is active. */
  listStepDays?: number
  actions?: ReactNode
}

function formatListLabel(start: Date, end: Date): string {
  const sameYear = start.getFullYear() === end.getFullYear()
  const startLabel = `${start.getDate()} ${MONTH_NAMES[start.getMonth()]}`
  const endLabel = `${end.getDate()} ${MONTH_NAMES[end.getMonth()]} ${end.getFullYear()}`
  return sameYear ? `${startLabel} – ${endLabel}` : `${startLabel} ${start.getFullYear()} – ${endLabel}`
}

function periodLabelFor(view: CalendarViewMode, anchor: Date, listStepDays: number): string {
  if (view === 'month') return `${MONTH_NAMES[anchor.getMonth()]} ${anchor.getFullYear()}`
  if (view === 'week') {
    const monday = mondayOf(anchor)
    return `week van ${monday.getDate()} ${MONTH_NAMES[monday.getMonth()]} ${monday.getFullYear()}`
  }
  const start = mondayOf(anchor)
  return formatListLabel(start, addDays(start, listStepDays - 1))
}

function nextAnchor(view: CalendarViewMode, anchor: Date, direction: -1 | 1, listStepDays: number): Date {
  if (view === 'month') return new Date(anchor.getFullYear(), anchor.getMonth() + direction, 1)
  if (view === 'week') return addDays(anchor, direction * 7)
  return addDays(anchor, direction * listStepDays)
}

function todayAnchor(view: CalendarViewMode): Date {
  return view === 'month' ? startOfMonth(new Date()) : mondayOf(new Date())
}

/**
 * Shared calendar navigation: view switcher (Maand|Week|Lijst), vorige/Vandaag/volgende and the
 * current period label — all derived from `anchor`/`view`, so features only need to store the
 * anchor date and hand it back to `onNavigate`.
 */
export function CalendarToolbar({
  anchor,
  view,
  onViewChange,
  onNavigate,
  views,
  listStepDays = 14,
  actions,
}: CalendarToolbarProps) {
  const { t } = useLocale()
  const resolvedViews: CalendarToolbarView[] = views ?? [
    { id: 'month', label: t('ui.calendar.month') },
    { id: 'week', label: t('ui.calendar.week') },
    { id: 'list', label: t('ui.calendar.list') },
  ]
  return (
    <div className="cal-toolbar">
      <div className="cal-toolbar-nav-group">
        <button
          type="button"
          className="cal-toolbar-nav"
          onClick={() => onNavigate(nextAnchor(view, anchor, -1, listStepDays))}
          aria-label={t('ui.calendar.previousPeriod')}
        >
          {t('ui.calendar.previous')}
        </button>
        <button type="button" className="cal-toolbar-today" onClick={() => onNavigate(todayAnchor(view))}>
          {t('ui.calendar.today')}
        </button>
        <button
          type="button"
          className="cal-toolbar-nav"
          onClick={() => onNavigate(nextAnchor(view, anchor, 1, listStepDays))}
          aria-label={t('ui.calendar.nextPeriod')}
        >
          {t('ui.calendar.next')}
        </button>
      </div>
      <span className="cal-toolbar-label">{periodLabelFor(view, anchor, listStepDays)}</span>
      <span className="cal-view-switch" role="group" aria-label={t('ui.calendar.view')}>
        {resolvedViews.map((mode) => (
          <button
            key={mode.id}
            type="button"
            className={view === mode.id ? 'cal-view-active' : undefined}
            onClick={() => onViewChange(mode.id)}
          >
            {mode.label}
          </button>
        ))}
      </span>
      {actions && <span className="cal-toolbar-actions">{actions}</span>}
    </div>
  )
}
