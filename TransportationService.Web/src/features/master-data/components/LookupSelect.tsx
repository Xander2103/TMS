import { useCallback, useMemo, useRef, useState, type FormEvent } from 'react'
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
import type { LookupOption } from '../types'

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
   * Exact text of the permanent "+ Nieuwe …" shortcut row shown to users with the manage
   * permission. Registry lookups (`singular` = `masterData.singular.<slug>`) need none: they get
   * the explicit `masterData.newLabel.<slug>` translation (e.g. "+ Nieuw contracttype").
   */
  createLabel?: string
  /**
   * What the select stores/returns: the lookup `id` (default) or its stable `code`. Use `code`
   * when the surrounding form persists a code rather than a foreign key (e.g. order unit types).
   */
  valueKey?: 'id' | 'code'
  /** Values (ids or codes, matching `valueKey`) left out of the list, e.g. items already chosen in a multi-select. */
  excludeValues?: string[]
  value: string | null
  /** The chosen value plus, when known, the full lookup row (so multi-select callers can render it at once). */
  onChange: (value: string | null, option?: LookupOption | null) => void
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

const REGISTRY_SINGULAR_PREFIX = 'masterData.singular.'

type Translate = (key: string, params?: Record<string, string | number>) => string

/**
 * "New …" phrases for the create flow. Gluing an adjective onto an arbitrary noun guesses at
 * grammar ("Nieuwe contracttype", "Nouveau une fonction"), so registry lookups get an explicit
 * translation per lookup (`masterData.newLabel.<slug>`). A caller that passes a plain noun falls
 * back to wording that needs no agreement with the noun.
 */
function createTexts(t: Translate, singular: string) {
  const noun = resolveSingular(t, singular)
  if (singular.startsWith(REGISTRY_SINGULAR_PREFIX)) {
    const newLabel = t(`masterData.newLabel.${singular.slice(REGISTRY_SINGULAR_PREFIX.length)}`)
    return {
      newLabel,
      createOption: (query: string) => t('masterData.select.createOption', { singular: noun, newLabel, query }),
    }
  }
  return {
    newLabel: t('masterData.select.newGeneric', { singular: noun }),
    createOption: (query: string) => t('masterData.select.createOptionGeneric', { singular: noun, query }),
  }
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
 * manage permission get a permanent "+ Nieuwe …" row (and an "add \"query\"" row while typing)
 * that opens a small dialog inside the current page; the option list is refreshed and the
 * created item is selected automatically so the surrounding form keeps all entered data.
 */
export function LookupSelect({
  id,
  basePath,
  viewPermission,
  managePermission,
  singular,
  createLabel,
  valueKey = 'id',
  excludeValues,
  value,
  onChange,
  placeholder,
  disabled,
}: LookupSelectProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canView = viewPermission === undefined || hasPermission(viewPermission)
  const { options, isLoading, refresh } = useLookupOptions(basePath, { enabled: canView })
  const [pending, setPending] = useState<PendingCreate | null>(null)
  // Rows created inline, keyed by their select value: the parent may ask for the full row
  // before the refreshed option list has propagated through React state.
  const createdRef = useRef(new Map<string, LookupOption>())

  const toValue = useCallback((o: LookupOption) => (valueKey === 'code' ? o.code : o.id), [valueKey])

  const selectOptions = useMemo<SearchableSelectOption[]>(
    () =>
      options
        .filter((o) => !excludeValues?.includes(toValue(o)))
        .map((o) => ({ value: toValue(o), label: o.name, keywords: o.code })),
    [options, toValue, excludeValues],
  )

  const canCreate = canView && hasPermission(managePermission)

  const onCreate = useMemo<SearchableSelectCreateConfig | undefined>(() => {
    if (!canCreate || disabled) return undefined
    const texts = createTexts(t, singular)
    return {
      label: texts.createOption,
      create: (query) =>
        new Promise<SearchableSelectOption | null>((resolve) => {
          setPending({ query, resolve })
        }),
      alwaysShow: true,
      emptyQueryLabel: createLabel ?? t('masterData.select.newAction', { newLabel: texts.newLabel }),
    }
  }, [canCreate, disabled, singular, createLabel, t])

  function handleChange(next: string | null) {
    if (next === null) {
      onChange(null, null)
      return
    }
    const row = options.find((o) => toValue(o) === next) ?? createdRef.current.get(next) ?? null
    onChange(next, row)
  }

  return (
    <>
      <SearchableSelect
        id={id}
        value={value}
        onChange={handleChange}
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
          initialName={pending.query}
          onCreated={async (created) => {
            createdRef.current.set(toValue(created), created)
            // Refresh first so the new row is in the list before it gets selected.
            await refresh()
            pending.resolve({ value: toValue(created), label: created.name, keywords: created.code })
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
  initialName,
  onCreated,
  onCancel,
}: {
  basePath: string
  singular: string
  initialName: string
  onCreated: (created: LookupOption) => Promise<void>
  onCancel: () => void
}) {
  const { t } = useLocale()
  const [name, setName] = useState(initialName)
  // Left empty by default: the backend then generates the next free unique code (the
  // name-derived suggestion is only a placeholder).
  const [code, setCode] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // The dialog is portaled, but React still bubbles this submit through the component tree
    // into the host form (e.g. the employee form) — which must NOT be submitted by it.
    event.stopPropagation()
    if (!name.trim()) {
      setError(t('masterData.form.nameRequired'))
      return
    }

    setSubmitting(true)
    setError(null)
    try {
      const created = await createLookupApi(basePath).create({
        code: code.trim() || null,
        name: name.trim(),
        description: description.trim() ? description.trim() : null,
        isActive: true,
        sortOrder: 0,
      })
      await onCreated({ id: created.id, code: created.code, name: created.name })
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
      title={createTexts(t, singular).newLabel}
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
            onChange={(e) => setName(e.target.value)}
            maxLength={150}
            autoFocus
          />
        </FormField>
        <FormField label={t('masterData.form.codeLabel')} htmlFor="lookup-inline-code" hint={t('masterData.select.codeAutoHint')}>
          <input
            id="lookup-inline-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder={suggestCode(name)}
            maxLength={50}
          />
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
