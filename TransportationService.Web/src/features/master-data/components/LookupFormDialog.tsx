import { useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { ApiError } from '../../../api/apiClient'
import { describeApiError, getFieldError } from '../../../api/problemDetails'
import { useDirtyDialogClose } from '../../settings/components/useDirtyDialogClose'
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
  const simple = Boolean(config.simpleForm)
  // Simple lookups fold code + sort order away; a code error must still be visible, so the
  // disclosure opens itself in that case.
  const [advancedOpen, setAdvancedOpen] = useState(false)
  // Escape, × and Annuleren ask before discarding typed input (shared dialog close model).
  const dirty =
    code !== (item?.code ?? '') ||
    name !== (item?.name ?? '') ||
    description !== (item?.description ?? '') ||
    isActive !== (item?.isActive ?? true) ||
    sortOrder !== String(item?.sortOrder ?? 0)
  const { requestClose, guard } = useDirtyDialogClose(dirty, isSubmitting, onClose)

  function validate(): FormErrors {
    const next: FormErrors = {}
    // Code is optional on create: an empty code asks the backend to generate the next free
    // unique one. Editing keeps the existing code mandatory (the row already owns one).
    if (isEdit && !code.trim()) next.code = t('masterData.form.codeRequired')
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
      if (validationErrors.code) setAdvancedOpen(true)
      return
    }

    const input: LookupInput = {
      code: code.trim() || null,
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
        setErrors({ code: t('masterData.errors.duplicateCode', { code: input.code ?? '' }) })
        setAdvancedOpen(true)
      } else if (error instanceof ApiError && error.status === 400) {
        // Server rules (e.g. a used code cannot change) arrive as Dutch field errors.
        const described = describeApiError(error, t('masterData.errors.saveFailed'))
        const codeError = getFieldError(described.fieldErrors, 'code')
        const nameError = getFieldError(described.fieldErrors, 'name')
        setErrors({ code: codeError, name: nameError, form: codeError || nameError ? undefined : described.message })
        if (codeError) setAdvancedOpen(true)
      } else {
        setErrors({ form: t('masterData.errors.saveFailed') })
      }
      setIsSubmitting(false)
    }
  }

  const codeField = (
    <FormField
      label={t('masterData.form.codeLabel')}
      htmlFor="lookup-code"
      error={errors.code}
      hint={[config.codeHint ? t(config.codeHint) : null, isEdit ? null : t('masterData.select.codeAutoHint')].filter(Boolean).join(' ') || undefined}
      required={isEdit}
    >
      <input
        id="lookup-code"
        value={code}
        onChange={(event) => setCode(event.target.value)}
        aria-invalid={errors.code ? 'true' : undefined}
        maxLength={50}
        autoFocus={isEdit && !simple}
      />
    </FormField>
  )
  const nameField = (
    <FormField label={t('masterData.form.nameLabel')} htmlFor="lookup-name" error={errors.name} required>
      <input
        id="lookup-name"
        value={name}
        onChange={(event) => setName(event.target.value)}
        aria-invalid={errors.name ? 'true' : undefined}
        maxLength={150}
        autoFocus={!isEdit || simple}
      />
    </FormField>
  )
  const descriptionField = (
    <FormField label={t('masterData.form.descriptionLabel')} htmlFor="lookup-description">
      <textarea
        id="lookup-description"
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        rows={3}
        maxLength={1000}
      />
    </FormField>
  )
  const sortField = (
    <FormField label={t('masterData.form.sortLabel')} htmlFor="lookup-sort" hint={t('masterData.form.sortHint')}>
      <input
        id="lookup-sort"
        type="number"
        value={sortOrder}
        onChange={(event) => setSortOrder(event.target.value)}
      />
    </FormField>
  )
  const activeField = (
    <label className="lookup-form-checkbox">
      <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
      {t('masterData.form.activeLabel')}
    </label>
  )

  return (
    <>
      <Modal
        title={isEdit ? t('masterData.form.editTitle', { singular: t(config.singular) }) : t(`masterData.newLabel.${config.slug}`)}
        onClose={requestClose}
        busy={isSubmitting}
        footer={
          <>
            <Button variant="secondary" onClick={requestClose} disabled={isSubmitting}>
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
          {!simple && codeField}
          {nameField}
          {descriptionField}
          {simple && activeField}
          {simple ? (
            <details className="lookup-form-advanced" open={advancedOpen}>
              <summary
                onClick={(event) => {
                  // Controlled by React state so a code error can open it; the native toggle
                  // would fight that, hence preventDefault.
                  event.preventDefault()
                  setAdvancedOpen((open) => !open)
                }}
              >
                {t('masterData.form.advanced')}
              </summary>
              <p className="lookup-form-advanced-hint">{t('masterData.form.advancedHint')}</p>
              {codeField}
              {sortField}
            </details>
          ) : (
            <>
              {sortField}
              {activeField}
            </>
          )}
        </form>
      </Modal>
      {guard}
    </>
  )
}
