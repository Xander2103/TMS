import { useEffect, useMemo, useRef, useState } from 'react'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { RowActionMenu } from '../../../components/ui/RowActionMenu'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  deleteQualificationDocument,
  downloadQualificationDocument,
  uploadQualificationDocument,
} from '../api/qualificationsApi'
import { useEmployeeQualifications } from '../hooks/useEmployeeQualifications'
import { formatDate } from '../../../utils/dates'
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
  /** Deep-link target (`?qualificationId=`): that row is highlighted and scrolled into view. */
  highlightQualificationId?: string | null
  /** Fired after a successful create/edit/verify/suspend so the host can refresh derived data
   * (the expiry-warning strip). */
  onChanged?: () => void
}

const STATUS_FILTERS: Array<QualificationStatus | 'all'> = ['all', 'Valid', 'ExpiringSoon', 'Expired', 'Pending', 'Suspended']

export function QualificationsTab({ employeeId, highlightQualificationId, onChanged }: QualificationsTabProps) {
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
  const [uploadTarget, setUploadTarget] = useState<EmployeeQualification | null>(null)
  const [removeDocumentTarget, setRemoveDocumentTarget] = useState<EmployeeQualification | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const visible = useMemo(() => {
    const rows = statusFilter === 'all' ? qualifications : qualifications.filter((q) => q.effectiveStatus === statusFilter)
    return [...rows].sort((a, b) => (a.expiryDate ?? '9999').localeCompare(b.expiryDate ?? '9999'))
  }, [qualifications, statusFilter])

  // Scroll the deep-linked row into view once the table is there (and again when the target
  // changes). Optional call: jsdom has no scrollIntoView implementation.
  useEffect(() => {
    if (!highlightQualificationId || isLoading) return
    document
      .getElementById(`employee-qualification-${highlightQualificationId}`)
      ?.scrollIntoView?.({ block: 'center', behavior: 'smooth' })
  }, [highlightQualificationId, isLoading, visible])

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

  async function removeDocument(row: EmployeeQualification) {
    try {
      await deleteQualificationDocument(employeeId, row.id)
      toast.showSuccess(t('employees.qualifications.documentDeleted'))
      setRemoveDocumentTarget(null)
      reload()
    } catch {
      toast.showError(t('employees.qualifications.documentDeleteFailed'))
    }
  }

  function download(row: EmployeeQualification) {
    downloadQualificationDocument(employeeId, row.id, t).catch(() => toast.showError(t('employees.errors.downloadFailed')))
  }

  function pickDocument(row: EmployeeQualification) {
    setUploadTarget(row)
    fileInputRef.current?.click()
  }

  // B15: one row = one line. The type carries its document number inline; the document column
  // holds at most ONE direct action (download, or upload when there is none yet); every other
  // action (edit, verify, replace/remove document, suspend) lives in the row's ⋯ menu.
  const columns: Column<EmployeeQualification>[] = [
    {
      key: 'type',
      header: t('employees.qualifications.columnType'),
      render: (row) => (
        <TruncatedText tooltip={[row.qualificationTypeName, row.documentNumber].filter(Boolean).join(' · ')}>
          <span className="qualification-type-name">{row.qualificationTypeName}</span>
          {row.documentNumber && <span className="qualification-doc-number"> · {row.documentNumber}</span>}
        </TruncatedText>
      ),
    },
    { key: 'obtained', header: t('employees.qualifications.columnObtained'), width: '110px', render: (row) => <span className="ui-nowrap">{formatDate(row.obtainedDate)}</span> },
    { key: 'expiry', header: t('employees.qualifications.columnExpiry'), width: '110px', render: (row) => <span className="ui-nowrap">{row.expiryDate ? formatDate(row.expiryDate) : '—'}</span> },
    { key: 'country', header: t('employees.qualifications.columnCountry'), width: '70px', render: (row) => row.issuingCountryCode ?? '—' },
    {
      key: 'status',
      header: t('employees.qualifications.columnStatus'),
      width: '150px',
      render: (row) => (
        <Badge tone={QUALIFICATION_STATUS_TONES[row.effectiveStatus]}>
          {t(`employees.qualificationStatus.${row.effectiveStatus}`)}
        </Badge>
      ),
    },
    {
      key: 'document',
      header: t('employees.qualifications.columnDocument'),
      width: '130px',
      render: (row) =>
        documentBusyId === row.id ? (
          <span className="qualification-link is-busy">{t('employees.qualifications.busy')}</span>
        ) : row.hasDocument ? (
          <button type="button" className="qualification-link" onClick={() => download(row)}>
            {t('employees.qualifications.download')}
          </button>
        ) : canEdit ? (
          <button type="button" className="qualification-link" disabled={documentBusyId !== null} onClick={() => pickDocument(row)}>
            {t('employees.qualifications.upload')}
          </button>
        ) : (
          <span className="qualification-doc-none">—</span>
        ),
    },
    {
      key: 'actions',
      header: '',
      width: '52px',
      render: (row) => (
        <RowActionMenu
          label={t('employees.qualifications.rowActions', { name: row.qualificationTypeName })}
          items={[
            ...(canEdit
              ? [{ key: 'edit', label: t('employees.qualifications.edit'), onSelect: () => setDialog({ mode: 'edit', qualification: row }), disabled: mutations.isSubmitting }]
              : []),
            ...(canApprove && row.storedStatus !== 'Valid'
              ? [
                  {
                    key: 'verify',
                    label: t('employees.qualifications.verify'),
                    disabled: mutations.isSubmitting,
                    onSelect: async () => {
                      const saved = await mutations.verify(employeeId, row.id)
                      if (saved) {
                        toast.showSuccess(t('employees.qualifications.verified'))
                        reload()
                        onChanged?.()
                      }
                    },
                  },
                ]
              : []),
            ...(canEdit && row.hasDocument
              ? [{ key: 'replace', label: t('employees.qualifications.replace'), onSelect: () => pickDocument(row), disabled: documentBusyId !== null }]
              : []),
            ...(row.hasDocument && canDeleteDocument
              ? [{ key: 'remove-document', label: t('employees.qualifications.removeDocument'), danger: true, onSelect: () => setRemoveDocumentTarget(row) }]
              : []),
            ...(canEdit && row.storedStatus !== 'Suspended'
              ? [{ key: 'suspend', label: t('employees.qualifications.suspend'), danger: true, onSelect: () => setSuspendTarget(row), disabled: mutations.isSubmitting }]
              : []),
          ]}
        />
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
        {canCreate && <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>{t('employees.qualifications.add')}</Button>}
      </div>

      <DataTable
        columns={columns}
        rows={visible}
        rowKey={(row) => row.id}
        rowId={(row) => `employee-qualification-${row.id}`}
        rowClassName={(row) => (row.id === highlightQualificationId ? 'is-highlighted' : undefined)}
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
            onChanged?.()
          }}
          onCancel={() => setDialog(null)}
        />
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept=".pdf,.jpg,.jpeg,.png"
        hidden
        data-testid="qualification-document-input"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = ''
          if (file && uploadTarget) void handleUpload(uploadTarget, file)
          setUploadTarget(null)
        }}
      />

      {removeDocumentTarget && (
        <ConfirmDialog
          title={t('employees.qualifications.removeDocumentTitle')}
          message={t('employees.qualifications.removeDocumentMessage', { name: removeDocumentTarget.qualificationTypeName })}
          confirmLabel={t('employees.qualifications.removeDocument')}
          destructive
          onConfirm={() => removeDocument(removeDocumentTarget)}
          onCancel={() => setRemoveDocumentTarget(null)}
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
              onChanged?.()
            }
          }}
          onCancel={() => setSuspendTarget(null)}
        />
      )}
    </div>
  )
}
