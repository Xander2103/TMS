import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  createPricingImportProfile,
  deletePricingImportProfile,
  readPricingImportHeaders,
  updatePricingImportProfile,
  type PricingImportField,
  type PricingImportProfile,
} from '../api/pricingImportApi'
import '../../transport-orders/components/commercialChange.css'

interface PricingImportProfilePanelProps {
  /** The workbook chosen in the import dialog; its header row is read to offer source columns. */
  file: File | null
  /** The profile currently selected in the dialog (null = standard template). */
  profile: PricingImportProfile | null
  /** Called with the saved/created profile, or null after a delete, so the dialog refreshes its list. */
  onProfilesChanged: (selected: PricingImportProfile | null) => void
  onMessage: (message: string) => void
  disabled?: boolean
}

const FIELD_LABEL_KEYS = new Set([
  'regelId', 'naam', 'basis', 'eenheid', 'zone', 'prioriteit', 'staffelVan', 'staffelTot', 'gewichtTot', 'volumeTot',
  'laadmeterTot', 'staffelprijs', 'prijsPerExtra', 'eenheidsprijs', 'basisbedrag', 'minimum', 'maximum', 'minAantal',
  'afrondingsstap', 'staffelmodus', 'geldigVan', 'geldigTot',
])

/**
 * Sprint 4 (completion): mapping profiles managed where they are used. The operator reads the
 * columns of the chosen workbook, maps each business-labelled pricing field onto one of them,
 * and saves that as a named profile (create / update / rename / delete). No JSON is ever shown.
 */
