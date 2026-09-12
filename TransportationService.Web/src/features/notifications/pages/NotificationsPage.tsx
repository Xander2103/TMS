import { useCallback, useContext, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { AlertTriangle, BellRing, CheckCheck, ChevronDown, ChevronRight, Inbox, Search } from 'lucide-react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import {
  NOTIFICATION_CATEGORIES,
  NOTIFICATION_CATEGORY_LABELS,
  acknowledgeNotification,
  archiveNotification,
  getNotificationPreferences,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  setNotificationPreference,
  type Notification,
  type NotificationCategory,
  type NotificationPreference,
} from '../api/notificationsApi'
import { NotificationsContext } from '../notificationsContextValue'
import { useLocale } from '../../../i18n/localeContext'
import {
  DEFAULT_LIST_FILTERS,
  computeStats,
  filterNotifications,
  groupNotifications,
  resolveNotificationLink,
  type NotificationGroupKey,
  type NotificationListFilters,
  type NotificationSort,
  type NotificationStatusFilter,
} from '../notificationView'
import { NotificationRow } from '../components/NotificationRow'
import { NotificationDetail } from '../components/NotificationDetail'
import './notifications.css'

const SELECTED_PARAM = 'id'

const GROUP_LABEL_KEYS: Record<NotificationGroupKey, string> = {
  today: 'notificationCenter.groups.today',
  week: 'notificationCenter.groups.week',
  earlier: 'notificationCenter.groups.earlier',
}

/**
 * Meldingen: master-detail notification centre. The list (left) stays visible while the
 * selected notification is shown on the right with its contextual actions. Data flow,
 * filters (category/archive server-side, the rest client-side), read/acknowledge/archive
 * and preferences are the existing behaviours; only the presentation changed.
 */
