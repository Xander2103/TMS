import { useRef } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import {
  EMPLOYEE_DOCUMENT_ACCEPT,
  EMPLOYEE_DOCUMENT_CATEGORY_LABELS,
  EMPLOYEE_DOCUMENT_CATEGORY_ORDER,
  type EmployeeDocumentCategory,
} from '../api/employeeDocumentsApi'
import type { PreparedEmployeeDocument } from '../utils/preparedFollowUp'

interface PreparedDocumentsEditorProps {
  value: PreparedEmployeeDocument[]
  onChange: (next: PreparedEmployeeDocument[]) => void
}

function newKey(): string {
  return crypto.randomUUID()
}

/**
 * Create-mode document editor: selects files + metadata into client-side state. Nothing is
 * uploaded here — the files are uploaded after the employee is created (see preparedFollowUp).
 */
export function PreparedDocumentsEditor({ value, onChange }: PreparedDocumentsEditorProps) {
  const addRef = useRef<HTMLInputElement>(null)
  const replaceRefs = useRef<Record<string, HTMLInputElement | null>>({})

  function addFiles(files: FileList | null) {
    if (!files || files.length === 0) return
    const additions: PreparedEmployeeDocument[] = Array.from(files).map((file) => ({
      key: newKey(),
      file,
      category: 'AdditionalDocument',
      customLabel: '',
      expiryDate: '',
      notes: '',
    }))
    onChange([...value, ...additions])
  }

  function patch(key: string, changes: Partial<PreparedEmployeeDocument>) {
    onChange(value.map((doc) => (doc.key === key ? { ...doc, ...changes } : doc)))
  }

  function remove(key: string) {
    onChange(value.filter((doc) => doc.key !== key))
  }

  return (
    <div className="prepared-editor">
      <p className="ui-form-section-description">
        Selecteer documenten; ze worden geüpload zodra de medewerker is aangemaakt.
      </p>

      {value.length === 0 && <p className="placeholder-text">Nog geen documenten geselecteerd.</p>}

      {value.map((doc) => (
        <div key={doc.key} className="prepared-editor-row">
          <div className="prepared-editor-file">
            <strong>{doc.file.name}</strong>
            <span className="customer-form-muted"> ({Math.max(1, Math.round(doc.file.size / 1024))} kB)</span>
          </div>
          <FormField label="Categorie" htmlFor={`pd-cat-${doc.key}`}>
            <select
              id={`pd-cat-${doc.key}`}
              value={doc.category}
              onChange={(e) => patch(doc.key, { category: e.target.value as EmployeeDocumentCategory })}
            >
              {EMPLOYEE_DOCUMENT_CATEGORY_ORDER.map((cat) => (
                <option key={cat} value={cat}>
                  {EMPLOYEE_DOCUMENT_CATEGORY_LABELS[cat]}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Label" htmlFor={`pd-label-${doc.key}`} hint="Optioneel; standaard de categorie.">
            <input
              id={`pd-label-${doc.key}`}
              value={doc.customLabel}
              onChange={(e) => patch(doc.key, { customLabel: e.target.value })}
              maxLength={150}
            />
          </FormField>
          <FormField label="Vervaldatum" htmlFor={`pd-exp-${doc.key}`} hint="Leeg = vervalt niet.">
            <input
              id={`pd-exp-${doc.key}`}
              type="date"
              value={doc.expiryDate}
              onChange={(e) => patch(doc.key, { expiryDate: e.target.value })}
            />
          </FormField>
          <FormField label="Notities" htmlFor={`pd-notes-${doc.key}`}>
            <input
              id={`pd-notes-${doc.key}`}
              value={doc.notes}
              onChange={(e) => patch(doc.key, { notes: e.target.value })}
              maxLength={500}
            />
          </FormField>
          <div className="prepared-editor-actions">
            <input
              ref={(el) => { replaceRefs.current[doc.key] = el }}
              type="file"
              accept={EMPLOYEE_DOCUMENT_ACCEPT}
              hidden
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (file) patch(doc.key, { file })
                e.target.value = ''
              }}
            />
            <Button variant="ghost" onClick={() => replaceRefs.current[doc.key]?.click()}>
              Vervangen
            </Button>
            <Button variant="ghost" onClick={() => remove(doc.key)} aria-label={`Document ${doc.file.name} verwijderen`}>
              Verwijderen
            </Button>
          </div>
        </div>
      ))}

      <input
        ref={addRef}
        type="file"
        accept={EMPLOYEE_DOCUMENT_ACCEPT}
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
