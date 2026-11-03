import { useState, type ReactNode } from 'react'
import { FileText } from 'lucide-react'
import { Badge } from '../../../components/ui/Badge'
import type { ActionMenuItem } from '../../../components/ui/ActionMenu'
import { EmptyState } from '../../../components/ui/EmptyState'
import { RowActionMenu } from '../../../components/ui/RowActionMenu'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { downloadDossierDocumentFile, fetchDossierDocumentBlob, isViewableDocument, type DossierDocument } from '../api/dossierDocumentsApi'
import { DocumentPager } from './DocumentPager'
import { DEFAULT_DOCUMENT_PAGE_SIZE, documentFileExtension, documentTypeLabel } from './documentList'

export interface DossierDocumentRowActions {
  /** Offered for a document without a file (the metadata exists, the upload failed or was skipped). */
  onAttachFile?: (document: DossierDocument) => void
  onChangeVisibility?: (document: DossierDocument) => void
  onMove?: (document: DossierDocument) => void
  onDelete?: (document: DossierDocument) => void
}

/**
 * The "..." menu of one document. Viewing/downloading goes through the shared auth path; every
 * write action only appears when the host passes a handler, so a read-only list simply passes none.
 */
function useDocumentMenu(actions: DossierDocumentRowActions) {
  const { t } = useLocale()
  const { showError } = useToast()

  function download(document: DossierDocument) {
    downloadDossierDocumentFile(document).catch((err: unknown) =>
      showError(describeApiError(err, t('dossierDocuments.downloadFailed')).message),
    )
  }

  async function view(document: DossierDocument) {
    try {
      const blob = await fetchDossierDocumentBlob(document)
      const url = URL.createObjectURL(blob)
      const opened = window.open(url, '_blank', 'noopener')
      if (!opened) showError(t('dossierDocuments.viewBlocked'))
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (err) {
      showError(describeApiError(err, t('dossierDocuments.viewFailed')).message)
    }
  }

  return (document: DossierDocument): ActionMenuItem[] => {
    const items: ActionMenuItem[] = []
    if (isViewableDocument(document)) items.push({ key: 'view', label: t('dossierDocuments.actions.viewShort'), onSelect: () => void view(document) })
    if (document.hasFile) items.push({ key: 'download', label: t('dossierDocuments.actions.downloadShort'), onSelect: () => download(document) })
    if (!document.hasFile && actions.onAttachFile) items.push({ key: 'attach', label: t('dossierDocuments.upload.retry'), onSelect: () => actions.onAttachFile!(document) })
    if (actions.onChangeVisibility) {
      items.push({
        key: 'visibility',
        label: document.customerVisible ? t('dossierDocuments.visibility.makeInternal') : t('dossierDocuments.visibility.makeVisible'),
        onSelect: () => actions.onChangeVisibility!(document),
      })
    }
    if (actions.onMove) items.push({ key: 'move', label: t('dossierDocuments.move.confirm'), onSelect: () => actions.onMove!(document) })
    if (actions.onDelete) items.push({ key: 'delete', label: t('dossierDocuments.delete.confirm'), onSelect: () => actions.onDelete!(document), danger: true })
    return items
  }
}

interface DossierDocumentTableProps {
  documents: DossierDocument[]
  actions?: DossierDocumentRowActions
  /** Tells the empty-state apart per list; the caller renders it. */
  emptyText: string
  /** B48: the one thing to do when the list is empty (upload), rendered inside the empty state. */
  emptyAction?: ReactNode
}

/** One document table for every view: level lists on the dossier, read-only general list on an order page. */
export function DossierDocumentTable({ documents, actions = {}, emptyText, emptyAction }: DossierDocumentTableProps) {
  const { t, formatDate, formatDateTime } = useLocale()
  const menuFor = useDocumentMenu(actions)

  if (documents.length === 0) return <EmptyState message={emptyText} action={emptyAction} />

  return (
    <div className="ddoc-table-wrap">
      <table className="ddoc-table">
        <thead>
          <tr>
            <th>{t('dossierDocuments.columns.title')}</th>
            <th>{t('dossierDocuments.columns.type')}</th>
            <th>{t('dossierDocuments.columns.file')}</th>
            <th>{t('dossierDocuments.columns.portal')}</th>
            <th>{t('dossierDocuments.columns.uploaded')}</th>
            <th className="ddoc-table-actions">{t('dossierDocuments.columns.actions')}</th>
          </tr>
        </thead>
        <tbody>
          {documents.map((document) => {
            const menu = menuFor(document)
            return (
              <tr key={document.id} data-document-id={document.id} data-scope={document.scope}>
                <td data-label={t('dossierDocuments.columns.title')}>
                  <span className="ddoc-title">{document.title}</span>
                  {document.issueDate && <span className="ddoc-meta">{formatDate(document.issueDate)}</span>}
                  {document.createdByName && <span className="ddoc-meta">{document.createdByName}</span>}
                </td>
                <td data-label={t('dossierDocuments.columns.type')}>
                  <Badge tone="info">{documentTypeLabel(document, t)}</Badge>
                </td>
                <td data-label={t('dossierDocuments.columns.file')}>
                  {document.hasFile ? (
                    <span className="ddoc-file" title={document.fileName ?? undefined}>{document.fileName ?? document.title}</span>
                  ) : (
                    <Badge tone="warning">{t('dossierDocuments.fileMissing')}</Badge>
                  )}
                </td>
                <td data-label={t('dossierDocuments.columns.portal')}>
                  <Badge tone={document.customerVisible ? 'success' : 'neutral'}>
                    {document.customerVisible ? t('dossierDocuments.visibility.visible') : t('dossierDocuments.visibility.internal')}
                  </Badge>
                </td>
                <td data-label={t('dossierDocuments.columns.uploaded')} className="ddoc-date">{formatDateTime(document.createdAt)}</td>
                <td data-label={t('dossierDocuments.columns.actions')} className="ddoc-table-actions">
                  <span className="ddoc-actions">
                    {menu.length > 0 ? (
                      <RowActionMenu items={menu} label={t('dossierDocuments.actions.menu', { title: document.title })} />
                    ) : (
                      <span className="ddoc-meta">—</span>
                    )}
                  </span>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/**
 * The dossier documents as seen from an order or activity: the same records, never copies, so
 * only viewing and downloading. A plain list that cannot be mistaken for the level's own table.
 */
export function DossierSharedDocumentList({ documents }: { documents: DossierDocument[] }) {
  const { t, formatDateTime } = useLocale()
  const menuFor = useDocumentMenu({})
  const [page, setPage] = useState(1)
  const totalPages = Math.max(1, Math.ceil(documents.length / DEFAULT_DOCUMENT_PAGE_SIZE))
  const current = Math.min(page, totalPages)
  const visible = documents.slice((current - 1) * DEFAULT_DOCUMENT_PAGE_SIZE, current * DEFAULT_DOCUMENT_PAGE_SIZE)

  return (
    <>
      <ul className="ddoc-shared-list">
        {visible.map((document) => {
          const menu = menuFor(document)
          const extension = documentFileExtension(document)
          return (
            <li key={document.id} data-document-id={document.id}>
              <FileText size={18} aria-hidden className="ddoc-shared-icon" />
              <span className="ddoc-title" title={document.title}>{document.title}</span>
              <span className="ddoc-shared-kind">
                {document.hasFile
                  ? <Badge tone="info">{extension ?? documentTypeLabel(document, t)}</Badge>
                  : <Badge tone="warning">{t('dossierDocuments.fileMissing')}</Badge>}
              </span>
              <span className="ddoc-date">{formatDateTime(document.createdAt)}</span>
              <span className="ddoc-actions">
                {menu.length > 0 && <RowActionMenu items={menu} label={t('dossierDocuments.actions.menu', { title: document.title })} />}
              </span>
            </li>
          )
        })}
      </ul>
      {totalPages > 1 && <DocumentPager page={current} totalPages={totalPages} onPageChange={setPage} />}
    </>
  )
}
