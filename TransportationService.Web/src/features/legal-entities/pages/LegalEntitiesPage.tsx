import { useCallback, useEffect, useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { isConcurrencyConflict } from '../../../api/conflicts'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getPeppolSchemes } from '../../customers/api/customersApi'
import { PeppolFieldGroup } from '../../customers/components/PeppolFieldGroup'
import type { PeppolScheme } from '../../customers/types'
import { ConcurrencyConflictNotice } from '../../settings/components/ConcurrencyConflictNotice'
import { useDirtyDialogClose } from '../../settings/components/useDirtyDialogClose'
import {
  createLegalEntity,
  deleteLegalEntityHeaderImage,
  deleteLegalEntityLogo,
  getLegalEntity,
  listLegalEntities,
  setLegalEntityActive,
  updateLegalEntity,
  uploadLegalEntityHeaderImage,
  uploadLegalEntityLogo,
} from '../api/legalEntitiesApi'
import { forgetEntityHeaderImage } from '../components/useEntityHeaderImage'
import type { LegalEntity, SaveLegalEntityInput } from '../types'
import './legal-entities.css'

const EMPTY_FORM: SaveLegalEntityInput = {
  legalName: '',
  tradingName: null,
  companyNumber: null,
  vatNumber: null,
  peppolId: null,
  peppolScheme: null,
  street: null,
  houseNumber: null,
  postalCode: null,
  city: null,
  countryCode: null,
  email: null,
  phoneNumber: null,
  website: null,
  iban: null,
  bic: null,
  bankName: null,
  defaultCurrency: 'EUR',
  paymentTermDays: 30,
  invoiceNumberFormat: '{YYYY}{MM}{SEQ}',
  invoiceSequencePadding: 4,
  invoicePrefix: null,
  creditNotePrefix: null,
  invoiceFooter: null,
  isDefault: false,
}

function toInput(entity: LegalEntity): SaveLegalEntityInput {
  return {
    legalName: entity.legalName,
    tradingName: entity.tradingName,
    companyNumber: entity.companyNumber,
    vatNumber: entity.vatNumber,
    peppolId: entity.peppolId,
    peppolScheme: entity.peppolScheme,
    street: entity.street,
    houseNumber: entity.houseNumber,
    postalCode: entity.postalCode,
    city: entity.city,
    countryCode: entity.countryCode,
    email: entity.email,
    phoneNumber: entity.phoneNumber,
    website: entity.website,
    iban: entity.iban,
    bic: entity.bic,
    bankName: entity.bankName,
    defaultCurrency: entity.defaultCurrency,
    paymentTermDays: entity.paymentTermDays,
    invoiceNumberFormat: entity.invoiceNumberFormat,
    invoiceSequencePadding: entity.invoiceSequencePadding,
    invoicePrefix: entity.invoicePrefix,
    creditNotePrefix: entity.creditNotePrefix,
    invoiceFooter: entity.invoiceFooter,
    isDefault: entity.isDefault,
  }
}

type TextField = Exclude<
  {
    [K in keyof SaveLegalEntityInput]: SaveLegalEntityInput[K] extends string | null ? K : never
  }[keyof SaveLegalEntityInput],
  undefined
>

interface EditorState {
  /** null = create. */
  id: string | null
  form: SaveLegalEntityInput
  /** Full entity for the logo section and the concurrency version (edit mode only). */
  entity: LegalEntity | null
  /** MD-M2: JSON of the form as opened — drives the discard confirmation. */
  baseline: string
  /** MD-M10: the logo chosen in the dialog; uploaded only after Opslaan stored the entity. */
  pendingLogo: File | null
  /** MD-M10: "Logo verwijderen" was pressed; applied on Opslaan, never immediately. */
  removeLogo: boolean
  /** Same staged flow as the logo: applied on Opslaan. */
  pendingHeaderImage: File | null
  removeHeaderImage: boolean
}

function editorFor(entity: LegalEntity | null): EditorState {
  const form = entity ? toInput(entity) : EMPTY_FORM
  return {
    id: entity?.id ?? null,
    form,
    entity,
    baseline: JSON.stringify(form),
    pendingLogo: null,
    removeLogo: false,
    pendingHeaderImage: null,
    removeHeaderImage: false,
  }
}

