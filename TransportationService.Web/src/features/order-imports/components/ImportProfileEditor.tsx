import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../../customers/api/customersApi'
import {
  analyzeOrderImportFile,
  createOrderImportProfile,
  listOrderImportFields,
  updateOrderImportProfile,
  type OrderImportField,
  type OrderImportProfile,
} from '../api/orderImportsApi'

/** Sentinel for "Niet importeren": the column is deliberately ignored, not forgotten. */
const IGNORE = '__ignore__'

interface MappingRow {
  columnIndex: number
  header: string
  sampleValues: string[]
  /** '' = unmapped, IGNORE = deliberately ignored, otherwise a field key. */
  field: string
  /** Recognition confidence of the automatic suggestion (null = user choice / no suggestion). */
  confidence: number | null
}

interface ImportProfileEditorProps {
  /** Null = create a new profile. */
  profile: OrderImportProfile | null
  onClose: (saved: boolean) => void
}

/** "A", "AB" for a 1-based column index. */
function columnLetter(index: number): string {
  let result = ''
  let value = index
  while (value > 0) {
    value -= 1
    result = String.fromCharCode(65 + (value % 26)) + result
    value = Math.floor(value / 26)
  }
  return result
}

/** "A" → 1, "AB" → 28, "13" → 13. */
function columnIndexFrom(reference: string): number {
  if (/^\d+$/.test(reference)) return Number(reference)
  let result = 0
  for (const character of reference.toUpperCase()) {
    result = result * 26 + (character.charCodeAt(0) - 64)
  }
  return result
}

/**
 * Import-profile editor: basics (name/customer/type/active) + a sample-file driven visual
 * column mapping. Recognition is deterministic (backend alias catalog); every suggestion stays
 * user-overridable through a searchable field picker. One editor serves create AND edit — an
 * existing profile opens from its stored headers without a new upload.
 */
