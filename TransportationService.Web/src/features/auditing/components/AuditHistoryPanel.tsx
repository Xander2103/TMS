import { useEffect, useState } from 'react'
import { apiClient } from '../../../api/apiClient'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import { formatAuditValues, type AuditValueLabels } from '../formatAuditValues'
import './AuditHistoryPanel.css'

interface AuditLogEntry {
  id: string
  userId: string | null
  entityType: string
  entityId: string
  action: string
  oldValuesJson: string | null
  newValuesJson: string | null
  timestamp: string
  /** Acting person; null = no human actor (automatic pipeline event) or unknown. Absent on older payloads. */
  userName?: string | null
}

interface AuditLogPage {
  items: AuditLogEntry[]
  totalCount: number
  page: number
  pageSize: number
}

/**
 * Translation keys per audit action. Unknown actions fall back to the entity's own catalogue
 * (`dossiers.history.<action>` for a dossier — ConfirmedManually, Reopened, OrderLinked, …) and
 * finally to their raw code.
 */
const ACTION_LABELS: Record<string, string> = {
  Created: 'auditing.action.Created',
  Updated: 'auditing.action.Updated',
  Deleted: 'auditing.action.Deleted',
  Deactivated: 'auditing.action.Deactivated',
  Reactivated: 'auditing.action.Reactivated',
  Blocked: 'auditing.action.Blocked',
  Unblocked: 'auditing.action.Unblocked',
  Cancelled: 'auditing.action.Cancelled',
  StatusChanged: 'auditing.action.StatusChanged',
  AssignmentChanged: 'auditing.action.AssignmentChanged',
  Reported: 'auditing.action.Reported',
  Resolved: 'auditing.action.Resolved',
  Rejected: 'auditing.action.Rejected',
  Assigned: 'auditing.action.Assigned',
  Unassigned: 'auditing.action.Unassigned',
  Archived: 'auditing.action.Archived',
}

/** Per-entity catalogue prefix tried for actions ACTION_LABELS does not know. */
const ENTITY_ACTION_PREFIX: Record<string, string> = {
  TransportDossier: 'dossiers.history',
}

function actionLabel(t: (key: string) => string, entityType: string, action: string): string {
  if (ACTION_LABELS[action]) return t(ACTION_LABELS[action])
  const prefix = ENTITY_ACTION_PREFIX[entityType]
  if (prefix) {
    // `t` echoes the key itself when it is missing (same pattern as localizeApiError).
    const key = `${prefix}.${action}`
    const translated = t(key)
    if (translated !== key) return translated
  }
  return action
}

/**
 * Change history for one entity, read from the tenant audit log. Rendered only for users
 * holding audit_logs.view; others see a short explanation instead.
 */
interface AuditHistoryPanelProps {
  entityType: string
  entityId: string
  /** Entity-specific value renderers (enum ordinals → labels, codes → names). */
  valueLabels?: AuditValueLabels
}

export function AuditHistoryPanel({ entityType, entityId, valueLabels }: AuditHistoryPanelProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canView = hasPermission('audit_logs.view')
  const [data, setData] = useState<AuditLogPage | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!canView) return
    let mounted = true
    apiClient
      .getJson<AuditLogPage>(`/api/audit-logs?entityType=${encodeURIComponent(entityType)}&entityId=${encodeURIComponent(entityId)}&pageSize=50`)
      .then((page) => {
        if (mounted) setData(page)
      })
      .catch(() => {
        if (mounted) setError(t('auditing.panel.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canView, entityType, entityId])

  if (!canView) {
    return <p className="placeholder-text">{t('auditing.panel.noPermission')}</p>
  }

  const columns: Column<AuditLogEntry>[] = [
    {
      key: 'timestamp',
      header: t('auditing.panel.columns.timestamp'),
      width: '150px',
      render: (row) => formatDateTime(row.timestamp),
    },
    {
      key: 'action',
      header: t('auditing.panel.columns.action'),
      width: '150px',
      render: (row) => actionLabel(t, row.entityType, row.action),
    },
    {
      key: 'actor',
      header: t('auditing.panel.columns.actor'),
      width: '140px',
      // No user id = no person acted (an automatic confirmation by the pipeline); never a made-up name.
      render: (row) => (row.userId ? (row.userName ?? row.userId) : <span className="placeholder-text">{t('auditing.panel.system')}</span>),
    },
    {
      key: 'changes',
      header: t('auditing.panel.columns.changes'),
      render: (row) => (
        // Stored change details are historical data; they are humanized for reading, never edited.
        <div className="audit-history-values">
          {row.oldValuesJson && (
            <div className="audit-history-old">
              {t('auditing.panel.before')} {formatAuditValues(row.oldValuesJson, valueLabels)}
            </div>
          )}
          {row.newValuesJson && (
            <div>
              {t('auditing.panel.after')} {formatAuditValues(row.newValuesJson, valueLabels)}
            </div>
          )}
        </div>
      ),
    },
  ]

  return (
    <DataTable
      columns={columns}
      rows={data?.items ?? []}
      rowKey={(row) => row.id}
      isLoading={!data && !error}
      error={error}
      emptyMessage={t('auditing.panel.empty')}
      loadingMessage={t('auditing.panel.loading')}
    />
  )
}
