import { useCallback, useEffect, useRef, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  ORDER_DOCUMENT_ACCEPT,
  ORDER_DOCUMENT_TYPE_LABELS,
  ORDER_DOCUMENT_TYPES,
  deleteOrderDocument,
  downloadOrderDocumentFile,
  listOrderDocuments,
  createOrderDocument,
  uploadOrderDocumentFile,
  type OrderDocument,
  type OrderDocumentType,
} from '../api/orderDocumentsApi'
import { useLocale } from '../../../i18n/localeContext'

interface OrderDocumentsPanelProps {
  orderId: string
}

/** Self-saving order documents list: upload, download, delete (customer delivery note, CMR, ...). */
export function OrderDocumentsPanel({ orderId }: OrderDocumentsPanelProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('orders.edit') || hasPermission('orders.create') || hasPermission('orders.manage')

  const [documents, setDocuments] = useState<OrderDocument[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [uploadType, setUploadType] = useState<OrderDocumentType>('CustomerDeliveryNote')
  const [confirmDelete, setConfirmDelete] = useState<OrderDocument | null>(null)
  const [busy, setBusy] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    listOrderDocuments(orderId)
      .then((data) => {
        setDocuments(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('Documenten konden niet worden geladen.'))
  }, [orderId])

  useEffect(() => {
    reload()
  }, [reload])

  async function handleUpload(files: FileList | null) {
    const file = files?.[0]
    if (!file) return
    setBusy(true)
    try {
      const created = await createOrderDocument(orderId, {
        documentType: uploadType,
        customTypeName: null,
        title: file.name,
        issueDate: null,
        notes: null,
      })
      await uploadOrderDocumentFile(created.id, file)
      showSuccess('Document toegevoegd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Het document kon niet worden toegevoegd.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!confirmDelete) return
    const target = confirmDelete
    setConfirmDelete(null)
    try {
      await deleteOrderDocument(target.id)
      showSuccess('Document verwijderd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Het document kon niet worden verwijderd.').message)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (documents === null) return <p className="placeholder-text">Documenten laden…</p>

  return (
    <div className="tof-documents">
      {canManage && (
        <div className="tof-documents-toolbar">
          <select aria-label="Documenttype" value={uploadType} onChange={(e) => setUploadType(e.target.value as OrderDocumentType)} disabled={busy}>
            {ORDER_DOCUMENT_TYPES.map((type) => (
              <option key={type} value={type}>
                {t(ORDER_DOCUMENT_TYPE_LABELS[type])}
              </option>
            ))}
          </select>
          <input
            ref={fileRef}
            type="file"
            accept={ORDER_DOCUMENT_ACCEPT}
            hidden
            onChange={(e) => {
              void handleUpload(e.target.files)
              e.target.value = ''
            }}
          />
          <Button variant="secondary" onClick={() => fileRef.current?.click()} disabled={busy}>
            + Document uploaden
          </Button>
        </div>
      )}

      {documents.length === 0 && <p className="placeholder-text">Nog geen documenten bij deze opdracht.</p>}
      {documents.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Titel</th>
              <th>Type</th>
              <th>Bestand</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {documents.map((doc) => (
              <tr key={doc.id}>
                <td>{doc.title}</td>
                <td>
                  <Badge tone="info">{t(ORDER_DOCUMENT_TYPE_LABELS[doc.documentType])}</Badge>
                </td>
                <td>{doc.hasAttachment ? doc.fileName : '—'}</td>
                <td className="issued-items-row-actions">
                  {doc.hasAttachment && (
                    <button
                      type="button"
                      className="tof-link"
                      onClick={() => void downloadOrderDocumentFile(doc.id, doc.fileName ?? 'document')}
                    >
                      Downloaden
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="tof-link tof-link-danger" onClick={() => setConfirmDelete(doc)}>
                      Verwijderen
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Document verwijderen"
          message={`Weet je zeker dat je "${confirmDelete.title}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(null)}
        />
      )}
    </div>
  )
}
