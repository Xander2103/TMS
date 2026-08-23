import { useMemo, useState } from 'react'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  deleteQualificationDocument,
  downloadQualificationDocument,
  uploadQualificationDocument,
} from '../api/qualificationsApi'
import { useEmployeeQualifications } from '../hooks/useEmployeeQualifications'
import { useQualificationMutations } from '../hooks/useQualificationMutations'
import { QualificationDialog } from './QualificationDialog'
import {
  QUALIFICATION_STATUS_TONES,
  type EmployeeQualification,
  type QualificationStatus,
} from '../types/qualification'
import './QualificationsTab.css'

interface QualificationsTabProps {
  employeeId: string
}

const STATUS_FILTERS: Array<QualificationStatus | 'all'> = ['all', 'Valid', 'ExpiringSoon', 'Expired', 'Pending', 'Suspended']

export function QualificationsTab({ employeeId }: QualificationsTabProps) {
  const { qualifications, isLoading, error, reload } = useEmployeeQualifications(employeeId)
  const mutations = useQualificationMutations()
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canCreate = hasPermission('employee_documents.create')
  const canEdit = hasPermission('employee_documents.edit')
  const canApprove = hasPermission('employee_documents.approve')
  const canDeleteDocument = hasPermission('employee_documents.delete')

  const [dialog, setDialog] = useState<{ mode: 'create' } | { mode: 'edit'; qualification: EmployeeQualification } | null>(null)
  const [suspendTarget, setSuspendTarget] = useState<EmployeeQualification | null>(null)
  const [statusFilter, setStatusFilter] = useState<QualificationStatus | 'all'>('all')
  const [documentBusyId, setDocumentBusyId] = useState<string | null>(null)

  const visible = useMemo(() => {
    const rows = statusFilter === 'all' ? qualifications : qualifications.filter((q) => q.effectiveStatus === statusFilter)
    return [...rows].sort((a, b) => (a.expiryDate ?? '9999').localeCompare(b.expiryDate ?? '9999'))
  }, [qualifications, statusFilter])

  async function handleUpload(qualification: EmployeeQualification, file: File) {
    setDocumentBusyId(qualification.id)
    try {
      await uploadQualificationDocument(employeeId, qualification.id, file, t)
      toast.showSuccess(t('employees.qualifications.documentSaved'))
      reload()
    } catch (err) {
      toast.showError(err instanceof Error ? err.message : t('employees.errors.uploadFailed'))
    } finally {
      setDocumentBusyId(null)
    }
  }

  if (isLoading) return <LoadingState message={t('employees.qualifications.loading')} />
  if (error) return <ErrorState message={error} />

  const columns: Column<EmployeeQualification>[] = [
    {
      key: 'type',
      header: t('employees.qualifications.columnType'),
      render: (row) => (
        <div>
          <div className="qualification-type-name">{row.qualificationTypeName}</div>
          {row.documentNumber && <div className="qualification-doc-number">{row.documentNumber}</div>}
        </div>
      ),
    },
    { key: 'obtained', header: t('employees.qualifications.columnObtained'), width: '110px', render: (row) => row.obtainedDate },
    { key: 'expiry', header: t('employees.qualifications.columnExpiry'), width: '110px', render: (row) => row.expiryDate ?? '—' },
    { key: 'country', header: t('employees.qualifications.columnCountry'), width: '70px', render: (row) => row.issuingCountryCode ?? '—' },
    {
      key: 'status',
      header: t('employees.qualifications.columnStatus'),
      width: '170px',
      render: (row) => (
        <Badge tone={QUALIFICATION_STATUS_TONES[row.effectiveStatus]}>
          {t(`employees.qualificationStatus.${row.effectiveStatus}`)}
        </Badge>
      ),
    },
    {
      key: 'document',
      header: t('employees.qualifications.columnDocument'),
      width: '210px',
      render: (row) => (
        <div className="qualification-doc-actions">
          {row.hasDocument && (
            <button
              type="button"
              className="qualification-link"
              onClick={() => downloadQualificationDocument(employeeId, row.id, t).catch(() => toast.showError(t('employees.errors.downloadFailed')))}
            >
              {t('employees.qualifications.download')}
            </button>
          )}
          {canEdit && (
            <label className={documentBusyId === row.id ? 'qualification-link is-busy' : 'qualification-link'}>
              {documentBusyId === row.id
                ? t('employees.qualifications.busy')
                : row.hasDocument
                  ? t('employees.qualifications.replace')
                  : t('employees.qualifications.upload')}
              <input
                type="file"
                accept=".pdf,.jpg,.jpeg,.png"
                hidden
                disabled={documentBusyId !== null}
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  e.target.value = ''
                  if (file) void handleUpload(row, file)
                }}
              />
            </label>
          )}
          {row.hasDocument && canDeleteDocument && (
            <button
              type="button"
              className="qualification-link qualification-link-danger"
              onClick={async () => {
                try {
                  await deleteQualificationDocument(employeeId, row.id)
                  toast.showSuccess(t('employees.qualifications.documentDeleted'))
                  reload()
                } catch {
                  toast.showError(t('employees.qualifications.documentDeleteFailed'))
                }
              }}
            >
              {t('employees.qualifications.remove')}
            </button>
          )}
        </div>
      ),
    },
    {
      key: 'actions',
      header: '',
      width: '230px',
      render: (row) => (
        <div className="qualification-actions">
          {canEdit && (
            <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', qualification: row })} disabled={mutations.isSubmitting}>
              {t('employees.qualifications.edit')}
            </Button>
          )}
          {canApprove && row.storedStatus !== 'Valid' && (
            <Button
              variant="ghost"
              onClick={async () => {
                const saved = await mutations.verify(employeeId, row.id)
                if (saved) {
                  toast.showSuccess(t('employees.qualifications.verified'))
                  reload()
                }
              }}
              disabled={mutations.isSubmitting}
            >
              {t('employees.qualifications.verify')}
            </Button>
          )}
          {canEdit && row.storedStatus !== 'Suspended' && (
            <Button variant="ghost" onClick={() => setSuspendTarget(row)} disabled={mutations.isSubmitting}>
              {t('employees.qualifications.suspend')}
            </Button>
          )}
        </div>
      ),
    },
  ]

  return (
    <div className="qualifications-tab">
      <div className="qualifications-toolbar">
        <div className="qualifications-filters" role="group" aria-label={t('employees.qualifications.filterLabel')}>
          {STATUS_FILTERS.map((status) => (
            <button
              key={status}
              type="button"
              className={statusFilter === status ? 'qualification-filter is-active' : 'qualification-filter'}
              onClick={() => setStatusFilter(status)}
            >
              {status === 'all' ? t('employees.qualifications.filterAll') : t(`employees.qualificationStatus.${status}`)}
            </button>
          ))}
        </div>
        {canCreate && <Button onClick={() => setDialog({ mode: 'create' })}>{t('employees.qualifications.add')}</Button>}
      </div>

      <DataTable
        columns={columns}
        rows={visible}
        rowKey={(row) => row.id}
        emptyMessage={
          statusFilter === 'all' ? t('employees.qualifications.empty') : t('employees.qualifications.emptyFiltered')
        }
      />

      {dialog && (
        <QualificationDialog
          employeeId={employeeId}
          existing={dialog.mode === 'edit' ? dialog.qualification : undefined}
          onSaved={() => {
            setDialog(null)
            toast.showSuccess(t('employees.qualifications.saved'))
            reload()
          }}
          onCancel={() => setDialog(null)}
        />
      )}

      {suspendTarget && (
        <ConfirmDialog
          title={t('employees.qualifications.suspendTitle')}
          message={t('employees.qualifications.suspendMessage', { name: suspendTarget.qualificationTypeName })}
          confirmLabel={t('employees.qualifications.suspendConfirm')}
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const saved = await mutations.suspend(employeeId, suspendTarget.id)
            if (saved) {
              toast.showSuccess(t('employees.qualifications.suspended'))
              setSuspendTarget(null)
              reload()
            }
          }}
          onCancel={() => setSuspendTarget(null)}
        />
      )}
    </div>
  )
}