/** Beheer van eigen (facturerende) juridische entiteiten. */
export function LegalEntitiesPage() {
  const { showError, showSuccess } = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('legal_entities.manage')

  const [entities, setEntities] = useState<LegalEntity[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editor, setEditor] = useState<EditorState | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  // MD-M2: the entity was saved by someone else since the dialog opened (409).
  const [conflict, setConflict] = useState(false)
  const [busy, setBusy] = useState(false)
  const [deactivating, setDeactivating] = useState<LegalEntity | null>(null)
  const logoInputRef = useRef<HTMLInputElement | null>(null)
  const headerImageInputRef = useRef<HTMLInputElement>(null)

  const editorDirty =
    editor !== null &&
    (JSON.stringify(editor.form) !== editor.baseline || editor.pendingLogo !== null || editor.removeLogo || editor.pendingHeaderImage !== null || editor.removeHeaderImage)
  const editorClose = useDirtyDialogClose(editorDirty, busy, () => setEditor(null))

  // Authoritative Peppol scheme catalog for the grouped Peppol control (same source as customers).
  const [peppolSchemes, setPeppolSchemes] = useState<PeppolScheme[]>([])
  useEffect(() => {
    let cancelled = false
    getPeppolSchemes()
      .then((data) => {
        if (!cancelled) setPeppolSchemes(data)
      })
      .catch(() => {
        /* fallback: lege schemalijst, handmatige invoer blijft mogelijk */
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    listLegalEntities(true)
      .then((data) => {
        if (cancelled) return
        setEntities(data)
        setLoadError(null)
      })
      .catch((error: unknown) => {
        if (!cancelled) setLoadError(describeApiError(error, t('legalEntities.page.loadFailed')).message)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [reloadToken, t])

  // Loading is flipped here (not inside the effect) so the effect only synchronises with the API.
  const reload = useCallback(() => {
    setLoading(true)
    setReloadToken((token) => token + 1)
  }, [])

  function openEditor(entity: LegalEntity | null) {
    setFormError(null)
    setFieldErrors({})
    setConflict(false)
    setEditor(editorFor(entity))
  }

  function setField<K extends keyof SaveLegalEntityInput>(key: K, value: SaveLegalEntityInput[K]) {
    setEditor((current) => (current ? { ...current, form: { ...current.form, [key]: value } } : current))
  }

  function text(key: TextField, label: string, opts?: { required?: boolean; maxLength?: number; hint?: string }): ReactNode {
    const value = editor?.form[key] ?? ''
    return (
      <FormField
        label={label}
        htmlFor={`le-${key}`}
        required={opts?.required}
        hint={opts?.hint}
        error={getFieldError(fieldErrors, key)}
      >
        <input
          id={`le-${key}`}
          type="text"
          value={value}
          required={opts?.required}
          maxLength={opts?.maxLength}
          disabled={busy}
          onChange={(event) => setField(key, event.target.value === '' ? null : event.target.value)}
        />
      </FormField>
    )
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!editor) return
    const { pendingLogo, removeLogo, pendingHeaderImage, removeHeaderImage } = editor
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    setConflict(false)
    try {
      let saved: LegalEntity
      try {
        saved = editor.id
          ? await updateLegalEntity(editor.id, { ...editor.form, expectedVersion: editor.entity?.version })
          : await createLegalEntity(editor.form)
      } catch (error) {
        if (isConcurrencyConflict(error)) {
          setConflict(true)
        } else {
          // Server rules (IBAN mod-97, BIC, Peppol scheme+id, …) land next to their fields.
          const described = describeApiError(error, t('legalEntities.page.saveFailed'))
          setFormError(described.message)
          setFieldErrors(described.fieldErrors)
        }
        return
      }

      // MD-M10: the logo change follows only once the entity data is stored.
      try {
        if (pendingLogo) saved = await uploadLegalEntityLogo(saved.id, pendingLogo)
        else if (removeLogo && saved.hasLogo) saved = await deleteLegalEntityLogo(saved.id)
      } catch (error) {
        // The entity IS saved: stay open on the saved version with the logo choice still pending,
        // so pressing Opslaan again retries just the logo.
        const fallback = pendingLogo ? t('legalEntities.page.logoUploadFailed') : t('legalEntities.page.logoRemoveFailed')
        setEditor({ ...editorFor(saved), pendingLogo, removeLogo, pendingHeaderImage, removeHeaderImage })
        setFormError(t('legalEntities.page.savedLogoFailed', { message: describeApiError(error, fallback).message }))
        reload()
        return
      }

      // Likewise for the banner photo; a failure keeps only that choice pending.
      try {
        if (pendingHeaderImage) saved = await uploadLegalEntityHeaderImage(saved.id, pendingHeaderImage)
        else if (removeHeaderImage && saved.hasHeaderImage) saved = await deleteLegalEntityHeaderImage(saved.id)
        if (pendingHeaderImage || removeHeaderImage) forgetEntityHeaderImage(saved.id)
      } catch (error) {
        const fallback = t(pendingHeaderImage ? 'legalEntities.page.headerImageUploadFailed' : 'legalEntities.page.headerImageRemoveFailed')
        setEditor({ ...editorFor(saved), pendingHeaderImage, removeHeaderImage })
        setFormError(t('legalEntities.page.savedHeaderImageFailed', { message: describeApiError(error, fallback).message }))
        reload()
        return
      }

      showSuccess(t('legalEntities.page.saved'))
      setEditor(null)
      reload()
    } finally {
      setBusy(false)
    }
  }

  // MD-M2: replace the draft with the current server version after a 409.
  async function reloadEditor() {
    if (!editor?.id) return
    setBusy(true)
    try {
      const fresh = await getLegalEntity(editor.id)
      setConflict(false)
      setFormError(null)
      setFieldErrors({})
      setEditor(editorFor(fresh))
      reload()
    } catch (error) {
      setFormError(describeApiError(error, t('legalEntities.page.loadFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDeactivate() {
    if (!deactivating) return
    setBusy(true)
    try {
      await setLegalEntityActive(deactivating.id, false)
      showSuccess(t('legalEntities.page.deactivated'))
      setDeactivating(null)
      reload()
    } catch (error) {
      // Surface backend rules (e.g. "de standaardentiteit kan niet worden gedeactiveerd").
      showError(describeApiError(error, t('legalEntities.page.deactivateFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleReactivate(entity: LegalEntity) {
    setBusy(true)
    try {
      await setLegalEntityActive(entity.id, true)
      showSuccess(t('legalEntities.page.reactivated'))
      reload()
    } catch (error) {
      showError(describeApiError(error, t('legalEntities.page.reactivateFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  // MD-M10: choosing a file only stages it; Opslaan uploads it (Annuleren discards it).
  function handleLogoChosen(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null
    if (!file) return
    setEditor((current) => (current ? { ...current, pendingLogo: file, removeLogo: false } : current))
  }

  function handleLogoRemove() {
    if (logoInputRef.current) logoInputRef.current.value = ''
    setEditor((current) => (current ? { ...current, pendingLogo: null, removeLogo: true } : current))
  }

  function handleLogoUndo() {
    if (logoInputRef.current) logoInputRef.current.value = ''
    setEditor((current) => (current ? { ...current, pendingLogo: null, removeLogo: false } : current))
  }

  function handleHeaderImageChosen(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null
    if (!file) return
    setEditor((current) => (current ? { ...current, pendingHeaderImage: file, removeHeaderImage: false } : current))
  }

  function setHeaderImageRemoval(removeHeaderImage: boolean) {
    if (headerImageInputRef.current) headerImageInputRef.current.value = ''
    setEditor((current) => (current ? { ...current, pendingHeaderImage: null, removeHeaderImage } : current))
  }

  const columns: Column<LegalEntity>[] = [
    {
      key: 'name',
      header: t('legalEntities.page.columnName'),
      render: (row) => (
        <div className="le-name">
          <span className="le-name-legal">{row.legalName}</span>
          {row.tradingName && <span className="le-name-trading">{row.tradingName}</span>}
        </div>
      ),
    },
    { key: 'vat', header: t('legalEntities.page.columnVat'), render: (row) => row.vatNumber ?? '—' },
    {
      key: 'default',
      header: t('legalEntities.page.columnDefault'),
      render: (row) => (row.isDefault ? <Badge tone="info">{t('legalEntities.page.defaultBadge')}</Badge> : null),
    },
    {
      key: 'status',
      header: t('legalEntities.page.columnStatus'),
      render: (row) =>
        row.isActive ? (
          <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
        ) : (
          <Badge tone="danger">{t('ui.statusBadges.inactive')}</Badge>
        ),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (row: LegalEntity) => (
              <div className="le-actions">
                <button type="button" className="le-link" onClick={() => openEditor(row)}>
                  {t('ui.actions.edit')}
                </button>
                {row.isActive ? (
                  <button type="button" className="le-link le-link-danger" onClick={() => setDeactivating(row)}>
                    {t('legalEntities.page.deactivateAction')}
                  </button>
                ) : (
                  <button type="button" className="le-link" onClick={() => void handleReactivate(row)}>
                    {t('legalEntities.page.reactivateAction')}
                  </button>
                )}
              </div>
            ),
          } satisfies Column<LegalEntity>,
        ]
      : []),
  ]

  return (
    <div className="le-page">
      <PageHeader
        title={t('navigation.menu.legalEntities')}
        subtitle={t('legalEntities.page.subtitle')}
        action={canManage ? <Button onClick={() => openEditor(null)}>{t('legalEntities.page.newEntity')}</Button> : undefined}
      />

      <DataTable
        columns={columns}
        rows={entities}
        rowKey={(row) => row.id}
        isLoading={loading}
        error={loadError}
        emptyMessage={t('legalEntities.page.empty')}
        loadingMessage={t('legalEntities.page.loading')}
      />

      {editor && (
        <Modal
          title={editor.id ? t('legalEntities.page.editTitle') : t('legalEntities.page.newEntity')}
          onClose={editorClose.requestClose}
          busy={busy}
        >
          <form className="le-form" onSubmit={(event) => void handleSubmit(event)}>
            {conflict && <ConcurrencyConflictNotice onReload={() => void reloadEditor()} busy={busy} />}
            {formError && (
              <p className="le-form-error" role="alert">
                {formError}
              </p>
            )}

            <FormSection title={t('legalEntities.form.sectionIdentity')}>
              {text('legalName', t('legalEntities.form.legalName'), { required: true, maxLength: 200 })}
              {text('tradingName', t('legalEntities.form.tradingName'), { maxLength: 200 })}
              {text('companyNumber', t('legalEntities.form.companyNumber'), { maxLength: 50 })}
              {text('vatNumber', t('legalEntities.form.vatNumber'), { maxLength: 50 })}
              <div className="form-span-all">
                <PeppolFieldGroup
                  scheme={editor.form.peppolScheme ?? ''}
                  participantId={editor.form.peppolId ?? ''}
                  status={editor.form.peppolId || editor.form.peppolScheme ? 'manual' : 'not-validated'}
                  schemes={peppolSchemes}
                  disabled={busy}
                  error={getFieldError(fieldErrors, 'peppolId') ?? getFieldError(fieldErrors, 'peppolScheme')}
                  onChange={(next) =>
                    setEditor((current) =>
                      current
                        ? {
                            ...current,
                            form: {
                              ...current.form,
                              peppolScheme: next.scheme === '' ? null : next.scheme,
                              peppolId: next.participantId === '' ? null : next.participantId,
                            },
                          }
                        : current,
                    )
                  }
                />
              </div>
            </FormSection>

            <FormSection title={t('legalEntities.form.sectionAddress')}>
              {text('street', t('legalEntities.form.street'), { maxLength: 200 })}
              {text('houseNumber', t('legalEntities.form.houseNumber'), { maxLength: 20 })}
              {text('postalCode', t('legalEntities.form.postalCode'), { maxLength: 20 })}
              {text('city', t('legalEntities.form.city'), { maxLength: 100 })}
              <FormField
                label={t('legalEntities.form.country')}
                htmlFor="le-countryCode"
                hint={t('legalEntities.form.countryHint')}
                error={getFieldError(fieldErrors, 'countryCode')}
              >
                <input
                  id="le-countryCode"
                  type="text"
                  value={editor.form.countryCode ?? ''}
                  maxLength={2}
                  disabled={busy}
                  onChange={(event) =>
                    setField('countryCode', event.target.value === '' ? null : event.target.value.toUpperCase())
                  }
                />
              </FormField>
            </FormSection>

            <FormSection title={t('legalEntities.form.sectionContact')}>
              {text('email', t('legalEntities.form.email'), { maxLength: 200 })}
              {text('phoneNumber', t('legalEntities.form.phone'), { maxLength: 50 })}
              {text('website', t('legalEntities.form.website'), { maxLength: 200 })}
            </FormSection>

            <FormSection title={t('legalEntities.form.sectionBank')}>
              {text('iban', t('legalEntities.form.iban'), { maxLength: 50 })}
              {text('bic', t('legalEntities.form.bic'), { maxLength: 20 })}
              {text('bankName', t('legalEntities.form.bankName'), { maxLength: 100 })}
            </FormSection>

            <FormSection title={t('legalEntities.form.sectionBilling')}>
              {text('defaultCurrency', t('legalEntities.form.currency'), { maxLength: 3 })}
              <FormField
                label={t('legalEntities.form.paymentTermDays')}
                htmlFor="le-paymentTermDays"
                error={getFieldError(fieldErrors, 'paymentTermDays')}
              >
                <input
                  id="le-paymentTermDays"
                  type="number"
                  min={0}
                  max={365}
                  value={editor.form.paymentTermDays}
                  disabled={busy}
                  onChange={(event) => setField('paymentTermDays', Number(event.target.value) || 0)}
                />
              </FormField>
              {text('invoiceNumberFormat', t('legalEntities.form.invoiceNumberFormat'), {
                maxLength: 100,
                // Tokens zijn technisch contract; de hint blijft bewust letterlijk in elke taal.
                hint: t('legalEntities.form.invoiceNumberFormatHint'),
              })}
              <FormField
                label={t('legalEntities.form.sequencePadding')}
                htmlFor="le-invoiceSequencePadding"
                error={getFieldError(fieldErrors, 'invoiceSequencePadding')}
              >
                <input
                  id="le-invoiceSequencePadding"
                  type="number"
                  min={2}
                  max={8}
                  value={editor.form.invoiceSequencePadding}
                  disabled={busy}
                  onChange={(event) => setField('invoiceSequencePadding', Number(event.target.value) || 4)}
                />
              </FormField>
              {text('invoicePrefix', t('legalEntities.form.invoicePrefix'), { maxLength: 20 })}
              {text('creditNotePrefix', t('legalEntities.form.creditNotePrefix'), {
                maxLength: 20,
                hint: t('legalEntities.form.creditNotePrefixHint'),
              })}
              <FormField
                label={t('legalEntities.form.invoiceFooter')}
                htmlFor="le-invoiceFooter"
                className="form-span-all"
                error={getFieldError(fieldErrors, 'invoiceFooter')}
              >
                <textarea
                  id="le-invoiceFooter"
                  rows={3}
                  value={editor.form.invoiceFooter ?? ''}
                  disabled={busy}
                  onChange={(event) => setField('invoiceFooter', event.target.value === '' ? null : event.target.value)}
                />
              </FormField>
              <label className="le-check form-span-all">
                <input
                  type="checkbox"
                  checked={editor.form.isDefault}
                  disabled={busy}
                  onChange={(event) => setField('isDefault', event.target.checked)}
                />
                {t('legalEntities.form.defaultEntity')}
              </label>
            </FormSection>

            <FormSection title={t('legalEntities.form.sectionLogo')} columns={1}>
              <div className="le-logo">
                {editor.pendingLogo ? (
                  <p className="le-logo-current">
                    {t('legalEntities.form.logoPending')} <strong>{editor.pendingLogo.name}</strong>
                  </p>
                ) : editor.removeLogo ? (
                  <p className="le-logo-current">{t('legalEntities.form.logoPendingRemoval')}</p>
                ) : editor.entity?.hasLogo ? (
                  <p className="le-logo-current">
                    {t('legalEntities.form.currentLogo')} <strong>{editor.entity.logoFileName ?? 'logo'}</strong>
                  </p>
                ) : (
                  <p className="le-logo-current">{t('legalEntities.form.noLogoYet')}</p>
                )}
                <div className="le-logo-actions">
                  <input
                    ref={logoInputRef}
                    type="file"
                    accept=".png,.jpg,.jpeg,.svg"
                    disabled={busy}
                    aria-label={t('legalEntities.form.logoUploadAria')}
                    onChange={handleLogoChosen}
                  />
                  {editor.pendingLogo || editor.removeLogo ? (
                    <Button variant="secondary" onClick={handleLogoUndo} disabled={busy}>
                      {t('legalEntities.form.logoUndo')}
                    </Button>
                  ) : (
                    editor.entity?.hasLogo && (
                      <Button variant="danger" onClick={handleLogoRemove} disabled={busy}>
                        {t('legalEntities.form.logoDelete')}
                      </Button>
                    )
                  )}
                </div>
                <p className="le-logo-hint">{t('legalEntities.form.logoHint')}</p>
                <p className="le-logo-hint">{t('legalEntities.form.logoAppliedOnSave')}</p>
              </div>
            </FormSection>

            <FormSection title={t('legalEntities.form.sectionHeaderImage')} columns={1}>
              <div className="le-logo">
                {editor.pendingHeaderImage ? (
                  <p className="le-logo-current">
                    {t('legalEntities.form.headerImagePending')} <strong>{editor.pendingHeaderImage.name}</strong>
                  </p>
                ) : editor.removeHeaderImage ? (
                  <p className="le-logo-current">{t('legalEntities.form.headerImagePendingRemoval')}</p>
                ) : editor.entity?.hasHeaderImage ? (
                  <p className="le-logo-current">
                    {t('legalEntities.form.currentHeaderImage')} <strong>{editor.entity.headerImageFileName ?? 'banner'}</strong>
                  </p>
                ) : (
                  <p className="le-logo-current">{t('legalEntities.form.noHeaderImageYet')}</p>
                )}
                <div className="le-logo-actions">
                  <input
                    ref={headerImageInputRef}
                    type="file"
                    accept=".png,.jpg,.jpeg"
                    disabled={busy}
                    aria-label={t('legalEntities.form.headerImageUploadAria')}
                    onChange={handleHeaderImageChosen}
                  />
                  {editor.pendingHeaderImage || editor.removeHeaderImage ? (
                    <Button variant="secondary" onClick={() => setHeaderImageRemoval(false)} disabled={busy}>
                      {t('legalEntities.form.logoUndo')}
                    </Button>
                  ) : (
                    editor.entity?.hasHeaderImage && (
                      <Button variant="danger" onClick={() => setHeaderImageRemoval(true)} disabled={busy}>
                        {t('legalEntities.form.headerImageDelete')}
                      </Button>
                    )
                  )}
                </div>
                <p className="le-logo-hint">{t('legalEntities.form.headerImageHint')}</p>
              </div>
            </FormSection>

            <div className="le-form-actions">
              <Button variant="secondary" type="button" onClick={editorClose.requestClose} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" disabled={busy}>
                {busy ? t('legalEntities.page.savingBusy') : t('ui.actions.save')}
              </Button>
            </div>
          </form>
        </Modal>
      )}
      {editor && editorClose.guard}

      {deactivating && (
        <ConfirmDialog
          title={t('legalEntities.page.deactivateTitle')}
          message={t('legalEntities.page.deactivateMessage', { name: deactivating.legalName })}
          confirmLabel={t('legalEntities.page.deactivateAction')}
          destructive
          busy={busy}
          onConfirm={() => void handleDeactivate()}
          onCancel={() => setDeactivating(null)}
        />
      )}
    </div>
  )
}
