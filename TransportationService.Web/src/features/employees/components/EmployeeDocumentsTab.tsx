import { useMemo, useRef, useState } from 'react'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { formatFileSize } from '../../invoices/utils/fileSize'
import { useEmployeeDocuments } from '../hooks/useEmployeeDocuments'
import { classifyExpiry } from '../utils/documentExpiry'
import {
  EMPLOYEE_DOCUMENT_ACCEPT,
  EMPLOYEE_DOCUMENT_CATEGORY_LABELS,
  EMPLOYEE_DOCUMENT_CATEGORY_ORDER,
  deleteEmployeeDocument,
  downloadEmployeeDocument,
  replaceEmployeeDocumentFile,
  setEmployeeDocumentArchived,
  uploadEmployeeDocument,
  type EmployeeDocument,
  type EmployeeDocumentCategory,
} from '../api/employeeDocumentsApi'
import './EmployeeDocumentsTab.css'

interface EmployeeDocumentsTabProps {
  employeeId: string
}

export function EmployeeDocumentsTab({ employeeId }: EmployeeDocumentsTabProps) {
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canCreate = hasPermission('employee_documents.create')
  const canEdit = hasPermission('employee_documents.edit')
  const canDelete = hasPermission('employee_documents.delete')
  const canViewSensitive = hasPermission('employee_documents.view_sensitive')

  const [includeArchived, setIncludeArchived] = useState(false)
  const { documents, isLoading, error, reload } = useEmployeeDocuments(employeeId, includeArchived)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<EmployeeDocument | null>(null)

  // Upload form state.
  const [uploadCategory, setUploadCategory] = useState<EmployeeDocumentCategory>('AdditionalDocument')
  const [uploadExpiry, setUploadExpiry] = useState('')
  const [uploadNotes, setUploadNotes] = useState('')
  const [uploading, setUploading] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const cameraInputRef = useRef<HTMLInputElement>(null)

  const grouped = useMemo(() => {
    const byCategory = new Map<EmployeeDocumentCategory, EmployeeDocument[]>()
    for (const doc of documents) {
      const list = byCategory.get(doc.category) ?? []
      list.push(doc)
      byCategory.set(doc.category, list)
    }
    return EMPLOYEE_DOCUMENT_CATEGORY_ORDER.filter((category) => byCategory.has(category)).map((category) => ({
      category,
      docs: (byCategory.get(category) ?? []).sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt)),
    }))
  }, [documents])

  async function handleUpload(file: File) {
    setUploading(true)
    try {
      await uploadEmployeeDocument(
        employeeId,
        {
          file,
          category: uploadCategory,
          expiryDate: uploadExpiry || null,
          notes: uploadNotes.trim() || null,
        },
        t,
      )
      toast.showSuccess(t('employees.documents.saved'))
      setUploadExpiry('')
      setUploadNotes('')
      void reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('employees.errors.uploadFailed')).message)
    } finally {
      setUploading(false)
    }
  }

  async function handleReplace(doc: EmployeeDocument, file: File) {
    setBusyId(doc.id)
    try {
      await replaceEmployeeDocumentFile(employeeId, doc.id, file, t)
      toast.showSuccess(t('employees.documents.fileReplaced'))
      void reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('employees.errors.replaceFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleArchive(doc: EmployeeDocument) {
    setBusyId(doc.id)
    try {
      await setEmployeeDocumentArchived(employeeId, doc.id, !doc.isArchived)
      toast.showSuccess(doc.isArchived ? t('employees.documents.restored') : t('employees.documents.archived'))
      void reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('employees.documents.actionFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  if (isLoading) return <LoadingState message={t('employees.documents.loading')} />
  if (error) return <ErrorState message={error} />

  return (
    <div className="employee-documents-tab">
      {!canViewSensitive && (
        <p className="employee-documents-info" role="note">
          {t('employees.documents.sensitiveHidden')}
        </p>
      )}

      {canCreate && (
        <div className="employee-documents-upload">
          <FormField label={t('employees.documents.uploadCategory')} htmlFor="ed-upload-category">
            <select
              id="ed-upload-category"
              value={uploadCategory}
              onChange={(e) => setUploadCategory(e.target.value as EmployeeDocumentCategory)}
            >
              {EMPLOYEE_DOCUMENT_CATEGORY_ORDER.map((category) => (
                <option key={category} value={category}>
                  {t(EMPLOYEE_DOCUMENT_CATEGORY_LABELS[category])}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('employees.documents.uploadExpiry')} htmlFor="ed-upload-expiry" hint={t('employees.documents.uploadExpiryHint')}>
            <input id="ed-upload-expiry" type="date" value={uploadExpiry} onChange={(e) => setUploadExpiry(e.target.value)} />
          </FormField>
          <FormField label={t('employees.documents.uploadNotes')} htmlFor="ed-upload-notes">
            <input id="ed-upload-notes" value={uploadNotes} onChange={(e) => setUploadNotes(e.target.value)} maxLength={500} />
          </FormField>
          <div className="employee-documents-upload-actions">
            <input
              ref={fileInputRef}
              type="file"
              accept={EMPLOYEE_DOCUMENT_ACCEPT}
              hidden
              onChange={(e) => {
                const file = e.target.files?.[0]
                e.target.value = ''
                if (file) void handleUpload(file)
              }}
            />
            <input
              ref={cameraInputRef}
              type="file"
              accept={EMPLOYEE_DOCUMENT_ACCEPT}
              capture="environment"
              hidden
              onChange={(e) => {
                const file = e.target.files?.[0]
                e.target.value = ''
                if (file) void handleUpload(file)
              }}
            />
            <Button variant="secondary" disabled={uploading} onClick={() => fileInputRef.current?.click()}>
              {uploading ? t('employees.documents.busy') : t('employees.documents.chooseFile')}
            </Button>
            <Button variant="secondary" disabled={uploading} onClick={() => cameraInputRef.current?.click()}>
              {t('employees.documents.takePhoto')}
            </Button>
          </div>
        </div>
      )}

      <div className="employee-documents-toolbar">
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />
          {t('employees.documents.showArchived')}
        </label>
      </div>

      {grouped.length === 0 ? (
        <p className="placeholder-text">{t('employees.documents.empty')}</p>
      ) : (
        grouped.map(({ category, docs }) => (
          <section key={category} className="employee-documents-group">
            <h3 className="employee-documents-group-title">{t(EMPLOYEE_DOCUMENT_CATEGORY_LABELS[category])}</h3>
            <ul className="employee-documents-list">
              {docs.map((doc) => {
                const expiry = classifyExpiry(doc.expiryDate)
                return (
                  <li key={doc.id} className={doc.isArchived ? 'employee-document is-archived' : 'employee-document'}>
                    <div className="employee-document-main">
                      <span className="employee-document-name">{doc.customLabel || doc.fileName}</span>
                      <span className="employee-document-meta">
                        {formatFileSize(doc.sizeBytes)}
                        {doc.expiryDate && <> · {t('employees.documents.expiresOn', { date: doc.expiryDate })}</>}
                      </span>
                    </div>
                    <div className="employee-document-badges">
                      {doc.isArchived && <Badge tone="neutral">{t('employees.documents.archivedBadge')}</Badge>}
                      {expiry === 'expired' && <Badge tone="danger">{t('employees.documents.expiredBadge')}</Badge>}
                      {expiry === 'expiring' && <Badge tone="warning">{t('employees.documents.expiringBadge')}</Badge>}
                    </div>
                    <div className="employee-document-actions">
                      <button
                        type="button"
                        className="employee-document-link"
                        onClick={() =>
                          downloadEmployeeDocument(employeeId, doc.id, doc.fileName, t).catch(() =>
                            toast.showError(t('employees.errors.downloadFailed')),
                          )
                        }
                      >
                        {t('employees.documents.download')}
                      </button>
                      {canEdit && (
                        <label className={busyId === doc.id ? 'employee-document-link is-busy' : 'employee-document-link'}>
                          {busyId === doc.id ? t('employees.documents.busy') : t('employees.documents.replace')}
                          <input
                            type="file"
                            accept={EMPLOYEE_DOCUMENT_ACCEPT}
                            hidden
                            disabled={busyId !== null}
                            onChange={(e) => {
                              const file = e.target.files?.[0]
                              e.target.value = ''
                              if (file) void handleReplace(doc, file)
                            }}
                          />
                        </label>
                      )}
                      {canEdit && (
                        <button
                          type="button"
                          className="employee-document-link"
                          disabled={busyId !== null}
                          onClick={() => void handleArchive(doc)}
                        >
                          {doc.isArchived ? t('employees.documents.restore') : t('employees.documents.archive')}
                        </button>
                      )}
                      {canDelete && (
                        <button
                          type="button"
                          className="employee-document-link employee-document-link-danger"
                          disabled={busyId !== null}
                          onClick={() => setDeleteTarget(doc)}
                        >
                          {t('employees.documents.remove')}
                        </button>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          </section>
        ))
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('employees.documents.deleteTitle')}
          message={t('employees.documents.deleteMessage', { name: deleteTarget.customLabel || deleteTarget.fileName })}
          confirmLabel={t('employees.documents.deleteConfirm')}
          destructive
          busy={busyId === deleteTarget.id}
          onConfirm={async () => {
            setBusyId(deleteTarget.id)
            try {
              await deleteEmployeeDocument(employeeId, deleteTarget.id)
              toast.showSuccess(t('employees.documents.deleted'))
              setDeleteTarget(null)
              void reload()
            } catch (err) {
              toast.showError(describeApiError(err, t('employees.documents.deleteFailed')).message)
            } finally {
              setBusyId(null)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
