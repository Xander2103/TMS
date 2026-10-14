import type { ReactNode } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { EmptyState } from '../../../components/ui/EmptyState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { ORDER_DOCUMENT_TYPE_LABELS } from '../../transport-orders/api/orderDocumentsApi'
import { downloadDossierDocumentFile, fetchDossierDocumentBlob, isViewableDocument, type DossierDocument } from '../api/dossierDocumentsApi'

export interface DossierDocumentRowActions {
  /** Offered for a document without a file (the metadata exists, the upload failed or was skipped). */
  onAttachFile?: (document: DossierDocument) => void
  onChangeVisibility?: (document: DossierDocument) => void
  onMove?: (document: DossierDocument) => void
  onDelete?: (document: DossierDocument) => void
}

interface DossierDocumentTableProps {
  documents: DossierDocument[]
  /** Shows the "Niveau" column (dossier vs order) — for lists that mix both scopes. */
  showLevel?: boolean
  actions?: DossierDocumentRowActions
  /** Tells the empty-state apart per list; the caller renders it. */
  emptyText: string
  /** B48: the one thing to do when the list is empty (upload), rendered inside the empty state. */
  emptyAction?: ReactNode
}

/**
 * One document table for every view (dossier level, order level, read-only general list on an
 * order page). Viewing/downloading goes through the shared auth path; every write action is
 * only rendered when the host passes a handler, so a read-only list simply passes none.
 */
export function DossierDocumentTable({ documents, showLevel = false, actions = {}, emptyText, emptyAction }: DossierDocumentTableProps) {
  const { t, formatDate } = useLocale()
  const { showError } = useToast()

  if (documents.length === 0) return <EmptyState message={emptyText} action={emptyAction} />

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

  const hasActions = Object.values(actions).some(Boolean)

  return (
    <div className="ddoc-table-wrap">
      <table className="ddoc-table">
        <thead>
          <tr>
            <th>{t('dossierDocuments.columns.title')}</th>
            <th>{t('dossierDocuments.columns.type')}</th>
            {showLevel && <th>{t('dossierDocuments.columns.level')}</th>}
            <th>{t('dossierDocuments.columns.file')}</th>
            <th>{t('dossierDocuments.columns.portal')}</th>
            <th>{t('dossierDocuments.columns.actions')}</th>
          </tr>
        </thead>
        <tbody>
          {documents.map((document) => {
            const typeLabel = document.documentType === 'Other' && document.customTypeName
              ? document.customTypeName
              : t(ORDER_DOCUMENT_TYPE_LABELS[document.documentType] ?? 'transportOrders.documentType.Other')
            return (
              <tr key={document.id} data-document-id={document.id} data-scope={document.scope}>
                <td data-label={t('dossierDocuments.columns.title')}>
                  <span className="ddoc-title">{document.title}</span>
                  {document.issueDate && <span className="ddoc-meta">{formatDate(document.issueDate)}</span>}
                  {document.createdByName && <span className="ddoc-meta">{document.createdByName}</span>}
                </td>
                <td data-label={t('dossierDocuments.columns.type')}>
                  <Badge tone="info">{typeLabel}</Badge>
                </td>
                {showLevel && (
                  <td data-label={t('dossierDocuments.columns.level')}>
                    {document.scope === 'Dossier' ? t('dossierDocuments.levels.dossierShort') : (document.orderNumber ?? '—')}
                  </td>
                )}
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
                <td data-label={t('dossierDocuments.columns.actions')}>
                  <span className="ddoc-actions">
                    {isViewableDocument(document) && (
                      <button type="button" className="link-button" aria-label={t('dossierDocuments.actions.view', { title: document.title })} onClick={() => void view(document)}>
                        {t('dossierDocuments.actions.viewShort')}
                      </button>
                    )}
                    {document.hasFile && (
                      <button type="button" className="link-button" aria-label={t('dossierDocuments.actions.download', { title: document.title })} onClick={() => download(document)}>
                        {t('dossierDocuments.actions.downloadShort')}
                      </button>
                    )}
                    {!document.hasFile && actions.onAttachFile && (
                      <button type="button" className="link-button" aria-label={t('dossierDocuments.actions.attachFile', { title: document.title })} onClick={() => actions.onAttachFile!(document)}>
                        {t('dossierDocuments.upload.retry')}
                      </button>
                    )}
                    {actions.onChangeVisibility && (
                      <button type="button" className="link-button" aria-label={t('dossierDocuments.actions.visibility', { title: document.title })} onClick={() => actions.onChangeVisibility!(document)}>
                        {document.customerVisible ? t('dossierDocuments.visibility.makeInternal') : t('dossierDocuments.visibility.makeVisible')}
                      </button>
                    )}
                    {actions.onMove && (
                      <button type="button" className="link-button" aria-label={t('dossierDocuments.actions.move', { title: document.title })} onClick={() => actions.onMove!(document)}>
                        {t('dossierDocuments.move.confirm')}
                      </button>
                    )}
                    {actions.onDelete && (
                      <button type="button" className="link-button link-button-danger" aria-label={t('dossierDocuments.actions.delete', { title: document.title })} onClick={() => actions.onDelete!(document)}>
                        {t('dossierDocuments.delete.confirm')}
                      </button>
                    )}
                    {!hasActions && !document.hasFile && <span className="ddoc-meta">—</span>}
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
