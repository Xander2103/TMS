import { euro } from '../invoices/types'
import type { TranslateFn } from '../../i18n/localeContext'
import type { Dashboard } from './types'

/** One clickable dashboard tile (the former "Kpi" shape, unchanged rendering). */
export interface DashboardTile {
  label: string
  value: string
  hint?: string
  to: string
  alert?: boolean
}

/** Lazily fetched data outside the main /api/dashboard payload. */
export interface DashboardExtras {
  /** GET /api/dossiers/attention-count — only fetched when the planning group renders
   * for a user with dossiers.view; null = not fetched (tile stays hidden). */
  dossierAttentionCount: number | null
}

/**
 * Wave 1 §16: het dashboard is rolgericht — een gebruiker ziet alleen de tegelgroepen
 * van zijn rol in plaats van de 26-tegelmuur. Elke bestaande tegel is behouden maar in
 * precies één doelgroepgroep geplaatst; `audience` is een any-of permissielijst
 * (leeg = iedere aangemelde gebruiker), gecheckt met hasAnyPermission.
 */
export interface DashboardTileGroup {
  id: string
  /** Translation key for the group header shown above the tiles (resolved via t()). */
  title: string
  /** Any-of permissions deciding whether the group renders; empty = everyone. */
  audience: string[]
  /** Builds the group's tiles from the shared dashboard payload + lazy extras; labels/hints
   * are produced already translated via the passed translate function. */
  tiles: (data: Dashboard, extras: DashboardExtras, t: TranslateFn) => DashboardTile[]
}

