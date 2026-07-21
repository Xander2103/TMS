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
  /** Singular noun for the create dialog title, e.g. "klantcategorie". */
  singular: string
  value: string | null
  onChange: (id: string | null) => void
  placeholder?: string
  disabled?: boolean
}

interface PendingCreate {
  query: string
  resolve: (option: SearchableSelectOption | null) => void
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
export function LookupSelect({ id, basePath, viewPermission, managePermission, singular, value, onChange, placeholder, disabled }: LookupSelectProps) {
  const { hasPermission } = useAuth()
  const canView = viewPermission === undefined || hasPermission(viewPermission)
  const { options, isLoading } = useLookupOptions(basePath, { enabled: canView })
  const [pending, setPending] = useState<PendingCreate | null>(null)

  const selectOptions = useMemo<SearchableSelectOption[]>(
    () => options.map((o) => ({ value: o.id, label: o.name, keywords: o.code })),
    [options],
  )

  const canCreate = canView && hasPermission(managePermission)

  const onCreate = useMemo<SearchableSelectCreateConfig | undefined>(() => {
    if (!canCreate || disabled) return undefined
    return {
      label: (query) => `Nieuwe ${singular} "${query}" toevoegen`,
      create: (query) =>
        new Promise<SearchableSelectOption | null>((resolve) => {
          setPending({ query, resolve })
        }),
    }
  }, [canCreate, disabled, singular])

  return (
    <>
      <SearchableSelect
        id={id}
        value={value}
        onChange={onChange}
        options={selectOptions}
        placeholder={placeholder ?? '— Selecteer —'}
        disabled={disabled || !canView}
        isLoading={isLoading}
        onCreate={onCreate}
      />
      {pending && (
        <LookupCreateDialog
          basePath={basePath}
          singular={singular}
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
  initialName,
  onCreated,
  onCancel,
}: {
  basePath: string
  singular: string
  initialName: string
  onCreated: (option: SearchableSelectOption) => void
  onCancel: () => void
}) {
  const [name, setName] = useState(initialName)
  const [code, setCode] = useState(suggestCode(initialName))
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || !code.trim()) {
      setError('Naam en code zijn verplicht.')
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
      onCreated({ value: created.id, label: created.name, keywords: created.code })
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? `Er bestaat al een ${singular} met code '${code.trim()}'.`
          : 'Toevoegen is mislukt. Probeer het opnieuw.',
      )
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title={`Nieuwe ${singular}`}
      onClose={onCancel}
      busy={submitting}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={submitting}>
            Annuleren
          </Button>
          <Button type="submit" form="lookup-inline-create" disabled={submitting}>
            {submitting ? 'Toevoegen…' : 'Toevoegen en selecteren'}
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
        <FormField label="Naam" htmlFor="lookup-inline-name" required>
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
        <FormField label="Code" htmlFor="lookup-inline-code" hint="Korte unieke code voor deze waarde." required>
          <input id="lookup-inline-code" value={code} onChange={(e) => setCode(e.target.value)} maxLength={50} />
        </FormField>
        <FormField label="Omschrijving" htmlFor="lookup-inline-description">
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
