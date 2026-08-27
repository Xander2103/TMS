import { useEffect, useState } from 'react'
import { apiClient } from '../../../api/apiClient'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import { formatAuditValues } from '../formatAuditValues'
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
}

interface AuditLogPage {
  items: AuditLogEntry[]
  totalCount: number
  page: number
  pageSize: number
}

/** Translation keys per audit action; unknown actions render their raw code. */
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
}

/**
 * Change history for one entity, read from the tenant audit log. Rendered only for users
 * holding audit_logs.view; others see a short explanation instead.
 */
export function AuditHistoryPanel({ entityType, entityId }: { entityType: string; entityId: string }) {
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
      width: '180px',
      render: (row) => formatDateTime(row.timestamp),
    },
    {
      key: 'action',
      header: t('auditing.panel.columns.action'),
      width: '160px',
      render: (row) => (ACTION_LABELS[row.action] ? t(ACTION_LABELS[row.action]) : row.action),
    },
    {
      key: 'changes',
      header: t('auditing.panel.columns.changes'),
      render: (row) => (
        // The stored change details are historical data and render as-is; only the chrome is translated.
        <div className="audit-history-values">
          {row.oldValuesJson && (
            <div className="audit-history-old">
              {t('auditing.panel.before')} {formatAuditValues(row.oldValuesJson)}
            </div>
          )}
          {row.newValuesJson && (
            <div>
              {t('auditing.panel.after')} {formatAuditValues(row.newValuesJson)}
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
