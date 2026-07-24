import { useRef } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { FLEET_DOCUMENT_ACCEPT } from '../api/fleetDocumentsApi'
import { FLEET_DOCUMENT_TYPES, FLEET_DOCUMENT_TYPE_LABELS, type FleetDocumentType } from '../types'
import type { PreparedFleetDocument } from '../utils/preparedFleetDocs'

interface PreparedFleetDocumentsEditorProps {
  value: PreparedFleetDocument[]
  onChange: (next: PreparedFleetDocument[]) => void
}

function newKey(): string {
  return crypto.randomUUID()
}

/**
 * Create-mode document editor for vehicles/trailers: real files + metadata staged in
 * client-side state. Nothing is uploaded here — uploads run after the asset is created
 * (see preparedFleetDocs), so a failed creation leaves no orphaned documents.
 */
export function PreparedFleetDocumentsEditor({ value, onChange }: PreparedFleetDocumentsEditorProps) {
  const addRef = useRef<HTMLInputElement>(null)
  const replaceRefs = useRef<Record<string, HTMLInputElement | null>>({})

  function addFiles(files: FileList | null) {
    if (!files || files.length === 0) return
    const additions: PreparedFleetDocument[] = Array.from(files).map((file) => ({
      key: newKey(),
      file,
      documentType: 'Other',
      customTypeName: '',
      documentNumber: '',
      issueDate: '',
      expiryDate: '',
      notes: '',
    }))
    onChange([...value, ...additions])
  }

  function patch(key: string, changes: Partial<PreparedFleetDocument>) {
    onChange(value.map((doc) => (doc.key === key ? { ...doc, ...changes } : doc)))
  }

  function remove(key: string) {
    onChange(value.filter((doc) => doc.key !== key))
  }

  return (
    <div className="prepared-editor">
      <p className="ui-form-section-description">
        Selecteer documenten (leasingcontract, verzekering, keuringsbewijs, …); ze worden geüpload zodra het voertuig of
        de oplegger is aangemaakt.
      </p>

      {value.length === 0 && <p className="placeholder-text">Nog geen documenten geselecteerd.</p>}

      {value.map((doc) => (
        <div key={doc.key} className="prepared-editor-row">
          <div className="prepared-editor-file">
            <strong>{doc.file.name}</strong>
            <span className="customer-form-muted"> ({Math.max(1, Math.round(doc.file.size / 1024))} kB)</span>
          </div>
          <FormField label="Documenttype" htmlFor={`pfd-type-${doc.key}`}>
            <select
              id={`pfd-type-${doc.key}`}
              value={doc.documentType}
              onChange={(e) => patch(doc.key, { documentType: e.target.value as FleetDocumentType })}
            >
              {FLEET_DOCUMENT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {FLEET_DOCUMENT_TYPE_LABELS[type]}
                </option>
              ))}
            </select>
          </FormField>
          {doc.documentType === 'Other' && (
            <FormField label="Titel" htmlFor={`pfd-title-${doc.key}`} hint="Naam voor dit document.">
              <input
                id={`pfd-title-${doc.key}`}
                value={doc.customTypeName}
                onChange={(e) => patch(doc.key, { customTypeName: e.target.value })}
                maxLength={150}
              />
            </FormField>
          )}
          <FormField label="Documentnummer" htmlFor={`pfd-number-${doc.key}`} hint="Optioneel.">
            <input
              id={`pfd-number-${doc.key}`}
              value={doc.documentNumber}
              onChange={(e) => patch(doc.key, { documentNumber: e.target.value })}
              maxLength={100}
            />
          </FormField>
          <FormField label="Uitgiftedatum" htmlFor={`pfd-issue-${doc.key}`}>
            <input
              id={`pfd-issue-${doc.key}`}
              type="date"
              value={doc.issueDate}
              onChange={(e) => patch(doc.key, { issueDate: e.target.value })}
            />
          </FormField>
          <FormField label="Vervaldatum" htmlFor={`pfd-exp-${doc.key}`} hint="Leeg = vervalt niet.">
            <input
              id={`pfd-exp-${doc.key}`}
              type="date"
              value={doc.expiryDate}
              onChange={(e) => patch(doc.key, { expiryDate: e.target.value })}
            />
          </FormField>
          <FormField label="Notities" htmlFor={`pfd-notes-${doc.key}`}>
            <input
              id={`pfd-notes-${doc.key}`}
              value={doc.notes}
              onChange={(e) => patch(doc.key, { notes: e.target.value })}
              maxLength={500}
            />
          </FormField>
          <div className="prepared-editor-actions">
            <input
              ref={(el) => { replaceRefs.current[doc.key] = el }}
              type="file"
              accept={FLEET_DOCUMENT_ACCEPT}
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
        accept={FLEET_DOCUMENT_ACCEPT}
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
