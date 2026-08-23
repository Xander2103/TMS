import { useMemo, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import {
  SearchableSelect,
  type SearchableSelectCreateConfig,
  type SearchableSelectOption,
} from '../../../components/ui/SearchableSelect'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { createLookupApi } from '../api/lookupApi'
import { useLookupOptions } from '../hooks/useLookupOptions'

interface LookupSelectProps {
  id?: string
  /** Lookup resource base path, e.g. `/api/customer-categories`. */
  basePath: string
  /**
   * Permission required to load/see the options, e.g. `contact_departments.view`. When the
   * user lacks it, no request is made and the select renders disabled. Omit for lookups
   * every authenticated user may read.
   */
  viewPermission?: string
  /** Permission required for the inline "add new" action, e.g. `customer_categories.manage`. */
  managePermission: string
  /**
   * Singular noun for the create dialog title: a translation key (e.g.
   * `masterData.singular.customer-categories`) or — for not-yet-localised callers — a plain
   * label, which t() passes through unchanged.
   */
  singular: string
  /**
   * What the select stores/returns: the lookup `id` (default) or its stable `code`. Use `code`
   * when the surrounding form persists a code rather than a foreign key (e.g. order unit types).
   */
  valueKey?: 'id' | 'code'
  value: string | null
  onChange: (value: string | null) => void
  placeholder?: string
  disabled?: boolean
}

interface PendingCreate {
  query: string
  resolve: (option: SearchableSelectOption | null) => void
}

/**
 * Resolve the `singular` prop: translation keys (contain a dot) go through t(); plain labels
 * from not-yet-localised callers pass through untouched — without triggering the dev-mode
 * missing-key warning t() would emit for them.
 */
function resolveSingular(t: (key: string) => string, singular: string): string {
  return singular.includes('.') ? t(singular) : singular
}

/** Suggest a lookup code from a name: uppercase, alphanumeric, max 10 chars. */
function suggestCode(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-zA-Z0-9]/g, '')
    .toUpperCase()
    .slice(0, 10)
}

/**
 * Lookup-backed SearchableSelect with a permission-gated inline-create flow: users with the
 * manage permission get an "add new" row that opens a small dialog; the created item is
 * selected automatically so the surrounding form keeps all entered data.
 */
export function LookupSelect({ id, basePath, viewPermission, managePermission, singular, valueKey = 'id', value, onChange, placeholder, disabled }: LookupSelectProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canView = viewPermission === undefined || hasPermission(viewPermission)
  const { options, isLoading } = useLookupOptions(basePath, { enabled: canView })
  const [pending, setPending] = useState<PendingCreate | null>(null)

  const selectOptions = useMemo<SearchableSelectOption[]>(
    () => options.map((o) => ({ value: valueKey === 'code' ? o.code : o.id, label: o.name, keywords: o.code })),
    [options, valueKey],
  )

  const canCreate = canView && hasPermission(managePermission)

  const onCreate = useMemo<SearchableSelectCreateConfig | undefined>(() => {
    if (!canCreate || disabled) return undefined
    return {
      label: (query) => t('masterData.select.createOption', { singular: resolveSingular(t, singular), query }),
      create: (query) =>
        new Promise<SearchableSelectOption | null>((resolve) => {
          setPending({ query, resolve })
        }),
    }
  }, [canCreate, disabled, singular, t])

  return (
    <>
      <SearchableSelect
        id={id}
        value={value}
        onChange={onChange}
        options={selectOptions}
        placeholder={placeholder ?? t('ui.select.placeholder')}
        disabled={disabled || !canView}
        isLoading={isLoading}
        onCreate={onCreate}
      />
      {pending && (
        <LookupCreateDialog
          basePath={basePath}
          singular={singular}
          valueKey={valueKey}
          initialName={pending.query}
          onCreated={(option) => {
            pending.resolve(option)
            setPending(null)
          }}
          onCancel={() => {
            pending.resolve(null)
            setPending(null)
          }}
        />
      )}
    </>
  )
}

function LookupCreateDialog({
  basePath,
  singular,
  valueKey,
  initialName,
  onCreated,
  onCancel,
}: {
  basePath: string
  singular: string
  valueKey: 'id' | 'code'
  initialName: string
  onCreated: (option: SearchableSelectOption) => void
  onCancel: () => void
}) {
  const { t } = useLocale()
  const [name, setName] = useState(initialName)
  const [code, setCode] = useState(suggestCode(initialName))
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || !code.trim()) {
      setError(t('masterData.select.nameAndCodeRequired'))
      return
    }

    setSubmitting(true)
    setError(null)
    try {
      const created = await createLookupApi(basePath).create({
        code: code.trim(),
        name: name.trim(),
        description: description.trim() ? description.trim() : null,
        isActive: true,
        sortOrder: 0,
      })
      onCreated({ value: valueKey === 'code' ? created.code : created.id, label: created.name, keywords: created.code })
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? t('masterData.select.duplicate', { singular: resolveSingular(t, singular), code: code.trim() })
          : t('masterData.select.createFailed'),
      )
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title={t('masterData.select.newTitle', { singular: resolveSingular(t, singular) })}
      onClose={onCancel}
      busy={submitting}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={submitting}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="lookup-inline-create" disabled={submitting}>
            {submitting ? t('masterData.select.adding') : t('masterData.select.addAndSelect')}
          </Button>
        </>
      }
    >
      <form id="lookup-inline-create" onSubmit={handleSubmit}>
        {error && (
          <p className="ui-form-field-error" role="alert">
            {error}
          </p>
        )}
        <FormField label={t('masterData.form.nameLabel')} htmlFor="lookup-inline-name" required>
          <input
            id="lookup-inline-name"
            value={name}
            onChange={(e) => {
              setName(e.target.value)
              setCode(suggestCode(e.target.value))
            }}
            maxLength={150}
            autoFocus
          />
        </FormField>
        <FormField label={t('masterData.form.codeLabel')} htmlFor="lookup-inline-code" hint={t('masterData.select.codeHint')} required>
          <input id="lookup-inline-code" value={code} onChange={(e) => setCode(e.target.value)} maxLength={50} />
        </FormField>
        <FormField label={t('masterData.form.descriptionLabel')} htmlFor="lookup-inline-description">
          <textarea
            id="lookup-inline-description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            maxLength={1000}
          />
        </FormField>
      </form>
    </Modal>
  )
}