export const DASHBOARD_TILE_GROUPS: DashboardTileGroup[] = [
  {
    id: 'planning',
    title: 'appDashboard.groups.planning',
    audience: ['planning.view'],
    tiles: (data, extras, t) => [
      {
        label: t('appDashboard.tiles.openOrders'),
        value: String(data.ordersOpenCount),
        hint: t('appDashboard.tiles.openOrdersHint', { count: data.ordersInExecutionCount }),
        to: '/transport-orders',
      },
      {
        label: t('appDashboard.tiles.tripsToday'),
        value: String(data.tripsTodayTotal),
        hint: t('appDashboard.tiles.tripsTodayHint', { count: data.tripsTodayInProgress }),
        to: '/planning',
        alert: data.tripsTodayWithConflicts > 0,
      },
      ...(extras.dossierAttentionCount != null
        ? [{
            label: t('appDashboard.tiles.dossiersAttention'),
            value: String(extras.dossierAttentionCount),
            hint: t('appDashboard.tiles.dossiersAttentionHint'),
            to: '/dossiers',
            alert: extras.dossierAttentionCount > 0,
          }]
        : []),
      {
        label: t('appDashboard.tiles.openIncidents'),
        value: String(data.openIncidentCount),
        hint: t('appDashboard.tiles.openIncidentsHint'),
        to: '/incidents',
        alert: data.openIncidentCount > 0,
      },
      {
        label: t('appDashboard.tiles.failedScans'),
        value: String(data.failedScanCount),
        hint: t('appDashboard.tiles.failedScansHint'),
        to: '/exceptions',
        alert: data.failedScanCount > 0,
      },
    ],
  },
  {
    id: 'voorraad',
    title: 'appDashboard.groups.inventory',
    audience: ['inventory.view', 'inventory.manage'],
    tiles: (data, _extras, t) =>
      data.inventory
        ? [
            { label: t('appDashboard.tiles.lowStock'), value: String(data.inventory.lowStock), to: '/inventory?status=LowStock' },
            {
              label: t('appDashboard.tiles.criticalStock'),
              value: String(data.inventory.criticalStock),
              to: '/inventory?status=CriticalStock',
              alert: data.inventory.criticalStock > 0,
            },
            { label: t('appDashboard.tiles.outOfStock'), value: String(data.inventory.outOfStock), to: '/inventory?status=OutOfStock' },
            {
              label: t('appDashboard.tiles.negativeStock'),
              value: String(data.inventory.negativeStock),
              to: '/inventory?status=NegativeStock',
              alert: data.inventory.negativeStock > 0,
            },
            { label: t('appDashboard.tiles.openReorderProposals'), value: String(data.inventory.openReorderProposals), to: '/inventory' },
            {
              label: t('appDashboard.tiles.overdueReturns'),
              value: String(data.inventory.overdueReturns),
              to: '/inventory',
              alert: data.inventory.overdueReturns > 0,
            },
          ]
        : [],
  },
  {
    id: 'taken',
    title: 'appDashboard.groups.tasks',
    audience: ['tasks.view_own', 'tasks.view_team', 'tasks.view_all'],
    tiles: (data, _extras, t) => {
      const tasks = data.tasks
      if (!tasks) return []
      return [
        { label: t('appDashboard.tiles.myOpenTasks'), value: String(tasks.myOpen), to: '/tasks?mine=1' },
        { label: t('appDashboard.tiles.dueToday'), value: String(tasks.myDueToday), to: '/tasks?mine=1' },
        {
          label: t('appDashboard.tiles.overdue'),
          value: String(tasks.myOverdue),
          to: '/tasks?mine=1&overdue=1',
          alert: tasks.myOverdue > 0,
        },
        { label: t('appDashboard.tiles.toAcknowledge'), value: String(tasks.myToAcknowledge), to: '/inbox' },
        ...(tasks.teamOpen != null
          ? [
              { label: t('appDashboard.tiles.teamOpen'), value: String(tasks.teamOpen), to: '/tasks' },
              {
                label: t('appDashboard.tiles.teamOverdue'),
                value: String(tasks.teamOverdue ?? 0),
                to: '/tasks?overdue=1',
                alert: (tasks.teamOverdue ?? 0) > 0,
              },
              { label: t('appDashboard.tiles.blocked'), value: String(tasks.teamBlocked ?? 0), to: '/tasks?status=Blocked' },
              { label: t('appDashboard.tiles.waitingReview'), value: String(tasks.teamWaitingReview ?? 0), to: '/tasks?review=1' },
            ]
          : []),
      ]
    },
  },
  {
    id: 'facturatie',
    title: 'appDashboard.groups.billing',
    audience: ['invoices.view'],
    tiles: (data, _extras, t) => [
      {
        label: t('appDashboard.tiles.revenueMonth'),
        value: euro(data.revenueInvoicedThisMonth),
        hint: t('appDashboard.tiles.revenueMonthHint', { count: data.ordersCompletedThisMonth }),
        to: '/invoices',
      },
      {
        label: t('appDashboard.tiles.outstanding'),
        value: euro(data.outstandingAmount),
        hint:
          data.overdueInvoiceCount > 0
            ? t('appDashboard.tiles.outstandingOverdueHint', { count: data.overdueInvoiceCount })
            : t('appDashboard.tiles.outstandingNoneHint'),
        to: '/invoices',
        alert: data.overdueInvoiceCount > 0,
      },
      {
        label: t('appDashboard.tiles.missingPod'),
        value: String(data.missingPodCount),
        hint: t('appDashboard.tiles.missingPodHint'),
        to: '/transport-orders',
        alert: data.missingPodCount > 0,
      },
    ],
  },
  {
    id: 'vloot',
    title: 'appDashboard.groups.fleet',
    audience: ['vehicles.view'],
    tiles: (data, _extras, t) => [
      {
        label: t('appDashboard.tiles.vehiclesAvailable'),
        value: String(data.vehiclesAvailable),
        hint: t('appDashboard.tiles.vehiclesAvailableHint', { count: data.maintenanceDueCount + data.inspectionsDueCount }),
        to: '/fleet',
        alert: data.documentsExpiringCount + data.openDamageCount > 0,
      },
      {
        label: t('appDashboard.tiles.overdueMaintenance'),
        value: String(data.overdueMaintenanceCount),
        hint: t('appDashboard.tiles.overdueMaintenanceHint'),
        to: '/fleet',
        alert: data.overdueMaintenanceCount > 0,
      },
    ],
  },
  {
    // HR-vormige audience (bewust géén employees.view/absences.view: die zitten in de
    // gedeelde CommonView-set, waardoor élke dispatcher de HR-groep zou zien — precies
    // de tegelmuur die §16 afschaft).
    id: 'personeel',
    title: 'appDashboard.groups.hr',
    audience: ['employees.create', 'employees.edit', 'absences.approve', 'hr_settings.manage'],
    tiles: (data, _extras, t) => [
      {
        label: t('appDashboard.tiles.driversAbsentToday'),
        value: String(data.driversAbsentToday),
        to: '/absences',
      },
      {
        label: t('appDashboard.tiles.qualifications'),
        value: String(data.qualificationsExpiring30d),
        hint:
          data.qualificationsExpired > 0
            ? t('appDashboard.tiles.qualificationsHintExpired', { count: data.qualificationsExpired })
            : t('appDashboard.tiles.qualificationsHint'),
        to: '/qualifications',
        alert: data.qualificationsExpiring30d + data.qualificationsExpired > 0,
      },
    ],
  },
  {
    id: 'management',
    title: 'appDashboard.groups.management',
    audience: ['kpi.view'],
    tiles: (_data, _extras, t) => [
      { label: t('appDashboard.tiles.kpis'), value: '→', hint: t('appDashboard.tiles.kpisHint'), to: '/kpi' },
      { label: t('appDashboard.tiles.profitability'), value: '→', hint: t('appDashboard.tiles.profitabilityHint'), to: '/profitability' },
    ],
  },
  {
    id: 'communicatie',
    title: 'appDashboard.groups.communication',
    audience: [],
    tiles: (data, _extras, t) => [
      {
        label: t('appDashboard.tiles.newMessages'),
        value: String(data.unreadInternalMessages),
        hint: t('appDashboard.tiles.newMessagesHint'),
        to: '/inbox',
      },
    ],
  },
]