export function PricingImportProfilePanel({ file, profile, onProfilesChanged, onMessage, disabled }: PricingImportProfilePanelProps) {
  const { t } = useLocale()
  const [headers, setHeaders] = useState<string[]>([])
  const [fields, setFields] = useState<PricingImportField[]>([])
  // Initialised from the selected profile; the dialog keys this panel on the profile id so a
  // different selection remounts it with that profile's values.
  const [mapping, setMapping] = useState<Record<string, string>>(() => profile?.mapping ?? {})
  const [name, setName] = useState(() => profile?.name ?? '')
  const [notes, setNotes] = useState(() => profile?.notes ?? '')
  const [headerRow, setHeaderRow] = useState(() => String(profile?.headerRow ?? 1))
  const [sheetName, setSheetName] = useState(() => profile?.sheetName ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  // Fields are needed even without a workbook (to show what can be mapped): read them once via
  // the headers endpoint of the chosen file; without a file the operator is asked for one.
  async function readColumns() {
    if (!file) {
      setError(t('tarification.importProfiles.noFile'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const result = await readPricingImportHeaders(file, null)
      setHeaders(result.headers)
      setFields(result.fields)
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.importProfiles.readFailed')))
    } finally {
      setBusy(false)
    }
  }

  function fieldLabel(field: PricingImportField): string {
    return FIELD_LABEL_KEYS.has(field.key) ? t(`tarification.importFields.${field.key}`) : field.standardHeader
  }

  const missingRequired = fields.filter((f) => f.required && !mapping[f.key]?.trim())

  function buildInput() {
    return {
      name: name.trim(),
      notes: notes.trim() || null,
      headerRow: Math.max(1, Number.parseInt(headerRow, 10) || 1),
      sheetName: sheetName.trim() || null,
      mapping: Object.fromEntries(Object.entries(mapping).filter(([, header]) => header.trim().length > 0)),
      isActive: true,
    }
  }

  async function save(asNew: boolean) {
    if (!name.trim()) {
      setError(t('tarification.importProfiles.nameRequired'))
      return
    }
    if (missingRequired.length > 0) {
      setError(t('tarification.importProfiles.missingRequired', { fields: missingRequired.map(fieldLabel).join(', ') }))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const input = buildInput()
      const saved = asNew || !profile
        ? await createPricingImportProfile(input)
        : await updatePricingImportProfile(profile.id, input)
      onMessage(t(asNew || !profile ? 'tarification.importProfiles.created' : 'tarification.importProfiles.updated', { name: saved.name }))
      onProfilesChanged(saved)
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.importProfiles.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!profile) return
    setBusy(true)
    setError(null)
    try {
      await deletePricingImportProfile(profile.id)
      setConfirmDelete(false)
      onMessage(t('tarification.importProfiles.deleted'))
      onProfilesChanged(null)
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.importProfiles.deleteFailed')))
    } finally {
      setBusy(false)
    }
  }

  const locked = disabled || busy
  // Existing mappings may name headers that are not in this file; keep them selectable so
  // nothing silently disappears when the operator only renames the profile.
  const sourceOptions = Array.from(new Set([...headers, ...Object.values(mapping).filter((h) => h)]))

  return (
    <section className="pricing-import-profile" data-testid="pricing-import-profile-panel">
      <h3>{t('tarification.importProfiles.title')}</h3>
      <p className="customer-form-muted">{t('tarification.importProfiles.intro')}</p>

      <div className="issued-items-form-row">
        <FormField label={t('tarification.importProfiles.nameLabel')} htmlFor="pricing-profile-name" required>
          <input id="pricing-profile-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={120} disabled={locked} />
        </FormField>
        <FormField label={t('tarification.importProfiles.headerRow')} htmlFor="pricing-profile-header-row">
          <input id="pricing-profile-header-row" type="number" min={1} value={headerRow} onChange={(e) => setHeaderRow(e.target.value)} disabled={locked} />
        </FormField>
        <FormField label={t('tarification.importProfiles.sheetName')} htmlFor="pricing-profile-sheet">
          <input id="pricing-profile-sheet" value={sheetName} onChange={(e) => setSheetName(e.target.value)} maxLength={60} disabled={locked} />
        </FormField>
      </div>
      <FormField label={t('tarification.importProfiles.notesLabel')} htmlFor="pricing-profile-notes">
        <input id="pricing-profile-notes" value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={500} disabled={locked} />
      </FormField>

      <div className="pricing-import-profile-actions">
        <Button variant="secondary" onClick={() => void readColumns()} disabled={locked || !file}>
          {t('tarification.importProfiles.readColumns')}
        </Button>
        {headers.length > 0 && <span className="customer-form-muted">{t('tarification.importProfiles.columnsFound', { count: headers.length })}</span>}
      </div>

      {fields.length > 0 && (
        <table className="pricing-import-mapping-table">
          <thead>
            <tr>
              <th>{t('tarification.importProfiles.fieldColumn')}</th>
              <th>{t('tarification.importProfiles.sourceColumn')}</th>
            </tr>
          </thead>
          <tbody>
            {fields.map((field) => (
              <tr key={field.key}>
                <td>
                  {fieldLabel(field)}
                  {field.required && <span className="customer-form-muted"> ({t('tarification.importProfiles.required')})</span>}
                </td>
                <td>
                  <select
                    aria-label={fieldLabel(field)}
                    value={mapping[field.key] ?? ''}
                    onChange={(e) => setMapping((m) => ({ ...m, [field.key]: e.target.value }))}
                    disabled={locked}
                  >
                    <option value="">{t('tarification.importProfiles.notMapped')}</option>
                    {sourceOptions.map((header) => (
                      <option key={header} value={header}>
                        {header}
                      </option>
                    ))}
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {error && (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {error}
        </p>
      )}

      <div className="pricing-import-profile-actions">
        {profile && (
          <Button onClick={() => void save(false)} disabled={locked}>
            {t('tarification.importProfiles.update')}
          </Button>
        )}
        <Button variant={profile ? 'secondary' : 'primary'} onClick={() => void save(true)} disabled={locked}>
          {t('tarification.importProfiles.saveNew')}
        </Button>
        {profile && (
          <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={locked}>
            {t('tarification.importProfiles.delete')}
          </Button>
        )}
      </div>

      {confirmDelete && profile && (
        <ConfirmDialog
          title={t('tarification.importProfiles.deleteTitle')}
          message={t('tarification.importProfiles.deleteMessage', { name: profile.name })}
          confirmLabel={t('tarification.importProfiles.delete')}
          onConfirm={() => void remove()}
          onCancel={() => setConfirmDelete(false)}
          busy={busy}
        />
      )}
    </section>
  )
}
