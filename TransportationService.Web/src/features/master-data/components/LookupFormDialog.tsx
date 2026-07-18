import { useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { ApiError } from '../../../api/apiClient'
import type { LookupApi } from '../api/lookupApi'
import type { LookupInput, LookupItem } from '../types'
import type { LookupResourceConfig } from '../lookupRegistry'

interface LookupFormDialogProps {
  config: LookupResourceConfig
  api: LookupApi
  /** Existing item when editing; undefined when creating. */
  item?: LookupItem
  onSaved: (item: LookupItem, wasCreate: boolean) => void
  onClose: () => void
}

interface FormErrors {
  code?: string
  name?: string
  form?: string
}

export function LookupFormDialog({ config, api, item, onSaved, onClose }: LookupFormDialogProps) {
  const isEdit = Boolean(item)
  const [code, setCode] = useState(item?.code ?? '')
  const [name, setName] = useState(item?.name ?? '')
  const [description, setDescription] = useState(item?.description ?? '')
  const [isActive, setIsActive] = useState(item?.isActive ?? true)
  const [sortOrder, setSortOrder] = useState(String(item?.sortOrder ?? 0))
  const [errors, setErrors] = useState<FormErrors>({})
  const [isSubmitting, setIsSubmitting] = useState(false)

  function validate(): FormErrors {
    const next: FormErrors = {}
    if (!code.trim()) next.code = 'Code is verplicht.'
    else if (code.trim().length > 50) next.code = 'Code mag maximaal 50 tekens bevatten.'
    if (!name.trim()) next.name = 'Naam is verplicht.'
    else if (name.trim().length > 150) next.name = 'Naam mag maximaal 150 tekens bevatten.'
    return next
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const validationErrors = validate()
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors)
      return
    }

    const input: LookupInput = {
      code: code.trim(),
      name: name.trim(),
      description: description.trim() ? description.trim() : null,
      isActive,
      sortOrder: Number.isFinite(Number(sortOrder)) ? Number(sortOrder) : 0,
    }

    setIsSubmitting(true)
    setErrors({})
    try {
      const saved = item ? await api.update(item.id, input) : await api.create(input)
      onSaved(saved, !item)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setErrors({ code: `Er bestaat al een item met code '${input.code}'.` })
      } else {
        setErrors({ form: 'Opslaan is mislukt. Probeer het opnieuw.' })
      }
      setIsSubmitting(false)
    }
  }

  return (
    <Modal
      title={isEdit ? `${config.singular} bewerken` : `Nieuwe ${config.singular}`}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Annuleren
          </Button>
          <Button type="submit" form="lookup-form" disabled={isSubmitting}>
            {isSubmitting ? 'Opslaan...' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <form id="lookup-form" onSubmit={handleSubmit} className="lookup-form">
        {errors.form && (
          <p className="ui-form-field-error" role="alert">
            {errors.form}
          </p>
        )}
        <FormField label="Code" htmlFor="lookup-code" error={errors.code} hint={config.codeHint} required>
          <input
            id="lookup-code"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            aria-invalid={errors.code ? 'true' : undefined}
            maxLength={50}
            autoFocus
          />
        </FormField>
        <FormField label="Naam" htmlFor="lookup-name" error={errors.name} required>
          <input
            id="lookup-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            aria-invalid={errors.name ? 'true' : undefined}
            maxLength={150}
          />
        </FormField>
        <FormField label="Omschrijving" htmlFor="lookup-description">
          <textarea
            id="lookup-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={3}
            maxLength={1000}
          />
        </FormField>
        <FormField label="Volgorde" htmlFor="lookup-sort" hint="Bepaalt de volgorde in keuzelijsten.">
          <input
            id="lookup-sort"
            type="number"
            value={sortOrder}
            onChange={(event) => setSortOrder(event.target.value)}
          />
        </FormField>
        <label className="lookup-form-checkbox">
          <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
          Actief
        </label>
      </form>
    </Modal>
  )
}