export function ImportProfileEditor({ profile, onClose }: ImportProfileEditorProps) {
  const { t } = useLocale()
  const { showSuccess } = useToast()

  const [name, setName] = useState(profile?.name ?? '')
  const [description, setDescription] = useState(profile?.description ?? '')
  const [customerId, setCustomerId] = useState<string | null>(profile?.customerId ?? null)
  const [isActive, setIsActive] = useState(profile?.isActive ?? true)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [fields, setFields] = useState<OrderImportField[]>([])
  const [rows, setRows] = useState<MappingRow[]>(() => rowsFromProfile(profile))
  const [analyzing, setAnalyzing] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listOrderImportFields()
      .then((data) => {
        if (mounted) setFields(data)
      })
      .catch(() => {})
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name })))
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  const fieldOptions: SearchableSelectOption[] = useMemo(
    () => [
      { value: IGNORE, label: t('orderImports.mapping.ignore'), description: t('orderImports.mapping.ignoreHint') },
      ...fields.map((field) => ({
        value: field.key,
        label: `${t(`orderImports.fieldGroups.${field.group}`)} · ${t(`orderImports.fields.${field.key}`)}`,
        keywords: field.key,
      })),
    ],
    [fields, t],
  )

  const fieldLabel = (key: string) => t(`orderImports.fields.${key}`)

  // A field mapped twice is a configuration error the user must resolve before saving.
  const duplicateFields = useMemo(() => {
    const seen = new Map<string, number>()
    for (const row of rows) {
      if (row.field && row.field !== IGNORE) {
        seen.set(row.field, (seen.get(row.field) ?? 0) + 1)
      }
    }
    return [...seen.entries()].filter(([, count]) => count > 1).map(([field]) => field)
  }, [rows])

  async function analyzeSample(file: File) {
    setAnalyzing(true)
    setError(null)
    try {
      const analysis = await analyzeOrderImportFile(file)
      const previousByHeader = new Map(rows.filter((r) => r.header).map((r) => [r.header.toLowerCase(), r]))
      setRows(
        analysis.columns.map((column) => {
          // An existing mapping for the same header wins over a fresh suggestion.
          const previous = previousByHeader.get(column.header.toLowerCase())
          if (previous?.field) {
            return { ...previous, columnIndex: column.columnIndex, sampleValues: column.sampleValues }
          }
          return {
            columnIndex: column.columnIndex,
            header: column.header,
            sampleValues: column.sampleValues,
            field: column.suggestedField ?? '',
            confidence: column.suggestedField ? column.confidence : null,
          }
        }),
      )
    } catch (err) {
      setError(describeApiError(err, t('orderImports.profileEditor.analyzeFailed')).message)
    } finally {
      setAnalyzing(false)
    }
  }

  function rowStatus(row: MappingRow): { label: string; tone: BadgeTone } {
    if (row.field === IGNORE) return { label: t('orderImports.mapping.statusIgnored'), tone: 'neutral' }
    if (!row.field) return { label: t('orderImports.mapping.statusUnmapped'), tone: 'danger' }
    if (row.confidence !== null && row.confidence < 95) {
      return { label: t('orderImports.mapping.statusReview'), tone: 'warning' }
    }
    return { label: t('orderImports.mapping.statusRecognized'), tone: 'success' }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (saving) return
    if (!name.trim()) {
      setError(t('orderImports.profileEditor.nameRequired'))
      return
    }
    if (duplicateFields.length > 0) {
      setError(
        t('orderImports.profileEditor.duplicateTargets', {
          fields: duplicateFields.map(fieldLabel).join(', '),
        }),
      )
      return
    }
    const mapping: Record<string, string> = {}
    for (const row of rows) {
      if (row.field && row.field !== IGNORE) {
        mapping[row.field] = String(row.columnIndex)
      }
    }
    setSaving(true)
    setError(null)
    try {
      const input = {
        name: name.trim(),
        description: description.trim() || null,
        customerId,
        isActive,
        headerRows: 1,
        mapping,
        sourceHeaders: rows.some((r) => r.header) ? rows.map((r) => r.header) : null,
      }
      if (profile) {
        await updateOrderImportProfile(profile.id, input)
        showSuccess(t('orderImports.profileEditor.updated'))
      } else {
        await createOrderImportProfile(input)
        showSuccess(t('orderImports.profileEditor.created'))
      }
      onClose(true)
    } catch (err) {
      // The filled mapping stays on screen — only the error surfaces.
      setError(describeApiError(err, t('orderImports.profileEditor.saveFailed')).message)
      setSaving(false)
    }
  }

  return (
    <section className="oi-profile-editor" aria-label={t('orderImports.profileEditor.title')}>
      <form onSubmit={(event) => void submit(event)} noValidate>
        {error && (
          <div className="oi-form-error" role="alert">
            {error}
          </div>
        )}

        <div className="oi-editor-grid">
          <FormField label={t('orderImports.profileEditor.name')} htmlFor="pe-name" required>
            <input id="pe-name" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} disabled={saving} />
          </FormField>
          <FormField
            label={t('orderImports.profileEditor.customer')}
            htmlFor="pe-customer"
            hint={t('orderImports.profileEditor.customerHint')}
          >
            <SearchableSelect
              id="pe-customer"
              value={customerId}
              onChange={setCustomerId}
              options={customerOptions}
              placeholder={t('orderImports.profileEditor.customerPlaceholder')}
              disabled={saving}
            />
          </FormField>
          <FormField label={t('orderImports.profileEditor.importType')} htmlFor="pe-type">
            {/* One import type exists today; a fixed field states it instead of hiding it. */}
            <input id="pe-type" value={t('orderImports.profileEditor.importTypeTransport')} disabled />
          </FormField>
          <FormField label={t('orderImports.profileEditor.description')} htmlFor="pe-description">
            <input
              id="pe-description"
              value={description}
              maxLength={500}
              onChange={(e) => setDescription(e.target.value)}
              disabled={saving}
            />
          </FormField>
        </div>
        <label className="oi-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={saving} />
          {t('orderImports.profileEditor.active')}
        </label>

        <FormField
          label={t('orderImports.profileEditor.sampleFile')}
          htmlFor="pe-sample"
          hint={t('orderImports.profileEditor.sampleFileHint')}
        >
          <input
            id="pe-sample"
            type="file"
            accept=".xlsx"
            disabled={saving || analyzing}
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) void analyzeSample(file)
            }}
          />
        </FormField>
        {analyzing && <p className="oi-hint">{t('orderImports.profileEditor.analyzing')}</p>}

        {rows.length === 0 ? (
          <p className="oi-hint">{t('orderImports.profileEditor.noColumnsYet')}</p>
        ) : (
          <div className="oi-mapping">
            <div className="oi-mapping-head" aria-hidden="true">
              <span>{t('orderImports.mapping.excelColumn')}</span>
              <span>{t('orderImports.mapping.samples')}</span>
              <span>{t('orderImports.mapping.tmsField')}</span>
              <span>{t('orderImports.mapping.status')}</span>
            </div>
            {rows.map((row) => {
              const status = rowStatus(row)
              const duplicate = row.field && row.field !== IGNORE && duplicateFields.includes(row.field)
              return (
                <div key={row.columnIndex} className="oi-mapping-row">
                  <div className="oi-mapping-col">
                    <code>{columnLetter(row.columnIndex)}</code>{' '}
                    <strong>{row.header || t('orderImports.mapping.noHeader')}</strong>
                  </div>
                  <div className="oi-mapping-samples">
                    {row.sampleValues.length > 0 ? row.sampleValues.join(' · ') : '—'}
                  </div>
                  <div className="oi-mapping-field">
                    <SearchableSelect
                      ariaLabel={t('orderImports.mapping.fieldAria', {
                        column: row.header || columnLetter(row.columnIndex),
                      })}
                      value={row.field || null}
                      onChange={(value) =>
                        setRows((current) =>
                          current.map((r) =>
                            r.columnIndex === row.columnIndex ? { ...r, field: value ?? '', confidence: null } : r,
                          ),
                        )
                      }
                      options={fieldOptions}
                      placeholder={t('orderImports.mapping.fieldPlaceholder')}
                      disabled={saving}
                    />
                  </div>
                  <div className="oi-mapping-status">
                    <Badge tone={duplicate ? 'danger' : status.tone}>
                      {duplicate ? t('orderImports.mapping.statusDuplicate') : status.label}
                    </Badge>
                    {row.confidence !== null && row.field && row.field !== IGNORE && (
                      <span className="oi-hint">{row.confidence}%</span>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
        )}

        <div className="oi-editor-actions">
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" disabled={saving || analyzing}>
            {saving ? t('orderImports.profileEditor.saving') : t('orderImports.profileEditor.save')}
          </Button>
        </div>
      </form>
    </section>
  )
}

/** Editor rows for an existing profile: stored headers first, stored mapping merged in. */
function rowsFromProfile(profile: OrderImportProfile | null): MappingRow[] {
  if (!profile) return []
  const fieldByColumn = new Map<number, string>()
  for (const [field, reference] of Object.entries(profile.mapping ?? {})) {
    fieldByColumn.set(columnIndexFrom(reference), field)
  }
  const headers = profile.sourceHeaders ?? []
  const columnCount = Math.max(headers.length, ...(fieldByColumn.size > 0 ? [...fieldByColumn.keys()] : [0]))
  const rows: MappingRow[] = []
  for (let column = 1; column <= columnCount; column += 1) {
    rows.push({
      columnIndex: column,
      header: headers[column - 1] ?? '',
      sampleValues: [],
      field: fieldByColumn.get(column) ?? '',
      confidence: null,
    })
  }
  return rows
}
