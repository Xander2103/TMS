import { useMemo, useState } from 'react'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
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
  QUALIFICATION_STATUS_LABELS,
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
      await uploadQualificationDocument(employeeId, qualification.id, file)
      toast.showSuccess('Document opgeslagen.')
      reload()
    } catch (err) {
      toast.showError(err instanceof Error ? err.message : 'Uploaden is mislukt.')
    } finally {
      setDocumentBusyId(null)
    }
  }

  if (isLoading) return <LoadingState message="Kwalificaties laden..." />
  if (error) return <ErrorState message={error} />

  const columns: Column<EmployeeQualification>[] = [
    {
      key: 'type',
      header: 'Type',
      render: (row) => (
        <div>
          <div className="qualification-type-name">{row.qualificationTypeName}</div>
          {row.documentNumber && <div className="qualification-doc-number">{row.documentNumber}</div>}
        </div>
      ),
    },
    { key: 'obtained', header: 'Behaald', width: '110px', render: (row) => row.obtainedDate },
    { key: 'expiry', header: 'Vervalt', width: '110px', render: (row) => row.expiryDate ?? '—' },
    { key: 'country', header: 'Land', width: '70px', render: (row) => row.issuingCountryCode ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '170px',
      render: (row) => (
        <Badge tone={QUALIFICATION_STATUS_TONES[row.effectiveStatus]}>
          {QUALIFICATION_STATUS_LABELS[row.effectiveStatus]}
        </Badge>
      ),
    },
    {
      key: 'document',
      header: 'Document',
      width: '210px',
      render: (row) => (
        <div className="qualification-doc-actions">
          {row.hasDocument && (
            <button
              type="button"
              className="qualification-link"
              onClick={() => downloadQualificationDocument(employeeId, row.id).catch(() => toast.showError('Download mislukt.'))}
            >
              Downloaden
            </button>
          )}
          {canEdit && (
            <label className={documentBusyId === row.id ? 'qualification-link is-busy' : 'qualification-link'}>
              {documentBusyId === row.id ? 'Bezig…' : row.hasDocument ? 'Vervangen' : 'Uploaden'}
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
                  toast.showSuccess('Document verwijderd.')
                  reload()
                } catch {
                  toast.showError('Document kon niet worden verwijderd.')
                }
              }}
            >
              Verwijderen
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
              Bewerken
            </Button>
          )}
          {canApprove && row.storedStatus !== 'Valid' && (
            <Button
              variant="ghost"
              onClick={async () => {
                const saved = await mutations.verify(employeeId, row.id)
                if (saved) {
                  toast.showSuccess('Kwalificatie geverifieerd.')
                  reload()
                }
              }}
              disabled={mutations.isSubmitting}
            >
              Verifiëren
            </Button>
          )}
          {canEdit && row.storedStatus !== 'Suspended' && (
            <Button variant="ghost" onClick={() => setSuspendTarget(row)} disabled={mutations.isSubmitting}>
              Intrekken
            </Button>
          )}
        </div>
      ),
    },
  ]

  return (
    <div className="qualifications-tab">
      <div className="qualifications-toolbar">
        <div className="qualifications-filters" role="group" aria-label="Filter op status">
          {STATUS_FILTERS.map((status) => (
            <button
              key={status}
              type="button"
              className={statusFilter === status ? 'qualification-filter is-active' : 'qualification-filter'}
              onClick={() => setStatusFilter(status)}
            >
              {status === 'all' ? 'Alle' : QUALIFICATION_STATUS_LABELS[status]}
            </button>
          ))}
        </div>
        {canCreate && <Button onClick={() => setDialog({ mode: 'create' })}>Kwalificatie toevoegen</Button>}
      </div>

      <DataTable
        columns={columns}
        rows={visible}
        rowKey={(row) => row.id}
        emptyMessage={
          statusFilter === 'all' ? 'Er zijn nog geen kwalificaties geregistreerd.' : 'Geen kwalificaties met deze status.'
        }
      />

      {dialog && (
        <QualificationDialog
          employeeId={employeeId}
          existing={dialog.mode === 'edit' ? dialog.qualification : undefined}
          onSaved={() => {
            setDialog(null)
            toast.showSuccess('Kwalificatie opgeslagen.')
            reload()
          }}
          onCancel={() => setDialog(null)}
        />
      )}

      {suspendTarget && (
        <ConfirmDialog
          title="Kwalificatie intrekken"
          message={`'${suspendTarget.qualificationTypeName}' intrekken? De kwalificatie telt dan niet meer mee voor inzetbaarheid.`}
          confirmLabel="Intrekken"
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const saved = await mutations.suspend(employeeId, suspendTarget.id)
            if (saved) {
              toast.showSuccess('Kwalificatie ingetrokken.')
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