export function NotificationsPage() {
  const navigate = useNavigate()
  const { showError, showSuccess } = useToast()
  const { t } = useLocale()
  const unreadBadge = useContext(NotificationsContext)
  const [searchParams, setSearchParams] = useSearchParams()

  const [notifications, setNotifications] = useState<Notification[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)
  const [now, setNow] = useState(() => new Date())
  const [categoryFilter, setCategoryFilter] = useState<NotificationCategory | ''>('')
  const [includeArchived, setIncludeArchived] = useState(false)
  const [filters, setFilters] = useState<NotificationListFilters>(DEFAULT_LIST_FILTERS)
  const [collapsed, setCollapsed] = useState<Set<NotificationGroupKey>>(() => new Set())
  const [busy, setBusy] = useState(false)
  const [preferences, setPreferences] = useState<NotificationPreference[] | null>(null)
  const listRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let mounted = true
    listNotifications({
      category: categoryFilter || undefined,
      includeArchived,
      take: 50,
    })
      .then((data) => {
        if (!mounted) return
        setNotifications(data)
        setNow(new Date())
        setLoadError(false)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [reloadToken, categoryFilter, includeArchived])

  useEffect(() => {
    let mounted = true
    getNotificationPreferences()
      .then((data) => {
        if (mounted) setPreferences(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  const categoryLabel = useCallback(
    (category: NotificationCategory) => t(NOTIFICATION_CATEGORY_LABELS[category]),
    [t],
  )

  const all = useMemo(() => notifications ?? [], [notifications])
  const visible = useMemo(() => filterNotifications(all, filters, categoryLabel), [all, filters, categoryLabel])
  const groups = useMemo(() => groupNotifications(visible, now), [visible, now])
  const stats = useMemo(() => computeStats(all), [all])

  const selectedId = searchParams.get(SELECTED_PARAM)
  const selected = useMemo(() => all.find((n) => n.id === selectedId) ?? null, [all, selectedId])

  const setSelectedId = useCallback(
    (id: string | null) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current)
          if (id) next.set(SELECTED_PARAM, id)
          else next.delete(SELECTED_PARAM)
          return next
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  function patchNotification(id: string, patch: Partial<Notification>) {
    setNotifications((current) => current?.map((n) => (n.id === id ? { ...n, ...patch } : n)) ?? null)
  }

  async function markRead(notification: Notification) {
    if (notification.isRead) return
    setBusy(true)
    try {
      await markNotificationRead(notification.id)
      patchNotification(notification.id, { isRead: true })
      unreadBadge?.refresh()
    } catch {
      showError(t('notificationCenter.errors.markFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function openLink(notification: Notification) {
    const link = resolveNotificationLink(notification.linkPath)
    try {
      if (!notification.isRead) {
        await markNotificationRead(notification.id)
        patchNotification(notification.id, { isRead: true })
        unreadBadge?.refresh()
      }
      if (link) navigate(link.path)
    } catch {
      showError(t('notificationCenter.errors.openFailed'))
    }
  }

  async function archive(notification: Notification) {
    setBusy(true)
    try {
      await archiveNotification(notification.id)
      if (!includeArchived) setSelectedId(null)
      reload()
    } catch {
      showError(t('notificationCenter.errors.archiveFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function acknowledge(notification: Notification) {
    setBusy(true)
    try {
      await acknowledgeNotification(notification.id)
      reload()
    } catch {
      showError(t('notificationCenter.errors.acknowledgeFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function markAll() {
    try {
      await markAllNotificationsRead()
      unreadBadge?.refresh()
      reload()
    } catch {
      showError(t('notificationCenter.errors.markFailed'))
    }
  }

  async function togglePreference(preference: NotificationPreference) {
    try {
      await setNotificationPreference(preference.category, !preference.enabled)
      setPreferences((current) =>
        current?.map((p) => (p.category === preference.category ? { ...p, enabled: !p.enabled } : p)) ?? null,
      )
      showSuccess(t('notificationCenter.toasts.preferenceSaved'))
    } catch {
      showError(t('notificationCenter.errors.preferenceFailed'))
    }
  }

  function toggleGroup(key: NotificationGroupKey) {
    setCollapsed((current) => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  function patchFilters(patch: Partial<NotificationListFilters>) {
    setFilters((current) => ({ ...current, ...patch }))
  }

  function clearFilters() {
    setFilters(DEFAULT_LIST_FILTERS)
    setCategoryFilter('')
    setIncludeArchived(false)
  }

  /** Arrow keys move focus between rows so the list behaves like an inbox. */
  function onListKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
    const rows = Array.from(listRef.current?.querySelectorAll<HTMLButtonElement>('button.ntc-row') ?? [])
    const index = rows.findIndex((row) => row === document.activeElement)
    if (index === -1) return
    event.preventDefault()
    const next = rows[event.key === 'ArrowDown' ? Math.min(index + 1, rows.length - 1) : Math.max(index - 1, 0)]
    next?.focus()
  }

  const hasUnread = stats.unread > 0
  const filtersActive =
    filters.search !== '' || filters.status !== 'all' || filters.warningsOnly || !filters.hideResolved || categoryFilter !== '' || includeArchived
  const loaded = !loadError && notifications !== null

  return (
    <div className="ntc-page">
      <Breadcrumbs items={[{ label: t('notificationCenter.page.title') }]} />
      <PageHeader
        title={t('notificationCenter.page.title')}
        subtitle={t('notificationCenter.page.subtitle')}
        action={
          <Button variant="primary" onClick={() => void markAll()} disabled={!hasUnread}>
            <CheckCheck size={16} aria-hidden="true" /> {t('notificationCenter.actions.markAllRead')}
          </Button>
        }
      />

      <div className="ntc-stats" role="group" aria-label={t('notificationCenter.stats.ariaLabel')}>
        <button
          type="button"
          className={`ntc-stat ${filters.status === 'all' && !filters.warningsOnly ? 'is-active' : ''}`}
          aria-pressed={filters.status === 'all' && !filters.warningsOnly}
          onClick={() => patchFilters({ status: 'all', warningsOnly: false })}
        >
          <span className="ntc-stat-icon is-info"><Inbox size={18} aria-hidden="true" /></span>
          <span className="ntc-stat-body">
            <span className="ntc-stat-label">{t('notificationCenter.stats.open')}</span>
            <span className="ntc-stat-value">{stats.open}</span>
            <span className="ntc-stat-sub">{t('notificationCenter.stats.ofTotal', { count: all.length })}</span>
          </span>
        </button>
        <button
          type="button"
          className={`ntc-stat ${filters.status === 'unread' ? 'is-active' : ''}`}
          aria-pressed={filters.status === 'unread'}
          onClick={() => patchFilters({ status: filters.status === 'unread' ? 'all' : 'unread' })}
        >
          <span className="ntc-stat-icon is-accent"><BellRing size={18} aria-hidden="true" /></span>
          <span className="ntc-stat-body">
            <span className="ntc-stat-label">{t('notificationCenter.stats.unread')}</span>
            <span className="ntc-stat-value">{stats.unread}</span>
            <span className="ntc-stat-sub">
              {stats.unread > 0 ? <span className="ntc-stat-pill">{t('notificationCenter.stats.unreadNew', { count: stats.unread })}</span> : '—'}
            </span>
          </span>
        </button>
        <button
          type="button"
          className={`ntc-stat ${filters.warningsOnly ? 'is-active' : ''}`}
          aria-pressed={filters.warningsOnly}
          onClick={() => patchFilters({ warningsOnly: !filters.warningsOnly })}
        >
          <span className="ntc-stat-icon is-warning"><AlertTriangle size={18} aria-hidden="true" /></span>
          <span className="ntc-stat-body">
            <span className="ntc-stat-label">{t('notificationCenter.stats.warnings')}</span>
            <span className="ntc-stat-value">{stats.warnings}</span>
            <span className="ntc-stat-sub">
              {stats.warnings > 0 ? <span className="ntc-stat-pill is-warning">{t('notificationCenter.stats.unreadNew', { count: stats.warnings })}</span> : '—'}
            </span>
          </span>
        </button>
      </div>

      <div className="ntc-filters">
        <label className="ntc-search">
          <Search size={16} aria-hidden="true" />
          <input
            type="search"
            value={filters.search}
            placeholder={t('notificationCenter.page.searchPlaceholder')}
            aria-label={t('notificationCenter.page.searchAria')}
            onChange={(e) => patchFilters({ search: e.target.value })}
          />
        </label>
        <select
          value={categoryFilter}
          onChange={(e) => setCategoryFilter(e.target.value as NotificationCategory | '')}
          aria-label={t('notificationCenter.page.categoryAria')}
        >
          <option value="">{t('notificationCenter.page.allCategories')}</option>
          {NOTIFICATION_CATEGORIES.map((category) => (
            <option key={category} value={category}>
              {t(NOTIFICATION_CATEGORY_LABELS[category])}
            </option>
          ))}
        </select>
        <select
          value={filters.status}
          onChange={(e) => patchFilters({ status: e.target.value as NotificationStatusFilter })}
          aria-label={t('notificationCenter.page.statusAria')}
        >
          <option value="all">{t('notificationCenter.page.allStatuses')}</option>
          <option value="unread">{t('notificationCenter.page.statusUnread')}</option>
          <option value="read">{t('notificationCenter.page.statusRead')}</option>
          <option value="ack">{t('notificationCenter.page.statusAck')}</option>
        </select>
        <label className="ntc-check">
          <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />
          {t('notificationCenter.page.showArchive')}
        </label>
        <label className="ntc-check">
          <input type="checkbox" checked={filters.hideResolved} onChange={(e) => patchFilters({ hideResolved: e.target.checked })} />
          {t('notificationCenter.page.hideResolved')}
        </label>
        {filtersActive && (
          <button type="button" className="ntc-link-button" onClick={clearFilters}>
            {t('notificationCenter.page.clearFilters')}
          </button>
        )}
      </div>

      <div className={`ntc-body ${selected ? 'has-selection' : ''}`}>
        <div className="ntc-list-panel" ref={listRef} onKeyDown={onListKeyDown}>
          <div className="ntc-list-head">
            <span className="ntc-list-count" aria-live="polite">
              {loaded ? t('notificationCenter.page.count', { count: visible.length }) : t('notificationCenter.page.loading')}
            </span>
            <label className="ntc-sort">
              <span>{t('notificationCenter.page.sortLabel')}</span>
              <select
                value={filters.sort}
                onChange={(e) => patchFilters({ sort: e.target.value as NotificationSort })}
                aria-label={t('notificationCenter.page.sortAria')}
              >
                <option value="newest">{t('notificationCenter.page.sortNewest')}</option>
                <option value="oldest">{t('notificationCenter.page.sortOldest')}</option>
              </select>
            </label>
          </div>

          {loadError && (
            <div className="ntc-state is-error" role="alert">
              <p>{t('notificationCenter.errors.loadFailed')}</p>
              <Button variant="secondary" onClick={reload}>
                {t('notificationCenter.page.retry')}
              </Button>
            </div>
          )}
          {!loadError && notifications === null && <p className="ntc-state placeholder-text">{t('notificationCenter.page.loading')}</p>}
          {loaded && visible.length === 0 && (
            <div className="ntc-state">
              <Inbox size={26} aria-hidden="true" />
              <p>{all.length === 0 || !filtersActive ? t('notificationCenter.page.empty') : t('notificationCenter.page.emptyFiltered')}</p>
              {all.length === 0 && !filtersActive ? (
                <span className="ntc-state-hint">{t('notificationCenter.page.emptyHint')}</span>
              ) : (
                <button type="button" className="ntc-link-button" onClick={clearFilters}>
                  {t('notificationCenter.page.clearFilters')}
                </button>
              )}
            </div>
          )}

          {loaded && visible.length > 0 && (
            <div className="ntc-groups" aria-label={t('notificationCenter.groups.listAria')}>
              {groups.map((group) => {
                const label = t(GROUP_LABEL_KEYS[group.key])
                const isCollapsed = collapsed.has(group.key)
                const listId = `ntc-group-${group.key}`
                return (
                  <section key={group.key} className="ntc-group">
                    <h3 className="ntc-group-head">
                      <button
                        type="button"
                        className="ntc-group-toggle"
                        aria-expanded={!isCollapsed}
                        aria-controls={listId}
                        aria-label={t('notificationCenter.groups.toggleAria', { group: label })}
                        onClick={() => toggleGroup(group.key)}
                      >
                        {isCollapsed ? <ChevronRight size={16} aria-hidden="true" /> : <ChevronDown size={16} aria-hidden="true" />}
                        <span>
                          {label} ({group.items.length})
                        </span>
                      </button>
                    </h3>
                    <ul id={listId} className="ntc-rows" hidden={isCollapsed}>
                      {group.items.map((notification) => (
                        <NotificationRow
                          key={notification.id}
                          notification={notification}
                          selected={notification.id === selectedId}
                          now={now}
                          onSelect={(n) => setSelectedId(n.id)}
                        />
                      ))}
                    </ul>
                  </section>
                )
              })}
            </div>
          )}

          {preferences && (
            <details className="ntf-preferences">
              <summary>{t('notificationCenter.page.preferencesSummary')}</summary>
              <p className="ntf-preferences-hint">{t('notificationCenter.page.preferencesHint')}</p>
              <ul>
                {preferences.map((preference) => (
                  <li key={preference.category}>
                    <label>
                      <input type="checkbox" checked={preference.enabled} onChange={() => void togglePreference(preference)} />{' '}
                      {t(NOTIFICATION_CATEGORY_LABELS[preference.category])}
                    </label>
                  </li>
                ))}
              </ul>
            </details>
          )}
        </div>

        <NotificationDetail
          notification={selected}
          now={now}
          busy={busy}
          onOpenLink={(n) => void openLink(n)}
          onMarkRead={(n) => void markRead(n)}
          onAcknowledge={(n) => void acknowledge(n)}
          onArchive={(n) => void archive(n)}
          onNavigate={(path) => navigate(path)}
          onClose={() => setSelectedId(null)}
        />
      </div>
    </div>
  )
}
