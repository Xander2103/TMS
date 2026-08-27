import { useRef } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import {
  ORDER_DOCUMENT_ACCEPT,
  ORDER_DOCUMENT_TYPE_LABELS,
  ORDER_DOCUMENT_TYPES,
  type OrderDocumentType,
} from '../api/orderDocumentsApi'
import type { PreparedOrderDocument } from '../utils/preparedOrderDocs'
import { useLocale } from '../../../i18n/localeContext'

interface PreparedOrderDocumentsEditorProps {
  value: PreparedOrderDocument[]
  onChange: (next: PreparedOrderDocument[]) => void
}

/** Create-mode staging for order documents; uploads run only after the order exists. */
export function PreparedOrderDocumentsEditor({ value, onChange }: PreparedOrderDocumentsEditorProps) {
  const { t } = useLocale()
  const addRef = useRef<HTMLInputElement>(null)

  function addFiles(files: FileList | null) {
    if (!files || files.length === 0) return
    const additions: PreparedOrderDocument[] = Array.from(files).map((file) => ({
      key: crypto.randomUUID(),
      file,
      documentType: 'CustomerDeliveryNote',
      title: '',
      issueDate: '',
      notes: '',
    }))
    onChange([...value, ...additions])
  }

  function patch(key: string, changes: Partial<PreparedOrderDocument>) {
    onChange(value.map((doc) => (doc.key === key ? { ...doc, ...changes } : doc)))
  }

  return (
    <div className="prepared-editor">
      <p className="ui-form-section-description">
        Bijvoorbeeld de leverbon van de klant; bestanden worden geüpload zodra de opdracht is aangemaakt.
      </p>
      {value.length === 0 && <p className="placeholder-text">Nog geen documenten geselecteerd.</p>}
      {value.map((doc) => (
        <div key={doc.key} className="prepared-editor-row">
          <div className="prepared-editor-file">
            <strong>{doc.file.name}</strong>
            <span className="customer-form-muted"> ({Math.max(1, Math.round(doc.file.size / 1024))} kB)</span>
          </div>
          <FormField label="Documenttype" htmlFor={`pod-type-${doc.key}`}>
            <select id={`pod-type-${doc.key}`} value={doc.documentType} onChange={(e) => patch(doc.key, { documentType: e.target.value as OrderDocumentType })}>
              {ORDER_DOCUMENT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(ORDER_DOCUMENT_TYPE_LABELS[type])}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Titel" htmlFor={`pod-title-${doc.key}`} hint="Leeg = bestandsnaam.">
            <input id={`pod-title-${doc.key}`} value={doc.title} onChange={(e) => patch(doc.key, { title: e.target.value })} maxLength={200} />
          </FormField>
          <FormField label="Uitgiftedatum" htmlFor={`pod-issue-${doc.key}`}>
            <input id={`pod-issue-${doc.key}`} type="date" value={doc.issueDate} onChange={(e) => patch(doc.key, { issueDate: e.target.value })} />
          </FormField>
          <FormField label="Notities" htmlFor={`pod-notes-${doc.key}`}>
            <input id={`pod-notes-${doc.key}`} value={doc.notes} onChange={(e) => patch(doc.key, { notes: e.target.value })} maxLength={500} />
          </FormField>
          <div className="prepared-editor-actions">
            <Button variant="ghost" onClick={() => onChange(value.filter((d) => d.key !== doc.key))}>
              Verwijderen
            </Button>
          </div>
        </div>
      ))}
      <input
        ref={addRef}
        type="file"
        accept={ORDER_DOCUMENT_ACCEPT}
        multiple
        hidden
        onChange={(e) => {
          addFiles(e.target.files)
          e.target.value = ''
        }}
      />
      <Button variant="secondary" onClick={() => addRef.current?.click()}>
        + Document toevoegen
      </Button>
    </div>
  )
}
