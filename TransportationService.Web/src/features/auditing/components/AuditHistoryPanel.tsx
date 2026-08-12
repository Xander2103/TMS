import { useEffect, useState } from 'react'
import { apiClient } from '../../../api/apiClient'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useAuth } from '../../auth/authContextValue'
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

const ACTION_LABELS: Record<string, string> = {
  Created: 'Aangemaakt',
  Updated: 'Bijgewerkt',
  Deleted: 'Verwijderd',
  Deactivated: 'Gedeactiveerd',
  Reactivated: 'Geheractiveerd',
  Blocked: 'Geblokkeerd',
  Unblocked: 'Gedeblokkeerd',
  Cancelled: 'Geannuleerd',
  StatusChanged: 'Status gewijzigd',
  AssignmentChanged: 'Toewijzing gewijzigd',
}

/**
 * Change history for one entity, read from the tenant audit log. Rendered only for users
 * holding audit_logs.view; others see a short explanation instead.
 */
export function AuditHistoryPanel({ entityType, entityId }: { entityType: string; entityId: string }) {
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
        if (mounted) setError('Historiek kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [canView, entityType, entityId])

  if (!canView) {
    return <p className="placeholder-text">Je hebt geen rechten om de historiek te bekijken.</p>
  }

  const columns: Column<AuditLogEntry>[] = [
    {
      key: 'timestamp',
      header: 'Tijdstip',
      width: '180px',
      render: (row) => formatDateTime(row.timestamp),
    },
    { key: 'action', header: 'Actie', width: '160px', render: (row) => ACTION_LABELS[row.action] ?? row.action },
    {
      key: 'changes',
      header: 'Wijziging',
      render: (row) => (
        <div className="audit-history-values">
          {row.oldValuesJson && <div className="audit-history-old">Voor: {formatAuditValues(row.oldValuesJson)}</div>}
          {row.newValuesJson && <div>Na: {formatAuditValues(row.newValuesJson)}</div>}
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
      emptyMessage="Nog geen historiek voor dit item."
      loadingMessage="Historiek laden…"
    />
  )
}
