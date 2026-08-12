import { euro } from '../invoices/types'
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
  /** Group header shown above the tiles. */
  title: string
  /** Any-of permissions deciding whether the group renders; empty = everyone. */
  audience: string[]
  /** Builds the group's tiles from the shared dashboard payload + lazy extras. */
  tiles: (data: Dashboard, extras: DashboardExtras) => DashboardTile[]
}

export const DASHBOARD_TILE_GROUPS: DashboardTileGroup[] = [
  {
    id: 'planning',
    title: 'Planning & uitvoering',
    audience: ['planning.view'],
    tiles: (data, extras) => [
      {
        label: 'Open opdrachten',
        value: String(data.ordersOpenCount),
        hint: `${data.ordersInExecutionCount} in uitvoering`,
        to: '/transport-orders',
      },
      {
        label: 'Ritten vandaag',
        value: String(data.tripsTodayTotal),
        hint: `${data.tripsTodayInProgress} onderweg`,
        to: '/planning',
        alert: data.tripsTodayWithConflicts > 0,
      },
      ...(extras.dossierAttentionCount != null
        ? [{
            label: 'Dossiers met aandacht',
            value: String(extras.dossierAttentionCount),
            hint: 'open dossiers met aandachtspunten',
            to: '/dossiers',
            alert: extras.dossierAttentionCount > 0,
          }]
        : []),
      {
        label: 'Open incidenten',
        value: String(data.openIncidentCount),
        hint: 'nieuw of in behandeling',
        to: '/incidents',
        alert: data.openIncidentCount > 0,
      },
      {
        label: 'Mislukte scans',
        value: String(data.failedScanCount),
        hint: 'laatste 7 dagen',
        to: '/exceptions',
        alert: data.failedScanCount > 0,
      },
    ],
  },
  {
    id: 'voorraad',
    title: 'Voorraad',
    audience: ['inventory.view', 'inventory.manage'],
    tiles: (data) =>
      data.inventory
        ? [
            { label: 'Lage voorraad', value: String(data.inventory.lowStock), to: '/inventory?status=LowStock' },
            {
              label: 'Kritieke voorraad',
              value: String(data.inventory.criticalStock),
              to: '/inventory?status=CriticalStock',
              alert: data.inventory.criticalStock > 0,
            },
            { label: 'Niet op voorraad', value: String(data.inventory.outOfStock), to: '/inventory?status=OutOfStock' },
            {
              label: 'Negatieve voorraad',
              value: String(data.inventory.negativeStock),
              to: '/inventory?status=NegativeStock',
              alert: data.inventory.negativeStock > 0,
            },
            { label: 'Open bestelvoorstellen', value: String(data.inventory.openReorderProposals), to: '/inventory' },
            {
              label: 'Achterstallige retouren',
              value: String(data.inventory.overdueReturns),
              to: '/inventory',
              alert: data.inventory.overdueReturns > 0,
            },
          ]
        : [],
  },
  {
    id: 'taken',
    title: 'Taken',
    audience: ['tasks.view_own', 'tasks.view_team', 'tasks.view_all'],
    tiles: (data) => {
      const tasks = data.tasks
      if (!tasks) return []
      return [
        { label: 'Mijn open taken', value: String(tasks.myOpen), to: '/tasks?mine=1' },
        { label: 'Vandaag te doen', value: String(tasks.myDueToday), to: '/tasks?mine=1' },
        {
          label: 'Achterstallig',
          value: String(tasks.myOverdue),
          to: '/tasks?mine=1&overdue=1',
          alert: tasks.myOverdue > 0,
        },
        { label: 'Te bevestigen berichten', value: String(tasks.myToAcknowledge), to: '/inbox' },
        ...(tasks.teamOpen != null
          ? [
              { label: 'Open teamtaken', value: String(tasks.teamOpen), to: '/tasks' },
              {
                label: 'Team achterstallig',
                value: String(tasks.teamOverdue ?? 0),
                to: '/tasks?overdue=1',
                alert: (tasks.teamOverdue ?? 0) > 0,
              },
              { label: 'Geblokkeerd', value: String(tasks.teamBlocked ?? 0), to: '/tasks?status=Blocked' },
              { label: 'Wacht op controle', value: String(tasks.teamWaitingReview ?? 0), to: '/tasks?review=1' },
            ]
          : []),
      ]
    },
  },
  {
    id: 'facturatie',
    title: 'Facturatie & administratie',
    audience: ['invoices.view'],
    tiles: (data) => [
      {
        label: 'Omzet deze maand',
        value: euro(data.revenueInvoicedThisMonth),
        hint: `${data.ordersCompletedThisMonth} opdrachten afgerond`,
        to: '/invoices',
      },
      {
        label: 'Openstaand',
        value: euro(data.outstandingAmount),
        hint: data.overdueInvoiceCount > 0 ? `${data.overdueInvoiceCount} vervallen` : 'Geen vervallen facturen',
        to: '/invoices',
        alert: data.overdueInvoiceCount > 0,
      },
      {
        label: 'POD ontbreekt',
        value: String(data.missingPodCount),
        hint: 'afgeronde opdrachten zonder afleverbewijs',
        to: '/transport-orders',
        alert: data.missingPodCount > 0,
      },
    ],
  },
  {
    id: 'vloot',
    title: 'Vloot',
    audience: ['vehicles.view'],
    tiles: (data) => [
      {
        label: 'Voertuigen inzetbaar',
        value: String(data.vehiclesAvailable),
        hint: `${data.maintenanceDueCount + data.inspectionsDueCount} onderhoud/keuring gepland`,
        to: '/fleet',
        alert: data.documentsExpiringCount + data.openDamageCount > 0,
      },
      {
        label: 'Onderhoud te laat',
        value: String(data.overdueMaintenanceCount),
        hint: 'geplande onderhoudsbeurten over datum',
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
    title: 'Personeel',
    audience: ['employees.create', 'employees.edit', 'absences.approve', 'hr_settings.manage'],
    tiles: (data) => [
      {
        label: 'Chauffeurs afwezig vandaag',
        value: String(data.driversAbsentToday),
        to: '/absences',
      },
      {
        label: 'Kwalificaties',
        value: String(data.qualificationsExpiring30d),
        hint:
          data.qualificationsExpired > 0
            ? `vervallen binnen 30 dagen · ${data.qualificationsExpired} verlopen`
            : 'vervallen binnen 30 dagen',
        to: '/qualifications',
        alert: data.qualificationsExpiring30d + data.qualificationsExpired > 0,
      },
    ],
  },
  {
    id: 'management',
    title: 'Management',
    audience: ['kpi.view'],
    tiles: () => [
      { label: "KPI's", value: '→', hint: 'Naar het KPI-dashboard', to: '/kpi' },
      { label: 'Rendement', value: '→', hint: 'Marges en rendement per rit', to: '/profitability' },
    ],
  },
  {
    id: 'communicatie',
    title: 'Communicatie',
    audience: [],
    tiles: (data) => [
      {
        label: 'Nieuwe berichten',
        value: String(data.unreadInternalMessages),
        hint: 'ongelezen interne berichten',
        to: '/inbox',
      },
    ],
  },
]
