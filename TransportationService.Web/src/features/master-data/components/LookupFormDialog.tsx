import { useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
    if (!code.trim()) next.code = t('masterData.form.codeRequired')
    else if (code.trim().length > 50) next.code = t('masterData.form.codeMax')
    if (!name.trim()) next.name = t('masterData.form.nameRequired')
    else if (name.trim().length > 150) next.name = t('masterData.form.nameMax')
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
        setErrors({ code: t('masterData.errors.duplicateCode', { code: input.code }) })
      } else {
        setErrors({ form: t('masterData.errors.saveFailed') })
      }
      setIsSubmitting(false)
    }
  }

  return (
    <Modal
      title={isEdit ? t('masterData.form.editTitle', { singular: t(config.singular) }) : t('masterData.form.newTitle', { singular: t(config.singular) })}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="lookup-form" disabled={isSubmitting}>
            {isSubmitting ? t('masterData.form.saving') : t('ui.actions.save')}
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
        <FormField label={t('masterData.form.codeLabel')} htmlFor="lookup-code" error={errors.code} hint={config.codeHint ? t(config.codeHint) : undefined} required>
          <input
            id="lookup-code"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            aria-invalid={errors.code ? 'true' : undefined}
            maxLength={50}
            autoFocus
          />
        </FormField>
        <FormField label={t('masterData.form.nameLabel')} htmlFor="lookup-name" error={errors.name} required>
          <input
            id="lookup-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            aria-invalid={errors.name ? 'true' : undefined}
            maxLength={150}
          />
        </FormField>
        <FormField label={t('masterData.form.descriptionLabel')} htmlFor="lookup-description">
          <textarea
            id="lookup-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={3}
            maxLength={1000}
          />
        </FormField>
        <FormField label={t('masterData.form.sortLabel')} htmlFor="lookup-sort" hint={t('masterData.form.sortHint')}>
          <input
            id="lookup-sort"
            type="number"
            value={sortOrder}
            onChange={(event) => setSortOrder(event.target.value)}
          />
        </FormField>
        <label className="lookup-form-checkbox">
          <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
          {t('masterData.form.activeLabel')}
        </label>
      </form>
    </Modal>
  )
}
