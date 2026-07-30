import { useCallback, useEffect, useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
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
import { getPeppolSchemes } from '../../customers/api/customersApi'
import { PeppolFieldGroup } from '../../customers/components/PeppolFieldGroup'
import type { PeppolScheme } from '../../customers/types'
import {
  createLegalEntity,
  deleteLegalEntityLogo,
  listLegalEntities,
  setLegalEntityActive,
  updateLegalEntity,
  uploadLegalEntityLogo,
} from '../api/legalEntitiesApi'
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
  /** Full entity for the logo section (edit mode only). */
  entity: LegalEntity | null
}

/** Beheer van eigen (facturerende) juridische entiteiten. */
export function LegalEntitiesPage() {
  const { showError, showSuccess } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('legal_entities.manage')

  const [entities, setEntities] = useState<LegalEntity[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editor, setEditor] = useState<EditorState | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [busy, setBusy] = useState(false)
  const [deactivating, setDeactivating] = useState<LegalEntity | null>(null)
  const logoInputRef = useRef<HTMLInputElement | null>(null)

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
        if (!cancelled) setLoadError(describeApiError(error, 'Eigen bedrijven konden niet worden geladen.').message)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [reloadToken])

  // Loading is flipped here (not inside the effect) so the effect only synchronises with the API.
  const reload = useCallback(() => {
    setLoading(true)
    setReloadToken((token) => token + 1)
  }, [])

  function openCreate() {
    setFormError(null)
    setFieldErrors({})
    setEditor({ id: null, form: EMPTY_FORM, entity: null })
  }

  function openEdit(entity: LegalEntity) {
    setFormError(null)
    setFieldErrors({})
    setEditor({ id: entity.id, form: toInput(entity), entity })
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
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      if (editor.id) {
        await updateLegalEntity(editor.id, editor.form)
      } else {
        await createLegalEntity(editor.form)
      }
      showSuccess('Bedrijf opgeslagen.')
      setEditor(null)
      reload()
    } catch (error) {
      const described = describeApiError(error, 'Het bedrijf kon niet worden opgeslagen.')
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  async function handleDeactivate() {
    if (!deactivating) return
    setBusy(true)
    try {
      await setLegalEntityActive(deactivating.id, false)
      showSuccess('Bedrijf gedeactiveerd.')
      setDeactivating(null)
      reload()
    } catch (error) {
      // Surface backend rules (e.g. "de standaardentiteit kan niet worden gedeactiveerd").
      showError(describeApiError(error, 'Het bedrijf kon niet worden gedeactiveerd.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleReactivate(entity: LegalEntity) {
    setBusy(true)
    try {
      await setLegalEntityActive(entity.id, true)
      showSuccess('Bedrijf geheractiveerd.')
      reload()
    } catch (error) {
      showError(describeApiError(error, 'Het bedrijf kon niet worden geheractiveerd.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleLogoUpload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file || !editor?.id) return
    setBusy(true)
    try {
      const updated = await uploadLegalEntityLogo(editor.id, file)
      setEditor((current) => (current ? { ...current, entity: updated } : current))
      showSuccess('Logo geüpload.')
      reload()
    } catch (error) {
      showError(describeApiError(error, 'Het logo kon niet worden geüpload.').message)
    } finally {
      setBusy(false)
      if (logoInputRef.current) logoInputRef.current.value = ''
    }
  }

  async function handleLogoDelete() {
    if (!editor?.id) return
    setBusy(true)
    try {
      const updated = await deleteLegalEntityLogo(editor.id)
      setEditor((current) => (current ? { ...current, entity: updated } : current))
      showSuccess('Logo verwijderd.')
      reload()
    } catch (error) {
      showError(describeApiError(error, 'Het logo kon niet worden verwijderd.').message)
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<LegalEntity>[] = [
    {
      key: 'name',
      header: 'Naam',
      render: (row) => (
        <div className="le-name">
          <span className="le-name-legal">{row.legalName}</span>
          {row.tradingName && <span className="le-name-trading">{row.tradingName}</span>}
        </div>
      ),
    },
    { key: 'vat', header: 'BTW-nummer', render: (row) => row.vatNumber ?? '—' },
    {
      key: 'default',
      header: 'Standaard',
      render: (row) => (row.isDefault ? <Badge tone="info">Standaard</Badge> : null),
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="danger">Inactief</Badge>),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (row: LegalEntity) => (
              <div className="le-actions">
                <button type="button" className="le-link" onClick={() => openEdit(row)}>
                  Bewerken
                </button>
                {row.isActive ? (
                  <button type="button" className="le-link le-link-danger" onClick={() => setDeactivating(row)}>
                    Deactiveren
                  </button>
                ) : (
                  <button type="button" className="le-link" onClick={() => void handleReactivate(row)}>
                    Heractiveren
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
      <Breadcrumbs items={[{ label: 'Eigen bedrijven' }]} />
      <PageHeader
        title="Eigen bedrijven"
        subtitle="Facturerende entiteiten van deze organisatie."
        action={canManage ? <Button onClick={openCreate}>Nieuw bedrijf</Button> : undefined}
      />

      <DataTable
        columns={columns}
        rows={entities}
        rowKey={(row) => row.id}
        isLoading={loading}
        error={loadError}
        emptyMessage="Nog geen eigen bedrijven aangemaakt."
        loadingMessage="Eigen bedrijven laden..."
      />

      {editor && (
        <Modal
          title={editor.id ? 'Bedrijf bewerken' : 'Nieuw bedrijf'}
          onClose={() => setEditor(null)}
          busy={busy}
        >
          <form className="le-form" onSubmit={(event) => void handleSubmit(event)}>
            {formError && (
              <p className="le-form-error" role="alert">
                {formError}
              </p>
            )}

            <FormSection title="Identiteit">
              {text('legalName', 'Juridische naam', { required: true, maxLength: 200 })}
              {text('tradingName', 'Handelsnaam', { maxLength: 200 })}
              {text('companyNumber', 'Ondernemingsnummer', { maxLength: 50 })}
              {text('vatNumber', 'BTW-nummer', { maxLength: 50 })}
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

            <FormSection title="Adres">
              {text('street', 'Straat', { maxLength: 200 })}
              {text('houseNumber', 'Nummer', { maxLength: 20 })}
              {text('postalCode', 'Postcode', { maxLength: 20 })}
              {text('city', 'Gemeente', { maxLength: 100 })}
              <FormField
                label="Land"
                htmlFor="le-countryCode"
                hint="Tweeletterige landcode (bv. BE)."
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

            <FormSection title="Contact">
              {text('email', 'E-mail', { maxLength: 200 })}
              {text('phoneNumber', 'Telefoon', { maxLength: 50 })}
              {text('website', 'Website', { maxLength: 200 })}
            </FormSection>

            <FormSection title="Bank">
              {text('iban', 'IBAN', { maxLength: 50 })}
              {text('bic', 'BIC', { maxLength: 20 })}
              {text('bankName', 'Banknaam', { maxLength: 100 })}
            </FormSection>

            <FormSection title="Facturatie">
              {text('defaultCurrency', 'Valuta (ISO)', { maxLength: 3 })}
              <FormField
                label="Betaaltermijn (dagen)"
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
              {text('invoiceNumberFormat', 'Factuurnummerformaat', {
                maxLength: 100,
                hint: 'Tokens: {YYYY} {YY} {MM} {SEQ} {PREFIX}',
              })}
              <FormField
                label="Volgnummer-breedte"
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
              {text('invoicePrefix', 'Voorvoegsel', { maxLength: 20 })}
              <FormField
                label="Factuurvoettekst"
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
                Standaardentiteit
              </label>
            </FormSection>

            {editor.id && (
              <FormSection title="Logo" columns={1}>
                <div className="le-logo">
                  {editor.entity?.hasLogo ? (
                    <p className="le-logo-current">
                      Huidig logo: <strong>{editor.entity.logoFileName ?? 'logo'}</strong>
                    </p>
                  ) : (
                    <p className="le-logo-current">Nog geen logo geüpload.</p>
                  )}
                  <div className="le-logo-actions">
                    <input
                      ref={logoInputRef}
                      type="file"
                      accept=".png,.jpg,.jpeg,.svg"
                      disabled={busy}
                      aria-label="Logo uploaden"
                      onChange={(event) => void handleLogoUpload(event)}
                    />
                    {editor.entity?.hasLogo && (
                      <Button variant="danger" onClick={() => void handleLogoDelete()} disabled={busy}>
                        Logo verwijderen
                      </Button>
                    )}
                  </div>
                  <p className="le-logo-hint">PNG, JPG of SVG, maximaal 2 MB.</p>
                </div>
              </FormSection>
            )}

            <div className="le-form-actions">
              <Button variant="secondary" type="button" onClick={() => setEditor(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" disabled={busy}>
                {busy ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {deactivating && (
        <ConfirmDialog
          title="Bedrijf deactiveren"
          message={`Weet je zeker dat je "${deactivating.legalName}" wilt deactiveren? Het bedrijf is daarna niet meer selecteerbaar voor nieuwe documenten.`}
          confirmLabel="Deactiveren"
          destructive
          busy={busy}
          onConfirm={() => void handleDeactivate()}
          onCancel={() => setDeactivating(null)}
        />
      )}
    </div>
  )
}
