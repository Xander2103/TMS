import { useEffect, useState } from 'react'
import { apiClient } from '../../../api/apiClient'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { auditChanges, auditSummary, type AuditReferences, type AuditValueLabels } from '../formatAuditValues'

const PAGE_SIZE = 25

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
  /** B25: ids inside the values resolved to human references (order number, name, …). */
  references?: AuditReferences | null
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
  PhotoAttached: 'auditing.action.PhotoAttached',
  PhotoDeleted: 'auditing.action.PhotoDeleted',
}

/** Per-entity catalogue prefix tried for actions ACTION_LABELS does not know. */
const ENTITY_ACTION_PREFIX: Record<string, string> = {
  TransportDossier: 'dossiers.history',
  Incident: 'incidents.history',
  ExecutionException: 'exceptions.history',
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
  /**
   * A domain-history endpoint of the entity itself (e.g. /api/incidents/{id}/history): readable by
   * everyone who may see the record — when / who / what, sensitive fields redacted server-side —
   * instead of the raw audit log that needs audit_logs.view.
   */
  historyPath?: string
}

export function AuditHistoryPanel({ entityType, entityId, valueLabels, historyPath }: AuditHistoryPanelProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canView = !!historyPath || hasPermission('audit_logs.view')
  const [page, setPage] = useState(1)
  const [loaded, setLoaded] = useState<{ key: string; data: AuditLogPage | null; error: string | null } | null>(null)
  const requestKey = `${historyPath ?? entityType}:${entityId}:${page}`

  useEffect(() => {
    if (!canView) return
    let mounted = true
    apiClient
      .getJson<AuditLogPage>(
        historyPath
          ? `${historyPath}?page=${page}&pageSize=${PAGE_SIZE}`
          : `/api/audit-logs?entityType=${encodeURIComponent(entityType)}&entityId=${encodeURIComponent(entityId)}&page=${page}&pageSize=${PAGE_SIZE}`,
      )
      .then((data) => {
        if (mounted) setLoaded({ key: requestKey, data, error: null })
      })
      .catch(() => {
        if (mounted) setLoaded({ key: requestKey, data: null, error: t('auditing.panel.loadFailed') })
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canView, requestKey])

  if (!canView) {
    return <p className="placeholder-text">{t('auditing.panel.noPermission')}</p>
  }

  const current = loaded?.key === requestKey ? loaded : null
  const items: HistoryItem[] | null = current?.data
    ? current.data.items.map((row) => {
        const changes = auditChanges(row.oldValuesJson, row.newValuesJson, { valueLabels, references: row.references ?? undefined })
        return {
          id: row.id,
          timestamp: row.timestamp,
          // No user id = no person acted (an automatic pipeline event); never a raw id as a name.
          actorName: row.userId ? (row.userName ?? t('auditing.panel.unknownUser')) : null,
          action: actionLabel(t, row.entityType, row.action),
          summary: auditSummary(changes),
          changes: changes.map(({ field, before, after }) => ({ field, before, after })),
        }
      })
    : null

  return (
    <HistoryTimeline
      items={current?.error ? [] : items}
      loading={!current}
      error={current?.error ?? null}
      emptyMessage={t('auditing.panel.empty')}
      loadingMessage={t('auditing.panel.loading')}
      actorLine={(item) =>
        item.actorName
          ? t('ui.history.actorLine', { action: item.action, name: item.actorName })
          : t('auditing.panel.systemLine', { action: item.action })
      }
      pagination={current?.data ? { page, pageSize: PAGE_SIZE, totalCount: current.data.totalCount, onPageChange: setPage } : undefined}
    />
  )
}
