import { useRef } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
      <p className="ui-form-section-description">{t('fleet.docs.prepared.intro')}</p>

      {value.length === 0 && <p className="placeholder-text">{t('fleet.docs.prepared.empty')}</p>}

      {value.map((doc) => (
        <div key={doc.key} className="prepared-editor-row">
          <div className="prepared-editor-file">
            <strong>{doc.file.name}</strong>
            <span className="customer-form-muted"> {t('fleet.docs.prepared.sizeKb', { size: Math.max(1, Math.round(doc.file.size / 1024)) })}</span>
          </div>
          <FormField label={t('fleet.docs.typeField')} htmlFor={`pfd-type-${doc.key}`}>
            <select
              id={`pfd-type-${doc.key}`}
              value={doc.documentType}
              onChange={(e) => patch(doc.key, { documentType: e.target.value as FleetDocumentType })}
            >
              {FLEET_DOCUMENT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(FLEET_DOCUMENT_TYPE_LABELS[type])}
                </option>
              ))}
            </select>
          </FormField>
          {doc.documentType === 'Other' && (
            <FormField label={t('fleet.docs.prepared.title')} htmlFor={`pfd-title-${doc.key}`} hint={t('fleet.docs.prepared.titleHint')}>
              <input
                id={`pfd-title-${doc.key}`}
                value={doc.customTypeName}
                onChange={(e) => patch(doc.key, { customTypeName: e.target.value })}
                maxLength={150}
              />
            </FormField>
          )}
          <FormField label={t('fleet.docs.number')} htmlFor={`pfd-number-${doc.key}`} hint={t('fleet.docs.prepared.numberHint')}>
            <input
              id={`pfd-number-${doc.key}`}
              value={doc.documentNumber}
              onChange={(e) => patch(doc.key, { documentNumber: e.target.value })}
              maxLength={100}
            />
          </FormField>
          <FormField label={t('fleet.docs.issueDate')} htmlFor={`pfd-issue-${doc.key}`}>
            <input
              id={`pfd-issue-${doc.key}`}
              type="date"
              value={doc.issueDate}
              onChange={(e) => patch(doc.key, { issueDate: e.target.value })}
            />
          </FormField>
          <FormField label={t('fleet.docs.expiryDate')} htmlFor={`pfd-exp-${doc.key}`} hint={t('fleet.docs.prepared.expiryHint')}>
            <input
              id={`pfd-exp-${doc.key}`}
              type="date"
              value={doc.expiryDate}
              onChange={(e) => patch(doc.key, { expiryDate: e.target.value })}
            />
          </FormField>
          <FormField label={t('fleet.docs.notes')} htmlFor={`pfd-notes-${doc.key}`}>
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
              {t('fleet.common.replace')}
            </Button>
            <Button variant="ghost" onClick={() => remove(doc.key)} aria-label={t('fleet.docs.prepared.removeAria', { name: doc.file.name })}>
              {t('ui.actions.delete')}
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
        {t('fleet.docs.prepared.add')}
      </Button>
    </div>
  )
}
